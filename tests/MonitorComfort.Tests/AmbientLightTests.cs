using MonitorComfort.Core.Ambient;
using MonitorComfort.Core.Planning;
using MonitorComfort.Core.Settings;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.Tests;

public sealed class AmbientLightTests
{
    private const double Gamma = 1.25;

    // Synthetic camera with gamma 1.25: brightness = 0.001 * 2^(1.25 * stops of exposure above 2^-9 s).
    private static readonly (int, double)[] ExposureSweep =
    [
        (-9, 0.001), (-8, 0.001 * Math.Pow(2, 1.25)), (-7, 0.001 * Math.Pow(2, 2.5)), (-6, 0.001 * Math.Pow(2, 3.75)),
        (-5, 0.001 * Math.Pow(2, 5.0)), (-4, 0.001 * Math.Pow(2, 6.25)), (-3, 0.001 * Math.Pow(2, 7.5)),
    ];

    // Gain 0, 128 and 255 add 0, 1 and 2 stops.
    private static readonly (int, double)[] GainSweep =
    [
        (0, 0.02), (128, 0.02 * Math.Pow(2, 1.25)), (255, 0.02 * Math.Pow(2, 2.5)),
    ];

    internal static readonly CameraResponse Response = new(Gamma, [new GainPoint(0, 0), new GainPoint(128, 1), new GainPoint(255, 2)]);

    [Fact]
    public void Fit_recovers_gamma_and_gain_stops_from_the_sweeps()
    {
        var response = CameraResponse.Fit(ExposureSweep, GainSweep)!;

        Assert.Equal(1.25, response.Gamma, 6);
        Assert.Equal(0.0, response.GainStops(0), 6);
        Assert.Equal(1.0, response.GainStops(128), 6);
        Assert.Equal(2.0, response.GainStops(255), 6);
    }

    [Fact]
    public void Fit_ignores_noise_floor_and_clipped_points()
    {
        // 2^-9 is 0.001 (under the 0.002 floor) and 2^-3 at 0.181 is fine; add a clipped 0.9 at 2^-2.
        (int, double)[] sweep = [.. ExposureSweep, (-2, 0.9)];

        Assert.Equal(1.25, CameraResponse.Fit(sweep, GainSweep)!.Gamma, 6);
    }

    [Fact]
    public void Fit_gives_up_without_enough_usable_exposure_points_or_with_a_nonsense_slope()
    {
        Assert.Null(CameraResponse.Fit([(-5, 0.03), (-4, 0.07)], GainSweep));
        Assert.Null(CameraResponse.Fit([(-6, 0.05), (-5, 0.05), (-4, 0.05)], GainSweep));
        Assert.Null(CameraResponse.Fit(ExposureSweep, [(0, 0.02)]));
    }

    [Fact]
    public void Fit_never_lets_more_gain_mean_less_light()
    {
        var response = CameraResponse.Fit(ExposureSweep, [(0, 0.02), (64, 0.05), (128, 0.04), (255, 0.1)])!;

        Assert.True(response.GainStops(128) >= response.GainStops(64));
    }

    [Fact]
    public void Gain_stops_interpolate_between_points_and_hold_at_the_ends()
    {
        Assert.Equal(0.5, Response.GainStops(64), 6);
        Assert.Equal(0.0, Response.GainStops(-10), 6);
        Assert.Equal(2.0, Response.GainStops(300), 6);
    }

    [Fact]
    public void Light_stops_combine_picture_brightness_exposure_and_gain()
    {
        // log2(0.25) / 1.25 - (-4) - 0 = -1.6 + 4 = 2.4
        Assert.Equal(2.4, Response.LightStops(0.25, -4, 0)!.Value, 6);
    }

    [Fact]
    public void Same_light_read_at_different_camera_settings_gives_the_same_stops()
    {
        // One stop less exposure and one stop more gain leave the picture unchanged.
        var a = Response.LightStops(0.25, -4, 0)!.Value;
        var b = Response.LightStops(0.25, -5, 128)!.Value;

        Assert.Equal(a, b, 6);
    }

    [Fact]
    public void Twice_the_light_reads_one_stop_higher()
    {
        var dim = Response.LightStops(0.1, -4, 0)!.Value;
        var bright = Response.LightStops(0.1 * Math.Pow(2, Gamma), -4, 0)!.Value;

        Assert.Equal(1.0, bright - dim, 6);
    }

    [Fact]
    public void Black_picture_is_no_measurement()
    {
        Assert.Null(Response.LightStops(0, -4, 0));
    }

    [Theory]
    [InlineData(2.4, 45)]
    [InlineData(3.4, 55)]
    [InlineData(0.4, 25)]
    [InlineData(-10.0, 5)]
    [InlineData(20.0, 100)]
    public void Brightness_moves_ten_points_per_stop_from_the_calibration(double stops, int expected)
    {
        Assert.Equal(expected, AmbientMapping.Brightness(new AmbientCalibration(new CalibrationPoint(2.4, 45), null), stops, 10));
    }

    [Fact]
    public void Readings_are_fresh_for_thirty_minutes()
    {
        var at = new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
        var reading = new AmbientReading(at, 2.0, 4000);

        Assert.True(AmbientMapping.IsFresh(reading, at.AddMinutes(30)));
        Assert.False(AmbientMapping.IsFresh(reading, at.AddMinutes(31)));
        Assert.False(AmbientMapping.IsFresh(reading, at.AddMinutes(-1)));
    }

