using MonitorComfort.Core.Settings;
using Serilog;

namespace MonitorComfort.App.Services;

/// <summary>
/// The current settings, shared by the UI and the controller. Changes apply at once; saving
/// is coalesced so dragging a slider writes the file once, not on every step.
/// </summary>
internal sealed class SettingsService : IDisposable
{
    private static readonly TimeSpan SaveDelay = TimeSpan.FromMilliseconds(500);

    private readonly VersionedJsonFile<ComfortSettings> _file;
    private readonly ILogger _log;
    private readonly ITimer _saveTimer;
    private readonly Lock _gate = new();
    private ComfortSettings _current;
    private bool _dirty;

    public SettingsService(VersionedJsonFile<ComfortSettings> file, TimeProvider time, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(time);
        _file = file;
        _log = log.ForContext<SettingsService>();

        var loaded = file.Load();
        if (loaded.Problem is not null)
        {
            _log.Warning("Settings: {Problem}", loaded.Problem);
        }

        var (normalized, corrections) = loaded.Value.Normalize();
        foreach (var correction in corrections)
        {
            _log.Warning("Settings corrected: {Correction}", correction);
        }

        _current = normalized;
        IsFirstRun = !loaded.Existed;
        _saveTimer = time.CreateTimer(_ => Flush(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public event EventHandler? Changed;

    public bool IsFirstRun { get; }

    public ComfortSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Update(Func<ComfortSettings, ComfortSettings> change)
    {
        ArgumentNullException.ThrowIfNull(change);
        lock (_gate)
        {
            var next = change(_current);
            if (next == _current)
            {
                return;
            }

            _current = next;
            _dirty = true;
            _saveTimer.Change(SaveDelay, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Flush()
    {
        ComfortSettings snapshot;
        lock (_gate)
        {
            if (!_dirty)
            {
                return;
            }

            snapshot = _current;
            _dirty = false;
        }

        try
        {
            _file.Save(snapshot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error(ex, "Could not save settings to {Path}", _file.Path);
            lock (_gate)
            {
                _dirty = true;
            }
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Flush();
    }
}
