using MonitorComfort.App.Ambient;
using MonitorComfort.App.Monitors;
using MonitorComfort.App.Native;
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
/// windows every second, notices display changes as Windows reports them and re-plans every
/// minute, or at once when asked.
/// </summary>
internal sealed class ComfortController : IAsyncDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FullscreenSettle = TimeSpan.FromSeconds(3);

    // A monitor waking up may not raise any event, so the layout is also read now and then.
    private static readonly TimeSpan TopologyBackstop = TimeSpan.FromSeconds(15);
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
    private readonly MonitorSupportCache _cache = new();
    private readonly ReplanSchedule _schedule = new();

    private Task _loop = Task.CompletedTask;
    private DisplayEventWindow? _displayEvents;
    private string? _topology;
    private DateTimeOffset _nextTopologyCheck = DateTimeOffset.MinValue;
    private DateTimeOffset _nextPlan = DateTimeOffset.MinValue;

    // Set by the display event thread, read and cleared on the loop.
    private int _topologyDirty;
    private int _displayWoke;
    private int _consoleDisplayOn = 1;
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

    public void Start()
    {
        _displayEvents = new DisplayEventWindow(_log);
        _displayEvents.TopologyMayHaveChanged += OnTopologyMayHaveChanged;
        _displayEvents.ConsoleDisplayChanged += OnConsoleDisplayChanged;
        _displayEvents.Start();
        _loop = Task.Run(() => RunAsync(_stop.Token));
    }

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
        if (_displayEvents is not null)
        {
            _displayEvents.TopologyMayHaveChanged -= OnTopologyMayHaveChanged;
            _displayEvents.ConsoleDisplayChanged -= OnConsoleDisplayChanged;
        }

        _displayEvents?.Dispose();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => RequestApply();

    private void OnTopologyMayHaveChanged(object? sender, EventArgs e) => Interlocked.Exchange(ref _topologyDirty, 1);

    private void OnConsoleDisplayChanged(object? sender, bool on)
    {
        _log.Information("Console display turned {State}", on ? "on" : "off");
        Volatile.Write(ref _consoleDisplayOn, on ? 1 : 0);
        if (on)
        {
            // Monitors that were asleep behave like newly connected ones, so they get the same settling replans.
            Interlocked.Exchange(ref _displayWoke, 1);
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Tick, _time);
        do
        {
            try
            {
                if (Interlocked.Exchange(ref _restoreRequested, 0) == 1)
                {
                    await RestoreAsync(cancellationToken);
                }

                // A sleeping monitor can't answer DDC/CI; the wake-up is handled as a display change.
                if (Volatile.Read(ref _consoleDisplayOn) == 1)
                {
                    await CycleAsync(cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Adjusting monitors failed; retrying next cycle");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private async Task CycleAsync(CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var displaysChanged = DisplaysChanged(now);
        var window = _detector.Detect();
        var fullscreenChanged = _fullscreen.Observe(window?.DeviceName);
        var requested = Interlocked.Exchange(ref _applyRequested, 0) == 1;
        var scheduled = displaysChanged || now >= _nextPlan;
        if (!scheduled && !fullscreenChanged && !requested)
        {
            return;
        }

        if (fullscreenChanged && window is not null)
        {
            _log.Information(
                "Full-screen on {Device}: {WindowClass} of {Process} at {Bounds}",
                window.DeviceName,
                window.WindowClass,
                FullscreenDetector.ProcessName(window.ProcessId),
                window.Bounds);
        }
        else if (fullscreenChanged)
        {
            _log.Information("Full-screen ended");
        }

        await ApplyAsync(now, scheduled, settling: _schedule.IsSettling, cancellationToken);
        if (scheduled)
        {
            _nextPlan = now + _schedule.NextDelay();
        }
    }

    private bool DisplaysChanged(DateTimeOffset now)
    {
        var woke = Interlocked.Exchange(ref _displayWoke, 0) == 1;
        var dirty = Interlocked.Exchange(ref _topologyDirty, 0) == 1;
        if (!woke && !dirty && now < _nextTopologyCheck)
        {
            return false;
        }

        _nextTopologyCheck = now + TopologyBackstop;
        var topology = DdcMonitorService.Topology();
        var layoutChanged = topology != _topology;
        if (!layoutChanged && !woke)
        {
            return false;
        }

        if (_topology is not null)
        {
            _log.Information("Displays {Change}: {Topology}", layoutChanged ? "changed" : "woke", topology);
        }

        _topology = topology;

        // A reconnected monitor may have reset itself, so what was last written no longer describes it.
        _lastWritten.Clear();
        _cache.TopologyChanged();
        _ddc.ForgetNames();
        _schedule.DisplaysChanged();
        return true;
    }

    private async Task ApplyAsync(DateTimeOffset now, bool scheduled, bool settling, CancellationToken cancellationToken)
    {
        var settings = _settings.Current;

        // Paused or without a location the monitors are left alone, so they aren't even opened to read their names.
        if (PausedUntil is { } until && now < until)
        {
            Publish(new ComfortStatus(ControllerState.Paused, null, KnownSupport(_ddc.Names()), until));
            return;
        }

        PausedUntil = null;
        if (settings.Location is not { } location)
        {
            Publish(new ComfortStatus(ControllerState.NeedsLocation, null, KnownSupport(_ddc.Names()), null));
            return;
        }

        var monitors = _ddc.Enumerate();
        try
        {
            var detected = monitors.Select(m => new DetectedMonitor(m.Id, m.Name, CheckSupport(m, now, scheduled, settling))).ToList();
            var adjustable = monitors.Where(m => _cache.SupportOf(m.Id) != MonitorSupport.None).ToList();
            if (adjustable.Count == 0)
            {
                Publish(new ComfortStatus(ControllerState.NoMonitors, null, detected, null));
                return;
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
                var support = _cache.SupportOf(monitor.Id);
                if (support == MonitorSupport.None)
                {
                    continue;
                }

                if (_originals.Find(monitor.Id) is null)
                {
                    _log.Warning("Leaving {MonitorId} alone until its original settings can be read", monitor.Id);
                    continue;
                }

                await ApplyTargetAsync(monitor, support, target, settings.ForMonitor(monitor.Id), settling, cancellationToken);
            }

            Publish(new ComfortStatus(ControllerState.Active, plan, detected, null));
        }
        finally
        {
            foreach (var monitor in monitors)
            {
                monitor.Dispose();
            }
        }
    }

    private List<DetectedMonitor> KnownSupport(IReadOnlyList<MonitorName> monitors)
        => [.. monitors.Select(m => new DetectedMonitor(m.Id, m.Name, _cache.SupportOf(m.Id)))];

    // Each answer costs a DDC/CI read, and each unanswered request waits out a timeout, so the cache decides when to ask.
    private MonitorSupport CheckSupport(DdcMonitor monitor, DateTimeOffset now, bool scheduled, bool settling)
    {
        if (!_cache.NeedsCheck(monitor.Id, now, scheduled, settling))
        {
            return _cache.SupportOf(monitor.Id);
        }

        // Logs only on a change, so a monitor without DDC/CI isn't reported every cycle.
        var support = _ddc.SupportOf(monitor);
        var changed = _cache.Record(monitor.Id, support, now, settling);

        // Without capabilities the colour support is only a guess, and a monitor still waking up may answer them later.
        if (support == MonitorSupport.BrightnessOnly && !_ddc.HasCapabilities(monitor.Id))
        {
            _cache.Invalidate(monitor.Id);
        }

        if (!changed)
        {
            return support;
        }

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
        bool rewriteAll,
        CancellationToken cancellationToken)
    {
        // Switch preset before reading: the user preset brings its own stored gains.
        var colour = support == MonitorSupport.BrightnessAndColour && await EnsureUserColourPresetAsync(monitor, cancellationToken);

        var lastWritten = _lastWritten.GetValueOrDefault(monitor.Id);
        var current = ReadLevels(monitor);
        if (current is null)
        {
            // A monitor that stops answering is classified again, and may now turn out not to answer at all.
            if (support == MonitorSupport.BrightnessAndColour)
            {
                _cache.Invalidate(monitor.Id);
            }

            // A monitor that won't answer reads falls back to what was last written, so it isn't rewritten every cycle.
            current = lastWritten;
        }
        else if (colour && WritePolicy.GainsChangedExternally(lastWritten, current))
        {
            // Someone, probably the monitor's own menu, changed the gains; the colour preset is checked again next cycle.
            _cache.SetInUserPreset(monitor.Id, false);
        }

        var neutral = new RgbGains(settings.NeutralGain, settings.NeutralGain, settings.NeutralGain);
        var gains = colour ? ColourTemperature.Gains(target.Kelvin, neutral) : current?.Gains ?? neutral;

        var written = current ?? new MonitorLevels(target.Brightness, gains);

        // Some monitors report written values they dropped while starting up, so the readback can't skip writes then.
        if (rewriteAll)
        {
            _log.Debug("{MonitorId} settling after a display change; rewriting every value", monitor.Id);
        }

        foreach (var write in WritePolicy.Writes(rewriteAll ? null : current, new MonitorLevels(target.Brightness, gains)))
        {
            if (write.Code != VcpCode.Brightness && !colour)
            {
                continue;
            }

            var error = await monitor.WriteAsync(write.Code, write.Value, cancellationToken);
            if (error is not null)
            {
                _log.Warning("{MonitorId} rejected {Code} = {Value} (Win32 error {Error})", monitor.Id, write.Code, write.Value, error);
                _cache.Invalidate(monitor.Id);
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

    // Gains only take effect in the user colour preset; switch once, if needed, and remember it.
    private async Task<bool> EnsureUserColourPresetAsync(DdcMonitor monitor, CancellationToken cancellationToken)
    {
        if (_cache.InUserPreset(monitor.Id))
        {
            return true;
        }

        if (monitor.Read(VcpCode.ColourPreset) == WritePolicy.UserColourPreset)
        {
            _cache.SetInUserPreset(monitor.Id, true);
            return true;
        }

        var error = await monitor.WriteAsync(VcpCode.ColourPreset, WritePolicy.UserColourPreset, cancellationToken);
        if (error is not null)
        {
            _log.Warning("{MonitorId} would not switch to the user colour preset (Win32 error {Error})", monitor.Id, error);
            _cache.Invalidate(monitor.Id);
            return false;
        }

        _log.Information("{MonitorId} switched to the user colour preset", monitor.Id);
        _cache.SetInUserPreset(monitor.Id, true);
        return true;
    }

    private static MonitorLevels? ReadLevels(DdcMonitor monitor)
    {
        // Reading the gains of a monitor that doesn't answer would only wait out more timeouts.
        if (monitor.Read(VcpCode.Brightness) is not { } brightness)
        {
            return null;
        }

        var red = monitor.Read(VcpCode.RedGain);
        var green = monitor.Read(VcpCode.GreenGain);
        var blue = monitor.Read(VcpCode.BlueGain);
        return red is { } r && green is { } g && blue is { } b
            ? new MonitorLevels(brightness, new RgbGains(r, g, b))
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

            // The restored preset may not be the user preset, so it has to be checked again.
            _cache.ForgetUserPresets();
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