    [Fact]
    public void Colour_is_trusted_only_within_three_stops_of_the_calibration_light()
    {
        var calibration = new AmbientCalibration(new CalibrationPoint(2.4, 45), null);

        Assert.Equal(4000, AmbientMapping.ReliableKelvin(calibration, new AmbientReading(DateTimeOffset.UnixEpoch, -0.6, 4000)));
        Assert.Null(AmbientMapping.ReliableKelvin(calibration, new AmbientReading(DateTimeOffset.UnixEpoch, -0.7, 4000)));
    }
}

public sealed class AmbientPlanningTests
{
    private static readonly GeoLocation Stockholm = new(59.3293, 18.0686);
    private static readonly TimeZoneInfo Winter = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
    private static readonly DateTimeOffset NightUtc = new(2026, 12, 21, 22, 30, 0, TimeSpan.Zero);
    private static readonly string[] Monitors = ["ACR0B70", "SAM08DA"];

    // Night profile alone would be 15% / 3400K; neutral room lights would make it 40% / 3500K.
    private static readonly ComfortSettings Calibrated = new()
    {
        Location = Stockholm,
        CameraLight = new CameraLightSettings { Enabled = true, Response = AmbientLightTests.Response, Calibration = new AmbientCalibration(new CalibrationPoint(2.4, 45), null) },
    };

    private static ComfortPlan Plan(ComfortSettings settings, AmbientReading? reading, string? fullscreen = null)
        => ComfortPlanner.Plan(NightUtc, Winter, settings, Stockholm, Monitors, fullscreen, reading);

    [Fact]
    public void Fresh_reading_sets_brightness_and_limits_warmth_to_the_measured_light()
    {
        // 1 stop above calibration -> 55%; 4100K light -> no warmer than 3600K.
        var plan = Plan(Calibrated, new AmbientReading(NightUtc.AddMinutes(-5), 3.4, 4100));

        Assert.Equal(AmbientSource.Camera, plan.Source);
        Assert.Equal(55, plan.Brightness);
        Assert.Equal(3600, plan.Kelvin);
    }

    [Fact]
    public void Dark_room_reading_dims_and_leaves_the_night_warmth_alone()
    {
        // 4 stops below calibration: 45 - 40 = 5%; colour untrusted, so the night profile's 3400K stays.
        var plan = Plan(Calibrated, new AmbientReading(NightUtc.AddMinutes(-5), -1.6, 6000));

        Assert.Equal(5, plan.Brightness);
        Assert.Equal(3400, plan.Kelvin);
    }

    [Fact]
    public void Stale_reading_falls_back_to_the_room_model()
    {
        var plan = Plan(Calibrated, new AmbientReading(NightUtc.AddMinutes(-31), 3.4, 4100));

        Assert.Equal(AmbientSource.RoomModel, plan.Source);
        Assert.Equal(40, plan.Brightness);
    }

    [Fact]
    public void Uncalibrated_or_disabled_camera_is_ignored()
    {
        var reading = new AmbientReading(NightUtc.AddMinutes(-5), 3.4, 4100);
        var uncalibrated = Calibrated with { CameraLight = Calibrated.CameraLight with { Calibration = null } };
        var disabled = Calibrated with { CameraLight = Calibrated.CameraLight with { Enabled = false } };

        Assert.Equal(AmbientSource.RoomModel, Plan(uncalibrated, reading).Source);
        Assert.Equal(AmbientSource.RoomModel, Plan(disabled, reading).Source);
    }

    [Fact]
    public void Calibration_preview_overrides_brightness_but_keeps_offsets_and_warmth()
    {
        var settings = Calibrated.WithMonitor("SAM08DA", new MonitorSettings { BrightnessOffset = -10 });

        var plan = ComfortPlanner.Plan(
            NightUtc, Winter, settings, Stockholm, Monitors, null, new AmbientReading(NightUtc.AddMinutes(-5), 3.4, 4100), previewBrightness: 70);

        Assert.Equal(70, plan.Targets.Single(t => t.MonitorId == "ACR0B70").Brightness);
        Assert.Equal(60, plan.Targets.Single(t => t.MonitorId == "SAM08DA").Brightness);
        Assert.Equal(3600, plan.Kelvin);
    }

    [Fact]
    public void Full_screen_dimming_still_applies_on_top_of_a_reading()
    {
        var plan = Plan(Calibrated, new AmbientReading(NightUtc.AddMinutes(-5), 3.4, 4100), fullscreen: "ACR0B70");

        Assert.Equal(55, plan.Targets.Single(t => t.MonitorId == "ACR0B70").Brightness);
        Assert.Equal(11, plan.Targets.Single(t => t.MonitorId == "SAM08DA").Brightness);
    }

    [Fact]
    public void Camera_settings_survive_a_save_and_load()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));
        try
        {
            var file = SettingsFiles.Settings(directory);
            file.Save(Calibrated);
            var loaded = file.Load().Value.CameraLight;

            Assert.True(loaded.IsReady);
            Assert.Equal(new AmbientCalibration(new CalibrationPoint(2.4, 45), null), loaded.Calibration);
            Assert.Equal(1.25, loaded.Response!.Gamma);
            Assert.Equal(AmbientLightTests.Response.GainCurve, loaded.Response.GainCurve);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
