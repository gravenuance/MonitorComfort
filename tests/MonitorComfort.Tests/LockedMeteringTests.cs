using MonitorComfort.Core.Ambient;

namespace MonitorComfort.Tests;

public sealed class LockedMeteringTests
{
    private const double Gamma = 1.25;

    // The C920's ranges.
    private static readonly CameraLimits Limits = new(-11, -2, 0, 255);

    // The fitted response; the camera below matches it except at gain 0, as measured on the C920.
    private static readonly CameraResponse Response = new(
        Gamma,
        [new GainPoint(0, 0), new GainPoint(63, 1.6), new GainPoint(126, 2.35), new GainPoint(189, 2.85), new GainPoint(255, 3.15)]);

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void Reading_does_not_depend_on_the_gain_automatic_exposure_reported(int reportedGain)
    {
        var camera = new FakeCamera(sceneStops: -3.0);

        var stops = Read(camera, new CameraSetting(-3, reportedGain));

        Assert.Equal(-3.0, stops, 6);
    }

    [Fact]
    public void A_well_exposed_picture_is_measured_at_the_metering_gain_without_stepping()
    {
        // At 2^-3 s and gain 63 the picture is 2^(1.25 * (-3 - 3 + 1.6)) = 0.022.
        var camera = new FakeCamera(sceneStops: -3.0);

        var (setting, _) = LockedMetering.Measure(Limits, new CameraSetting(-3, 0), camera.LockAndRead);

        Assert.Equal(new CameraSetting(-3, 63), setting);
        Assert.Equal([new CameraSetting(-3, 63)], camera.Locked);
    }

    [Fact]
    public void A_bright_room_shortens_exposure_until_nothing_clips()
    {
        // 2^-3 s clips; 2^-6 s gives 2^(1.25 * (2 - 6 + 1.6)) = 0.125.
        var camera = new FakeCamera(sceneStops: 2.0);

        var (setting, _) = LockedMetering.Measure(Limits, new CameraSetting(-3, 0), camera.LockAndRead);

        Assert.Equal(new CameraSetting(-6, 63), setting);
        Assert.Equal(2.0, Read(new FakeCamera(sceneStops: 2.0), new CameraSetting(-3, 0)), 6);
    }

    [Fact]
    public void A_dark_room_lengthens_exposure_first_then_raises_gain()
    {
        // At the longest exposure, 2^-2 s: gain 63 gives 0.007, 126 gives 0.014, 189 gives 0.021.
        var camera = new FakeCamera(sceneStops: -5.3);

        var (setting, _) = LockedMetering.Measure(Limits, new CameraSetting(-3, 255), camera.LockAndRead);

        Assert.Equal(new CameraSetting(-2, 189), setting);
        Assert.Equal(
            [new CameraSetting(-3, 63), new CameraSetting(-2, 63), new CameraSetting(-2, 126), new CameraSetting(-2, 189)],
            camera.Locked);
        Assert.Equal(-5.3, Read(new FakeCamera(sceneStops: -5.3), new CameraSetting(-3, 255)), 6);
    }

    [Fact]
    public void Stops_at_the_limits_when_the_room_is_too_dark_or_too_bright()
    {
        var dark = LockedMetering.Measure(Limits, new CameraSetting(-3, 0), new FakeCamera(sceneStops: -12).LockAndRead);
        var bright = LockedMetering.Measure(Limits, new CameraSetting(-3, 0), new FakeCamera(sceneStops: 12).LockAndRead);

        Assert.Equal(new CameraSetting(-2, 255), dark.Setting);
        Assert.Equal(new CameraSetting(-11, 63), bright.Setting);
    }

    [Fact]
    public void A_reported_exposure_outside_the_range_starts_from_the_nearest_limit()
    {
        var camera = new FakeCamera(sceneStops: 0);

        LockedMetering.Measure(Limits, new CameraSetting(5, 0), camera.LockAndRead);

        Assert.Equal(new CameraSetting(-2, 63), camera.Locked[0]);
    }

    private static double Read(FakeCamera camera, CameraSetting reported)
    {
        var (setting, meanY) = LockedMetering.Measure(Limits, reported, camera.LockAndRead);
        return Response.LightStops(meanY, setting.ExposureLog2, setting.Gain)!.Value;
    }

    /// <summary>A camera whose picture is 2^(gamma * (scene + exposure + gain stops)), clipping at 1.</summary>
    private sealed class FakeCamera(double sceneStops)
    {
        public List<CameraSetting> Locked { get; } = [];

        public double LockAndRead(CameraSetting setting)
        {
            Locked.Add(setting);

            // At gain 0 the real picture is 0.7 stops darker than the fit says.
            var gainStops = setting.Gain == 0 ? -0.7 : Response.GainStops(setting.Gain);
            return Math.Min(1, Math.Pow(2, Gamma * (sceneStops + setting.ExposureLog2 + gainStops)));
        }
    }
}
