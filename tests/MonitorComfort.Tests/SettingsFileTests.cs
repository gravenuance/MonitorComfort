using System.Text.Json.Nodes;
using MonitorComfort.Core.Settings;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.Tests;

public sealed class SettingsFileTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

    public SettingsFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string SettingsPath => Path.Combine(_directory, "settings.json");

    [Fact]
    public void Missing_file_loads_defaults_without_a_problem()
    {
        var result = SettingsFiles.Settings(_directory).Load();

        Assert.False(result.Existed);
        Assert.Null(result.Problem);
        Assert.Equal(60, result.Value.Day.Brightness);
        Assert.Equal(new GeoLocation(59.33, 18.07), result.Value.Location);
    }

    [Fact]
    public void Saved_settings_load_back_unchanged()
    {
        var file = SettingsFiles.Settings(_directory);
        var settings = new ComfortSettings
        {
            Location = new GeoLocation(59.3293, 18.0686),
            NightStart = new TimeOnly(23, 15),
            Night = new DisplayProfile(10, 3000),
            RoomLight = new RoomLightSettings
            {
                OnTime = new TimeOnly(17, 30),
                OffTime = new TimeOnly(1, 0),
                Curve = true,
                Colour = LightColour.Warm,
            },
        }.WithMonitor("SAM08DA", new MonitorSettings { BrightnessOffset = -20 });

        file.Save(settings);
        var loaded = file.Load().Value;

        Assert.Equal(settings.Location, loaded.Location);
        Assert.Equal(new TimeOnly(23, 15), loaded.NightStart);
        Assert.Equal(new DisplayProfile(10, 3000), loaded.Night);
        Assert.Equal(settings.RoomLight, loaded.RoomLight);
        Assert.Equal(-20, loaded.ForMonitor("sam08da").BrightnessOffset);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
        Assert.False(file.Load().Migrated);
    }

    [Fact]
    public void The_light_meter_camera_loads_back()
    {
        var file = SettingsFiles.Settings(_directory);
        file.Save(new ComfortSettings { CameraLight = new CameraLightSettings { CameraId = @"\?\usb#vid_046d&pid_082d" } });

        Assert.Equal(@"\?\usb#vid_046d&pid_082d", file.Load().Value.CameraLight.CameraId);
    }

    [Fact]
    public void A_calibration_saved_before_cameras_were_told_apart_loads_without_a_camera()
    {
        File.WriteAllText(
            SettingsPath,
            """{ "schemaVersion": 3, "cameraLight": { "enabled": true, "response": { "gamma": 1.25, "gainCurve": [] }, "calibration": { "bright": { "stops": -1.2, "brightness": 70 } } } }""");

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.Null(result.Problem);
        Assert.Null(result.Value.CameraLight.CameraId);
        Assert.Equal(70, result.Value.CameraLight.Calibration!.Bright!.Brightness);
        Assert.True(result.Value.CameraLight.IsReady);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_and_keeps_a_copy()
    {
        File.WriteAllText(SettingsPath, "{ not json");

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.NotNull(result.Problem);
        Assert.Equal(60, result.Value.Day.Brightness);
        Assert.Equal("{ not json", File.ReadAllText(SettingsPath + ".unreadable.bak"));
    }

    [Fact]
    public void File_from_a_newer_version_is_not_bound_and_is_kept()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 99, "day": { "brightness": 5, "kelvin": 6500 } }""");

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.Contains("newer version", result.Problem, StringComparison.Ordinal);
        Assert.Equal(60, result.Value.Day.Brightness);
        Assert.True(File.Exists(SettingsPath + ".unreadable.bak"));
    }

    [Fact]
    public void Unversioned_file_is_backed_up_then_upgraded_keeping_its_values()
    {
        const string original = """{ "day": { "brightness": 70, "kelvin": 6000 } }""";
        File.WriteAllText(SettingsPath, original);

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.Equal(new DisplayProfile(70, 6000), result.Value.Day);
        Assert.Equal(ComfortSettings.CurrentSchemaVersion, result.Value.SchemaVersion);
        Assert.Equal(original, File.ReadAllText(SettingsPath + ".v0.bak"));
    }

    [Fact]
    public void Lights_that_went_off_at_sunrise_load_as_an_off_time_of_07_00()
    {
        const string original = """
            { "schemaVersion": 2, "roomLight": { "enabled": true, "onMinutesAfterSunset": 30, "offAt": "AtSunrise", "offTime": "23:00:00", "colour": "Warm", "level": "Bright" } }
            """;
        File.WriteAllText(SettingsPath, original);

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.True(result.Migrated);
        Assert.Equal(
            new RoomLightSettings
            {
                OnTime = new TimeOnly(16, 0),
                OffTime = new TimeOnly(7, 0),
                Colour = LightColour.Warm,
                Level = LightLevel.Bright,
            },
            result.Value.RoomLight);
        Assert.Equal(3, result.Value.SchemaVersion);
        Assert.Equal(original, File.ReadAllText(SettingsPath + ".v2.bak"));
    }

    [Fact]
    public void Lights_that_went_off_at_a_set_time_keep_that_time()
    {
        File.WriteAllText(
            SettingsPath,
            """{ "schemaVersion": 2, "roomLight": { "onMinutesAfterSunset": -20, "offAt": "AtTime", "offTime": "22:15:00" } }""");

        var roomLight = SettingsFiles.Settings(_directory).Load().Value.RoomLight;

        Assert.Equal(new TimeOnly(22, 15), roomLight.OffTime);
        Assert.Equal(new TimeOnly(16, 0), roomLight.OnTime);
    }

    [Fact]
    public void Migration_drops_the_sunset_fields_and_adds_the_on_time()
    {
        var root = JsonNode.Parse(
            """{ "schemaVersion": 2, "roomLight": { "onMinutesAfterSunset": 45, "offAt": "AtSunrise", "offTime": "23:00:00" } }""")!.AsObject();

        var roomLight = SettingsFiles.MigrateSettings(root, 2)["roomLight"]!.AsObject();

        Assert.False(roomLight.ContainsKey("onMinutesAfterSunset"));
        Assert.False(roomLight.ContainsKey("offAt"));
        Assert.Equal("16:00:00", roomLight["onTime"]!.GetValue<string>());
        Assert.Equal("07:00:00", roomLight["offTime"]!.GetValue<string>());
    }

    [Fact]
    public void Migration_reads_a_missing_off_rule_as_the_old_sunrise_default()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 2, "roomLight": { "level": "Dim" } }""")!.AsObject();

        var roomLight = SettingsFiles.MigrateSettings(root, 2)["roomLight"]!.AsObject();

        Assert.Equal("07:00:00", roomLight["offTime"]!.GetValue<string>());
    }

    [Fact]
    public void Migration_leaves_a_file_without_room_light_alone()
    {
        var root = JsonNode.Parse("""{ "schemaVersion": 2, "day": { "brightness": 70, "kelvin": 6000 } }""")!.AsObject();

        var migrated = SettingsFiles.MigrateSettings(root, 2);

        Assert.False(migrated.ContainsKey("roomLight"));
    }

    [Fact]
    public void Fields_missing_from_the_file_keep_their_defaults()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 3, "day": { "brightness": 70, "kelvin": 6000 }, "roomLight": { "level": "Dim" } }""");

        var loaded = SettingsFiles.Settings(_directory).Load().Value;

        Assert.Equal(new TimeOnly(22, 30), loaded.NightStart);
        Assert.Equal(new TimeOnly(7, 0), loaded.NightEnd);
        Assert.Equal(new DisplayProfile(35, 4500), loaded.Evening);
        Assert.True(loaded.StartWithWindows);
        Assert.True(loaded.Fullscreen.DimOtherMonitors);
        Assert.Equal(new RoomLightSettings { Level = LightLevel.Dim }, loaded.RoomLight);
    }

    [Fact]
    public void Out_of_range_location_is_reported_not_thrown()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 1, "location": { "latitude": 200, "longitude": 0 } }""");

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.Contains("invalid values", result.Problem, StringComparison.Ordinal);
        Assert.Equal(new GeoLocation(59.33, 18.07), result.Value.Location);
    }

    [Fact]
    public void Normalize_clamps_out_of_range_values_and_lists_each_correction()
    {
        var settings = new ComfortSettings
        {
            Day = new DisplayProfile(150, 9000),
            Fullscreen = new FullscreenSettings { OtherMonitorBrightnessPercent = -5 },
        };

        var (normalized, corrections) = settings.Normalize();

        Assert.Equal(new DisplayProfile(100, 6500), normalized.Day);
        Assert.Equal(0, normalized.Fullscreen.OtherMonitorBrightnessPercent);
        Assert.Equal(3, corrections.Count);
    }

    [Fact]
    public void Normalize_leaves_valid_settings_untouched()
    {
        var (_, corrections) = new ComfortSettings().Normalize();

        Assert.Empty(corrections);
    }

    [Fact]
    public void Original_values_keep_the_first_capture_of_each_monitor()
    {
        var first = new MonitorSnapshot { MonitorId = "ACR0B70", Name = "KA242Y", Brightness = 60 };
        var later = first with { Brightness = 15 };

        var values = new OriginalMonitorValues().WithFirstCapture(first).WithFirstCapture(later);

        Assert.Equal(60, Assert.Single(values.Monitors).Brightness);
    }
}
