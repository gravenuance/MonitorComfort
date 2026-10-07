using MonitorComfort.App.Ambient;
using MonitorComfort.App.Monitors;
using MonitorComfort.App.Services;
using MonitorComfort.Core.Settings;
using Serilog;

namespace MonitorComfort.App;

/// <summary>Composition root: builds the long-lived services once and tears them down in reverse.</summary>
internal sealed class AppHost : IAsyncDisposable
{
    public AppHost(AppPaths paths, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(paths);
        Log = log;
        Time = TimeProvider.System;
        Settings = new SettingsService(SettingsFiles.Settings(paths.SettingsDirectory), Time, log);
        Meter = new CameraLightMeter(log);

        // The controller is created right after; the lambda only runs once both exist.
        Ambient = new AmbientLightService(Settings, Meter, Time, log, () => Controller!.IsPaused);
        Controller = new ComfortController(
            Settings,
            new DdcMonitorService(log),
            new FullscreenDetector(),
            SettingsFiles.Originals(paths.SettingsDirectory),
            Ambient,
            Time,
            log);
        Autostart = new Autostart(log);

        var current = Settings.Current;
        log.Information(
            "Settings: location set {HasLocation}, room light {RoomLight}, camera light meter {CameraLight} (calibrated {Calibrated}), full-screen dim {DimOthers} at {DimPercent}%, {MonitorCount} monitor overrides",
            current.Location is not null,
            current.RoomLight.Enabled,
            current.CameraLight.Enabled,
            current.CameraLight.Calibration is not null,
            current.Fullscreen.DimOtherMonitors,
            current.Fullscreen.OtherMonitorBrightnessPercent,
            current.Monitors.Count);
    }

    public ILogger Log { get; }

    public TimeProvider Time { get; }

    public SettingsService Settings { get; }

    public ComfortController Controller { get; }

    public CameraLightMeter Meter { get; }

    public AmbientLightService Ambient { get; }

    public Autostart Autostart { get; }

    public void Start()
    {
        Autostart.Apply(Settings.Current.StartWithWindows);
        Controller.Start();
        Ambient.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await Ambient.DisposeAsync();
        await Controller.DisposeAsync();
        Meter.Dispose();
        Settings.Dispose();
    }
}
