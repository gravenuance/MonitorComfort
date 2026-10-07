namespace MonitorComfort.Core.Settings;

/// <summary>The app's persisted files under one settings directory.</summary>
public static class SettingsFiles
{
    public static VersionedJsonFile<ComfortSettings> Settings(string directory)
        => new(
            Path.Combine(directory, "settings.json"),
            SettingsJsonContext.Default.ComfortSettings,
            ComfortSettings.CurrentSchemaVersion,
            static () => new ComfortSettings());

    public static VersionedJsonFile<OriginalMonitorValues> Originals(string directory)
        => new(
            Path.Combine(directory, "original-monitor-settings.json"),
            SettingsJsonContext.Default.OriginalMonitorValues,
            OriginalMonitorValues.CurrentSchemaVersion,
            static () => new OriginalMonitorValues());
}
