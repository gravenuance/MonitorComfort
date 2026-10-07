using System.Text.Json.Nodes;

namespace MonitorComfort.Core.Settings;

/// <summary>The app's persisted files under one settings directory.</summary>
public static class SettingsFiles
{
    public static VersionedJsonFile<ComfortSettings> Settings(string directory)
        => new(
            Path.Combine(directory, "settings.json"),
            SettingsJsonContext.Default.ComfortSettings,
            ComfortSettings.CurrentSchemaVersion,
            static () => new ComfortSettings(),
            MigrateSettings);

    /// <summary>Raw-JSON upgrades, applied in order from the file's version.</summary>
    public static JsonObject MigrateSettings(JsonObject root, int fromVersion)
    {
        ArgumentNullException.ThrowIfNull(root);

        // 1 -> 2: a single calibration point {referenceStops, referenceBrightness} becomes the bright point.
        if (fromVersion < 2
            && root["cameraLight"]?["calibration"] is JsonObject calibration
            && calibration["referenceStops"] is { } stops
            && calibration["referenceBrightness"] is { } brightness)
        {
            root["cameraLight"]!["calibration"] = new JsonObject
            {
                ["bright"] = new JsonObject { ["stops"] = stops.DeepClone(), ["brightness"] = brightness.DeepClone() },
            };
        }

        return root;
    }

    public static VersionedJsonFile<OriginalMonitorValues> Originals(string directory)
        => new(
            Path.Combine(directory, "original-monitor-settings.json"),
            SettingsJsonContext.Default.OriginalMonitorValues,
            OriginalMonitorValues.CurrentSchemaVersion,
            static () => new OriginalMonitorValues());
}
