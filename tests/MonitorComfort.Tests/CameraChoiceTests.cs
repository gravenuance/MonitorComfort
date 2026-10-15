using MonitorComfort.Core.Ambient;
using MonitorComfort.Core.Settings;

namespace MonitorComfort.Tests;

public sealed class CameraChoiceTests
{
    private static readonly CameraDevice Virtual = new(@"\\?\root#camera#0000", "OBS Virtual Camera");
    private static readonly CameraDevice C920 = new(@"\\?\usb#vid_046d&pid_082d#abc", "HD Pro Webcam C920");
    private static readonly CameraDevice Laptop = new(@"\\?\usb#vid_0bda&pid_5634#def", "Integrated Camera");
    private static readonly CameraDevice[] All = [Virtual, C920, Laptop];

    [Fact]
    public void Without_a_choice_every_camera_is_a_candidate_in_windows_order()
        => Assert.Equal(All, CameraChoice.Candidates(All, null));

    [Fact]
    public void A_chosen_camera_is_the_only_candidate_whatever_the_case_of_its_id()
        => Assert.Equal([C920], CameraChoice.Candidates(All, C920.Id.ToUpperInvariant()));

    [Fact]
    public void A_chosen_camera_that_is_not_connected_leaves_no_candidates()
        => Assert.Empty(CameraChoice.Candidates([Virtual, Laptop], C920.Id));

    [Fact]
    public void The_same_webcam_on_another_usb_port_stands_in_for_the_chosen_one()
    {
        var otherPort = C920 with { Id = @"\\?\usb#vid_046d&pid_082d#xyz" };

        Assert.Equal([otherPort], CameraChoice.Candidates([Virtual, otherPort, Laptop], C920.Id));
    }

    [Fact]
    public void Two_webcams_of_the_chosen_model_on_other_ports_are_ambiguous()
    {
        var first = C920 with { Id = @"\\?\usb#vid_046d&pid_082d#one" };
        var second = C920 with { Id = @"\\?\usb#vid_046d&pid_082d#two" };

        Assert.Empty(CameraChoice.Candidates([first, second], C920.Id));
    }

    [Fact]
    public void An_id_that_is_not_a_device_path_matches_only_exactly()
        => Assert.Empty(CameraChoice.Candidates([C920], "HD Pro Webcam"));

    [Fact]
    public void Cameras_without_the_controls_are_skipped_for_the_next()
    {
        var skipped = new List<CameraDevice>();

        var opened = CameraChoice.OpenFirstUsable(
            All,
            c => c == Virtual ? throw new NotSupportedException("no exposure") : c.Name,
            (c, _) => skipped.Add(c));

        Assert.Equal(C920.Name, opened);
        Assert.Equal([Virtual], skipped);
    }

    [Fact]
    public void A_camera_in_use_is_reported_rather_than_falling_back_to_another()
    {
        var tried = new List<CameraDevice>();

        var error = Assert.Throws<InvalidOperationException>(() => CameraChoice.OpenFirstUsable<string>(
            [C920, Laptop],
            c =>
            {
                tried.Add(c);
                throw new InvalidOperationException("in use");
            },
            (_, _) => { }));

        Assert.Equal("in use", error.Message);
        Assert.Equal([C920], tried);
    }

    [Fact]
    public void When_no_camera_has_the_controls_the_last_reason_is_thrown()
    {
        var error = Assert.Throws<NotSupportedException>(() => CameraChoice.OpenFirstUsable<string>(
            [Virtual, Laptop],
            c => throw new NotSupportedException(c.Name),
            (_, _) => { }));

        Assert.Equal(Laptop.Name, error.Message);
    }

    [Fact]
    public void No_candidates_opens_nothing()
        => Assert.Null(CameraChoice.OpenFirstUsable<string>([], c => c.Name, (_, _) => { }));

    [Fact]
    public void Frame_size_prefers_640_by_480()
        => Assert.Equal(new FrameSize(640, 480), FrameSize.Pick([new(1920, 1080), new(320, 240), new(640, 480)]));

    [Fact]
    public void Frame_size_falls_back_to_the_smallest_offered()
        => Assert.Equal(new FrameSize(424, 240), FrameSize.Pick([new(1920, 1080), new(0, 0), new(424, 240), new(1280, 720)]));

    [Fact]
    public void Frame_size_is_left_to_the_camera_when_none_is_offered()
    {
        Assert.Null(FrameSize.Pick([]));
        Assert.Null(FrameSize.Pick([new(0, 0)]));
    }

    [Fact]
    public void Switching_camera_drops_the_old_cameras_calibration()
    {
        var calibrated = Calibrated(C920.Id);

        var switched = calibrated.WithCamera(Laptop.Id);

        Assert.Equal(Laptop.Id, switched.CameraId);
        Assert.Null(switched.Response);
        Assert.Null(switched.Calibration);
        Assert.True(switched.Enabled);
    }

    [Fact]
    public void Choosing_the_same_camera_keeps_its_calibration()
    {
        var calibrated = Calibrated(C920.Id);

        Assert.Same(calibrated, calibrated.WithCamera(C920.Id.ToUpperInvariant()));
    }

    [Fact]
    public void Choosing_a_camera_for_a_calibration_without_one_keeps_the_calibration()
    {
        var chosen = Calibrated(null).WithCamera(C920.Id);

        Assert.Equal(C920.Id, chosen.CameraId);
        Assert.NotNull(chosen.Response);
        Assert.NotNull(chosen.Calibration);
    }

    [Fact]
    public void A_calibration_without_a_camera_is_assigned_to_the_camera_a_reading_came_from()
    {
        var after = Calibrated(null).AfterReadingWith(null, C920.Id);

        Assert.Equal(C920.Id, after.CameraId);
        Assert.NotNull(after.Response);
        Assert.NotNull(after.Calibration);
    }

    [Fact]
    public void A_webcam_moved_to_another_port_keeps_its_calibration_under_the_new_id()
    {
        const string otherPort = @"\\?\usb#vid_046d&pid_082d#xyz";

        var after = Calibrated(C920.Id).AfterReadingWith(C920.Id, otherPort);

        Assert.Equal(otherPort, after.CameraId);
        Assert.NotNull(after.Calibration);
    }

    [Fact]
    public void A_reading_never_overrides_a_camera_chosen_while_it_ran()
    {
        var changed = Calibrated(Laptop.Id);

        Assert.Same(changed, changed.AfterReadingWith(C920.Id, C920.Id));
    }

    [Fact]
    public void A_reading_names_no_camera_without_a_calibration()
        => Assert.Null(new CameraLightSettings().AfterReadingWith(null, C920.Id).CameraId);

    [Fact]
    public void Fit_without_a_gain_sweep_gives_a_response_that_ignores_gain()
    {
        // 0.001 * 2^(1.25 * stops of exposure above 2^-9 s).
        (int, double)[] sweep = [(-8, 0.001 * Math.Pow(2, 1.25)), (-7, 0.001 * Math.Pow(2, 2.5)), (-6, 0.001 * Math.Pow(2, 3.75))];

        var response = CameraResponse.Fit(sweep, [])!;

        Assert.Equal(1.25, response.Gamma, 6);
        Assert.Empty(response.GainCurve);
        Assert.Equal(0, response.GainStops(0));
    }

    private static CameraLightSettings Calibrated(string? cameraId) => new()
    {
        Enabled = true,
        CameraId = cameraId,
        Response = AmbientLightTests.Response,
        Calibration = AmbientCalibration.Empty.With(CalibrationLevel.Bright, new CalibrationPoint(-1.2, 70)).Calibration,
    };
}
