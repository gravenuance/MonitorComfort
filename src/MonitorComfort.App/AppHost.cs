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
        Controller = new ComfortController(
            Settings,
            new DdcMonitorService(log),
            new FullscreenDetector(),
            SettingsFiles.Originals(paths.SettingsDirectory),
            Time,
            log);
        Autostart = new Autostart(log);

        var current = Settings.Current;
        log.Information(
            "Settings: location set {HasLocation}, room light {RoomLight}, full-screen dim {DimOthers} at {DimPercent}%, {MonitorCount} monitor overrides",
            current.Location is not null,
            current.RoomLight.Enabled,
            current.Fullscreen.DimOtherMonitors,
            current.Fullscreen.OtherMonitorBrightnessPercent,
            current.Monitors.Count);
    }

    public ILogger Log { get; }

    public TimeProvider Time { get; }

    public SettingsService Settings { get; }

    public ComfortController Controller { get; }

    public Autostart Autostart { get; }

    public void Start()
    {
        Autostart.Apply(Settings.Current.StartWithWindows);
        Controller.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await Controller.DisposeAsync();
        Settings.Dispose();
    }
}
