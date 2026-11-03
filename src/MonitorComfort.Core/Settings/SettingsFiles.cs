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

        // 2 -> 3: sunset/sunrise switching becomes fixed times; "off at sunrise" becomes 07:00.
        if (fromVersion < 3 && root["roomLight"] is JsonObject roomLight)
        {
            // Sunrise was the default, so a file without the rule meant it too.
            if (roomLight["offAt"] is null || IsSunriseRule(roomLight["offAt"]))
            {
                roomLight["offTime"] = "07:00:00";
            }

            roomLight.Remove("offAt");
            roomLight.Remove("onMinutesAfterSunset");
            roomLight["onTime"] ??= "16:00:00";
        }

        return root;
    }

    // Enums are written as names, but a hand-edited file may hold the number (AtSunrise is 0).
    private static bool IsSunriseRule(JsonNode? offAt) => offAt is JsonValue value
        && (value.TryGetValue<string>(out var name)
            ? string.Equals(name, "AtSunrise", StringComparison.OrdinalIgnoreCase)
            : value.TryGetValue<int>(out var number) && number == 0);

    public static VersionedJsonFile<UiState> UiStateFile(string directory)
        => new(
            Path.Combine(directory, "ui-state.json"),
            SettingsJsonContext.Default.UiState,
            UiState.CurrentSchemaVersion,
            static () => new UiState());

    public static VersionedJsonFile<OriginalMonitorValues> Originals(string directory)
        => new(
            Path.Combine(directory, "original-monitor-settings.json"),
            SettingsJsonContext.Default.OriginalMonitorValues,
            OriginalMonitorValues.CurrentSchemaVersion,
            static () => new OriginalMonitorValues());
}
