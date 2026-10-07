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
        Assert.Null(result.Value.Location);
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
            RoomLight = new RoomLightSettings { OffAt = LightsOff.AtTime, Colour = LightColour.Warm },
        }.WithMonitor("SAM08DA", new MonitorSettings { BrightnessOffset = -20 });

        file.Save(settings);
        var loaded = file.Load().Value;

        Assert.Equal(settings.Location, loaded.Location);
        Assert.Equal(new TimeOnly(23, 15), loaded.NightStart);
        Assert.Equal(new DisplayProfile(10, 3000), loaded.Night);
        Assert.Equal(settings.RoomLight, loaded.RoomLight);
        Assert.Equal(-20, loaded.ForMonitor("sam08da").BrightnessOffset);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
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
    public void Out_of_range_location_is_reported_not_thrown()
    {
        File.WriteAllText(SettingsPath, """{ "schemaVersion": 1, "location": { "latitude": 200, "longitude": 0 } }""");

        var result = SettingsFiles.Settings(_directory).Load();

        Assert.Contains("invalid values", result.Problem, StringComparison.Ordinal);
        Assert.Null(result.Value.Location);
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
