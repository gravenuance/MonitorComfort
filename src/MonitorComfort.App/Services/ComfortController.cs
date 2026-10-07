using MonitorComfort.App.Ambient;
using MonitorComfort.App.Monitors;
using MonitorComfort.Core.Colour;
using MonitorComfort.Core.Context;
using MonitorComfort.Core.Monitors;
using MonitorComfort.Core.Planning;
using MonitorComfort.Core.Settings;
using Serilog;

namespace MonitorComfort.App.Services;

public enum ControllerState
{
    Starting,
    Active,
    Paused,
    NeedsLocation,
    NoMonitors,
}

public sealed record DetectedMonitor(string Id, string Name, MonitorSupport Support);

public sealed record ComfortStatus(
    ControllerState State,
    ComfortPlan? Plan,
    IReadOnlyList<DetectedMonitor> Monitors,
    DateTimeOffset? PausedUntil)
{
    public static ComfortStatus Initial { get; } = new(ControllerState.Starting, null, [], null);
}

/// <summary>
/// The only code that talks to the monitors: a background loop that watches for full-screen
/// windows and display changes every second and re-plans every minute, or at once when asked.
/// </summary>
internal sealed class ComfortController : IAsyncDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FullscreenSettle = TimeSpan.FromSeconds(3);
    private const int NoPreview = -1;

    private readonly SettingsService _settings;
    private readonly DdcMonitorService _ddc;
    private readonly FullscreenDetector _detector;
    private readonly VersionedJsonFile<OriginalMonitorValues> _originalsFile;
    private readonly AmbientLightService _ambient;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly SettledValue<string?> _fullscreen;
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, MonitorLevels> _lastWritten = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MonitorSupport> _support = new(StringComparer.OrdinalIgnoreCase);
    private readonly ReplanSchedule _schedule = new();

    private Task _loop = Task.CompletedTask;
    private string? _topology;
    private OriginalMonitorValues _originals;
    private int _applyRequested;
    private int _previewBrightness = NoPreview;
    private int _restoreRequested;
    private readonly Lock _pauseGate = new();
    private DateTimeOffset? _pausedUntilValue;

    public ComfortController(
        SettingsService settings,
        DdcMonitorService ddc,
        FullscreenDetector detector,
        VersionedJsonFile<OriginalMonitorValues> originalsFile,
        AmbientLightService ambient,
        TimeProvider time,
        ILogger log)
    {
        _settings = settings;
        _ddc = ddc;
        _detector = detector;
        _originalsFile = originalsFile;
        _ambient = ambient;
        _time = time;
        _log = log.ForContext<ComfortController>();
        _fullscreen = new SettledValue<string?>(null, FullscreenSettle, time);

        var loaded = originalsFile.Load();
        if (loaded.Problem is not null)
        {
            _log.Warning("Original monitor values: {Problem}", loaded.Problem);
        }

        _originals = loaded.Value;
        _settings.Changed += OnSettingsChanged;
        _ambient.ReadingChanged += OnSettingsChanged;
    }

    /// <summary>Raised on the background loop; UI subscribers marshal to their own thread.</summary>
    public event EventHandler<ComfortStatus>? StatusChanged;

    public ComfortStatus Status { get; private set; } = ComfortStatus.Initial;

    // Set from the UI thread, read on the loop.
    private DateTimeOffset? PausedUntil
    {
        get
        {
            lock (_pauseGate)
            {
                return _pausedUntilValue;
            }
        }

        set
        {
            lock (_pauseGate)
            {
                _pausedUntilValue = value;
            }
        }
    }

    public bool IsPaused => PausedUntil is { } until && _time.GetUtcNow() < until;

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    public void PauseUntil(DateTimeOffset until)
    {
        PausedUntil = until;
        _log.Information("Paused until {Until}", until);
        RequestApply();
    }

    public void Resume()
    {
        PausedUntil = null;
        _log.Information("Resumed");
        RequestApply();
    }

    /// <summary>Puts every monitor back as it was before this app first changed it, then pauses.</summary>
    public void RestoreOriginals()
    {
        PausedUntil = DateTimeOffset.MaxValue;
        Interlocked.Exchange(ref _restoreRequested, 1);
    }

    public void RequestApply() => Interlocked.Exchange(ref _applyRequested, 1);

    /// <summary>Shows this base brightness on every monitor until cleared with null; used while calibrating.</summary>
    public void PreviewBrightness(int? brightness)
    {
        Interlocked.Exchange(ref _previewBrightness, brightness ?? NoPreview);
        RequestApply();
    }

    public async ValueTask DisposeAsync()
    {
        _settings.Changed -= OnSettingsChanged;
        _ambient.ReadingChanged -= OnSettingsChanged;
        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _stop.Dispose();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => RequestApply();

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Tick, _time);
        var nextPlan = DateTimeOffset.MinValue;
        do
        {
            try
            {
                if (Interlocked.Exchange(ref _restoreRequested, 0) == 1)
                {
                    await RestoreAsync(cancellationToken);
                }

                var displaysChanged = DisplaysChanged();
                var window = _detector.Detect();
                var fullscreenChanged = _fullscreen.Observe(window?.DeviceName);
                var requested = Interlocked.Exchange(ref _applyRequested, 0) == 1;
                var now = _time.GetUtcNow();
                var scheduled = displaysChanged || now >= nextPlan;
                if (scheduled || fullscreenChanged || requested)
                {
                    if (fullscreenChanged && window is not null)
                    {
                        _log.Information(
                            "Full-screen on {Device}: {WindowClass} of {Process} at {Bounds}",
                            window.DeviceName,
                            window.WindowClass,
                            window.ProcessName,
                            window.Bounds);
                    }
                    else if (fullscreenChanged)
                    {
                        _log.Information("Full-screen ended");
                    }

                    var allAnswered = await ApplyAsync(now, recheckUnanswered: scheduled, cancellationToken);
                    if (scheduled)
                    {
                        nextPlan = now + _schedule.NextDelay(allAnswered);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Adjusting monitors failed; retrying next cycle");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private bool DisplaysChanged()
    {
        var topology = DdcMonitorService.Topology();
        if (topology == _topology)
        {
            return false;
        }

        if (_topology is not null)
        {
            _log.Information("Displays changed: {Topology}", topology);
        }

        _topology = topology;

        // A reconnected monitor may have reset itself, so what was last written no longer describes it.
        _lastWritten.Clear();
        _schedule.DisplaysChanged();
        return true;
    }

    /// <summary>Returns whether every monitor answered, so the schedule knows whether to retry soon.</summary>
    private async Task<bool> ApplyAsync(DateTimeOffset now, bool recheckUnanswered, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;
        var monitors = _ddc.Enumerate();
        try
        {
            // Paused or without a location the monitors are left alone, so nothing is retried either.
            if (PausedUntil is { } until && now < until)
            {
                Publish(new ComfortStatus(ControllerState.Paused, null, KnownSupport(monitors), until));
                return true;
            }

            PausedUntil = null;
            if (settings.Location is not { } location)
            {
                Publish(new ComfortStatus(ControllerState.NeedsLocation, null, KnownSupport(monitors), null));
                return true;
            }

            var detected = monitors.Select(m => new DetectedMonitor(m.Id, m.Name, CheckSupport(m, recheckUnanswered))).ToList();
            var adjustable = monitors.Where(m => _support[m.Id] != MonitorSupport.None).ToList();
            var allAnswered = adjustable.Count == monitors.Count;

            if (adjustable.Count == 0)
            {
                Publish(new ComfortStatus(ControllerState.NoMonitors, null, detected, null));
                return allAnswered;
            }

            CaptureOriginals(adjustable, now);

            var fullscreenId = monitors.FirstOrDefault(m => m.DeviceName == _fullscreen.Current)?.Id;
            var plan = ComfortPlanner.Plan(
                now,
                TimeZoneInfo.Local,
                settings,
                location,
                [.. monitors.Select(m => m.Id)],
                fullscreenId,
                _ambient.Latest,
                Volatile.Read(ref _previewBrightness) is var preview and >= 0 ? preview : null);
            foreach (var target in plan.Targets)
            {
                var monitor = monitors.First(m => m.Id == target.MonitorId);
                var support = _support[monitor.Id];
                if (support == MonitorSupport.None)
                {
                    continue;
                }

                if (_originals.Find(monitor.Id) is null)
                {
                    _log.Warning("Leaving {MonitorId} alone until its original settings can be read", monitor.Id);
                    allAnswered = false;
                    continue;
                }

                await ApplyTargetAsync(monitor, support, target, settings.ForMonitor(monitor.Id), cancellationToken);
            }

            Publish(new ComfortStatus(ControllerState.Active, plan, detected, null));
            return allAnswered;
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }
    }

    private List<DetectedMonitor> KnownSupport(IReadOnlyList<DdcMonitor> monitors)
        => [.. monitors.Select(m => new DetectedMonitor(m.Id, m.Name, _support.GetValueOrDefault(m.Id, MonitorSupport.Unknown)))];

    // A monitor that didn't answer is asked again only on schedule: each unanswered request waits out a DDC/CI timeout.
    private MonitorSupport CheckSupport(DdcMonitor monitor, bool recheckUnanswered)
    {
        if (!recheckUnanswered && _support.TryGetValue(monitor.Id, out var known) && known == MonitorSupport.None)
        {
            return MonitorSupport.None;
        }

        // Logs only on a change, so a monitor without DDC/CI isn't reported every cycle.
        var support = _ddc.SupportOf(monitor);
        if (_support.TryGetValue(monitor.Id, out var previous) && previous == support)
        {
            return support;
        }

        _support[monitor.Id] = support;
        if (support == MonitorSupport.None)
        {
            _log.Warning("{MonitorId} ({Name}) doesn't answer DDC/CI; leaving it alone", monitor.Id, monitor.Name);
        }
        else
        {
            _log.Information("{MonitorId} ({Name}) supports {Support}", monitor.Id, monitor.Name, support);
        }

        return support;
    }

    private async Task ApplyTargetAsync(
        DdcMonitor monitor,
        MonitorSupport support,
        MonitorTarget target,
        MonitorSettings settings,
        CancellationToken cancellationToken)
    {
        // Switch preset before reading: the user preset brings its own stored gains.
        var colour = support == MonitorSupport.BrightnessAndColour && await EnsureUserColourPresetAsync(monitor, cancellationToken);

        // A monitor that won't answer reads falls back to what was last written, so it isn't rewritten every cycle.
        var current = ReadLevels(monitor) ?? _lastWritten.GetValueOrDefault(monitor.Id);
        var neutral = new RgbGains(settings.NeutralGain, settings.NeutralGain, settings.NeutralGain);
        var gains = colour ? ColourTemperature.Gains(target.Kelvin, neutral) : current?.Gains ?? neutral;

        var written = current ?? new MonitorLevels(target.Brightness, gains);
        foreach (var write in WritePolicy.Writes(current, new MonitorLevels(target.Brightness, gains)))
        {
            if (write.Code != VcpCode.Brightness && !colour)
            {
                continue;
            }

            var error = await monitor.WriteAsync(write.Code, write.Value, cancellationToken);
            if (error is not null)
            {
                _log.Warning("{MonitorId} rejected {Code} = {Value} (Win32 error {Error})", monitor.Id, write.Code, write.Value, error);
                continue;
            }

            _log.Debug("{MonitorId} {Code} -> {Value}", monitor.Id, write.Code, write.Value);
            written = write.Code switch
            {
                VcpCode.Brightness => written with { Brightness = write.Value },
                VcpCode.RedGain => written with { Gains = written.Gains with { Red = write.Value } },
                VcpCode.GreenGain => written with { Gains = written.Gains with { Green = write.Value } },
                VcpCode.BlueGain => written with { Gains = written.Gains with { Blue = write.Value } },
                VcpCode.ColourPreset => written,
                _ => throw new InvalidOperationException($"Unhandled VCP code {write.Code}"),
            };
        }

        _lastWritten[monitor.Id] = written;
    }

    // Gains only take effect in the user colour preset; switch once, if needed.
    private async Task<bool> EnsureUserColourPresetAsync(DdcMonitor monitor, CancellationToken cancellationToken)
    {
        if (monitor.Read(VcpCode.ColourPreset) == WritePolicy.UserColourPreset)
        {
            return true;
        }

        var error = await monitor.WriteAsync(VcpCode.ColourPreset, WritePolicy.UserColourPreset, cancellationToken);
        if (error is not null)
        {
            _log.Warning("{MonitorId} would not switch to the user colour preset (Win32 error {Error})", monitor.Id, error);
            return false;
        }

        _log.Information("{MonitorId} switched to the user colour preset", monitor.Id);
        return true;
    }

    private static MonitorLevels? ReadLevels(DdcMonitor monitor)
    {
        var brightness = monitor.Read(VcpCode.Brightness);
        var red = monitor.Read(VcpCode.RedGain);
        var green = monitor.Read(VcpCode.GreenGain);
        var blue = monitor.Read(VcpCode.BlueGain);
        return brightness is { } b && red is { } r && green is { } g && blue is { } bl
            ? new MonitorLevels(b, new RgbGains(r, g, bl))
            : null;
    }

    private void CaptureOriginals(IReadOnlyList<DdcMonitor> monitors, DateTimeOffset now)
    {
        var updated = _originals;
        foreach (var monitor in monitors.Where(m => _originals.Find(m.Id) is null))
        {
            var snapshot = new MonitorSnapshot
            {
                MonitorId = monitor.Id,
                Name = monitor.Name,
                CapturedAt = now,
                Brightness = monitor.Read(VcpCode.Brightness),
                ColourPreset = monitor.Read(VcpCode.ColourPreset),
                RedGain = monitor.Read(VcpCode.RedGain),
                GreenGain = monitor.Read(VcpCode.GreenGain),
                BlueGain = monitor.Read(VcpCode.BlueGain),
            };
            updated = updated.WithFirstCapture(snapshot);
            if (snapshot.IsComplete)
            {
                _log.Information("Captured original settings of {MonitorId} ({Name})", monitor.Id, monitor.Name);
            }
        }

        if (ReferenceEquals(updated, _originals))
        {
            return;
        }

        // If this throws, no monitor is changed this cycle: without a saved copy nothing could be restored.
        _originalsFile.Save(updated);
        _originals = updated;
    }

    /// <summary>Writes each monitor's saved original settings back; used by the loop and by <c>--restore</c>.</summary>
    public async Task RestoreAsync(CancellationToken cancellationToken)
    {
        var monitors = _ddc.Enumerate();
        try
        {
            foreach (var monitor in monitors)
            {
                if (_originals.Find(monitor.Id) is not { } snapshot)
                {
                    continue;
                }

                // Gains first: they belong to the user preset, which the preset write may leave.
                foreach (var (code, value) in new (VcpCode, int?)[]
                {
                    (VcpCode.RedGain, snapshot.RedGain),
                    (VcpCode.GreenGain, snapshot.GreenGain),
                    (VcpCode.BlueGain, snapshot.BlueGain),
                    (VcpCode.ColourPreset, snapshot.ColourPreset),
                    (VcpCode.Brightness, snapshot.Brightness),
                })
                {
                    if (value is { } v && await monitor.WriteAsync(code, v, cancellationToken) is { } error)
                    {
                        _log.Warning("Restoring {MonitorId} {Code} failed (Win32 error {Error})", monitor.Id, code, error);
                    }
                }

                _log.Information("Restored original settings of {MonitorId}", monitor.Id);
            }
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }

        RequestApply();
    }

    private void Publish(ComfortStatus status)
    {
        var previous = Status;
        Status = status;
        if (previous.State != status.State)
        {
            _log.Information("State {State}", status.State);
        }

        StatusChanged?.Invoke(this, status);
    }
}
