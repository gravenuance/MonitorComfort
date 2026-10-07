using MonitorComfort.Core.Ambient;
using MonitorComfort.Core.Settings;

namespace MonitorComfort.Tests;

public sealed class TwoLevelCalibrationTests
{
    // Bright room: 2.0 stops at 60%; dim room: -1.0 stops at 20%. Rate: 40 points over 3 stops.
    private static readonly AmbientCalibration BothLevels = new(new CalibrationPoint(2.0, 60), new CalibrationPoint(-1.0, 20));

    [Theory]
    [InlineData(2.0, 60)]
    [InlineData(-1.0, 20)]
    [InlineData(0.5, 40)]    // halfway in stops -> halfway in brightness
    [InlineData(2.75, 70)]   // continues above the bright point: 60 + 0.75 * 13.33
    [InlineData(-2.5, 5)]    // 20 - 1.5 * 13.33 = 0, held at the 5% floor
    public void Both_levels_set_brightness_along_the_line_through_them(double stops, int expected)
    {
        Assert.Equal(expected, AmbientMapping.Brightness(BothLevels, stops, brightnessPerStop: 10));
    }

    [Fact]
    public void One_level_uses_the_fixed_rate_around_it()
    {
        var dimOnly = new AmbientCalibration(null, new CalibrationPoint(-1.0, 20));

        Assert.Equal(30, AmbientMapping.Brightness(dimOnly, 0.0, brightnessPerStop: 10));
    }

    [Fact]
    public void Setting_a_level_keeps_the_other_one()
    {
        var (calibration, error) = BothLevels.With(CalibrationLevel.Dim, new CalibrationPoint(-2.0, 15));

        Assert.Null(error);
        Assert.Equal(new CalibrationPoint(2.0, 60), calibration!.Bright);
        Assert.Equal(new CalibrationPoint(-2.0, 15), calibration.Dim);
    }

    [Fact]
    public void Levels_with_barely_different_light_are_rejected()
    {
        var (calibration, error) = BothLevels.With(CalibrationLevel.Dim, new CalibrationPoint(1.8, 20));

        Assert.Null(calibration);
        Assert.Equal("The light barely changed. Dim the room more and try again.", error);
    }

    [Fact]
    public void A_dim_room_wanting_a_brighter_screen_is_rejected()
    {
        var (calibration, error) = BothLevels.With(CalibrationLevel.Dim, new CalibrationPoint(-1.0, 70));

        Assert.Null(calibration);
        Assert.Equal("The dim room needs a lower brightness than the bright one.", error);
    }

    [Fact]
    public void A_dim_level_measured_brighter_than_the_bright_one_is_rejected()
    {
        var (calibration, error) = BothLevels.With(CalibrationLevel.Dim, new CalibrationPoint(3.0, 20));

        Assert.Null(calibration);
        Assert.NotNull(error);
    }

    [Fact]
    public void First_level_can_be_either_one()
    {
        Assert.Null(AmbientCalibration.Empty.With(CalibrationLevel.Dim, new CalibrationPoint(-1.0, 20)).Error);
        Assert.Null(AmbientCalibration.Empty.With(CalibrationLevel.Bright, new CalibrationPoint(2.0, 60)).Error);
    }

    [Fact]
    public void Colour_trust_is_measured_from_the_brightest_level()
    {
        // Brightest calibrated light is 2.0 stops, so colour is trusted down to -1.0.
        Assert.Equal(4000, AmbientMapping.ReliableKelvin(BothLevels, new AmbientReading(DateTimeOffset.UnixEpoch, -1.0, 4000)));
        Assert.Null(AmbientMapping.ReliableKelvin(BothLevels, new AmbientReading(DateTimeOffset.UnixEpoch, -1.1, 4000)));
    }

    [Fact]
    public void Version_1_calibration_becomes_the_bright_level_and_the_old_file_is_kept()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            const string version1 = """
                { "schemaVersion": 1, "cameraLight": { "enabled": true, "calibration": { "referenceStops": -0.55, "referenceBrightness": 60 } } }
                """;
            File.WriteAllText(path, version1);

            var result = SettingsFiles.Settings(directory).Load();

            Assert.True(result.Migrated);
            Assert.Equal(2, result.Value.SchemaVersion);
            Assert.Equal(new CalibrationPoint(-0.55, 60), result.Value.CameraLight.Calibration!.Bright);
            Assert.Null(result.Value.CameraLight.Calibration.Dim);
            Assert.True(result.Value.CameraLight.Enabled);
            Assert.Equal(version1, File.ReadAllText(path + ".v1.bak"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Saved_files_hold_settings_only_not_computed_values()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        try
        {
            SettingsFiles.Settings(directory).Save(new ComfortSettings
            {
                CameraLight = new CameraLightSettings { Enabled = true, Calibration = BothLevels },
            });
            SettingsFiles.Originals(directory).Save(new OriginalMonitorValues().WithFirstCapture(
                new MonitorSnapshot { MonitorId = "ACR0B70", Name = "KA242Y", Brightness = 60 }));

            var json = File.ReadAllText(Path.Combine(directory, "settings.json"))
                + File.ReadAllText(Path.Combine(directory, "original-monitor-settings.json"));

            Assert.DoesNotContain("isReady", json, StringComparison.Ordinal);
            Assert.DoesNotContain("hasAnyPoint", json, StringComparison.Ordinal);
            Assert.DoesNotContain("brightestStops", json, StringComparison.Ordinal);
            Assert.DoesNotContain("isComplete", json, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Version_1_file_without_a_calibration_migrates_untouched()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse("""{ "schemaVersion": 1, "day": { "brightness": 70, "kelvin": 6000 } }""")!.AsObject();

        var migrated = SettingsFiles.MigrateSettings(root, 1);

        Assert.Null(migrated["cameraLight"]);
        Assert.Equal(70, migrated["day"]!["brightness"]!.GetValue<int>());
    }
}
