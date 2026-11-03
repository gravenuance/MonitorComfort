using MonitorComfort.Core.Planning;
using MonitorComfort.Core.Settings;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.Tests;

public sealed class ComfortPlannerTests
{
    private const string Acer = "ACR0B70";
    private const string Samsung = "SAM08DA";

    private static readonly GeoLocation Stockholm = new(59.3293, 18.0686);
    private static readonly TimeZoneInfo Winter = TimeZoneInfo.CreateCustomTimeZone("UTC+1", TimeSpan.FromHours(1), "UTC+1", "UTC+1");
    private static readonly TimeZoneInfo Summer = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");
    private static readonly string[] BothMonitors = [Acer, Samsung];

    // Defaults: day 60%/6500K, evening 35%/4500K, night 15%/3400K, night 22:30–07:00.
    private static readonly ComfortSettings NoRoomLight = new()
    {
        Location = Stockholm,
        RoomLight = new RoomLightSettings { Enabled = false },
    };

    private static readonly ComfortSettings NeutralRoomLight = NoRoomLight with
    {
        RoomLight = new RoomLightSettings
        {
            Enabled = true,
            OnTime = new TimeOnly(16, 0),
            OffTime = new TimeOnly(2, 0),
            Colour = LightColour.Neutral,
            Level = LightLevel.Normal,
        },
    };

    [Fact]
    public void Summer_midday_uses_the_day_profile()
    {
        var plan = Plan(Utc(2026, 6, 21, 11, 0), Summer, NoRoomLight);

        Assert.Equal(Phase.Day, plan.Phase);
        Assert.Equal(60, plan.Brightness);
        Assert.Equal(6500, plan.Kelvin);
    }

    [Fact]
    public void Winter_late_afternoon_after_sunset_uses_the_evening_profile()
    {
        // 17:00 local; sunset was 14:48.
        var plan = Plan(Utc(2026, 12, 21, 16, 0), Winter, NoRoomLight);

        Assert.Equal(Phase.Evening, plan.Phase);
        Assert.Equal(35, plan.Brightness);
        Assert.Equal(4500, plan.Kelvin);
    }

    [Fact]
    public void Past_the_night_ramp_in_a_dark_room_uses_the_night_profile()
    {
        // 23:30 local, a full 60 minutes after 22:30.
        var plan = Plan(Utc(2026, 12, 21, 22, 30), Winter, NoRoomLight);

        Assert.Equal(Phase.Night, plan.Phase);
        Assert.Equal(AmbientSource.Schedule, plan.Source);
        Assert.Equal(15, plan.Brightness);
        Assert.Equal(3400, plan.Kelvin);
    }

    [Fact]
    public void Midway_through_the_night_ramp_blends_evening_into_night()
    {
        // 23:00 local is 30 of 45 minutes in: 35 + (15 - 35) * 2/3 = 21.7; 4500 + (3400 - 4500) * 2/3 = 3766.7.
        var plan = Plan(Utc(2026, 12, 21, 22, 0), Winter, NoRoomLight);

        Assert.Equal(22, plan.Brightness);
        Assert.Equal(3767, plan.Kelvin);
    }

    [Fact]
    public void Lit_room_at_night_keeps_brightness_up_and_colour_near_the_lights()
    {
        // Neutral 4000K tubes at normal level: at least 40%, and the night's 3400K averaged with 4000K in mireds.
        var plan = Plan(Utc(2026, 12, 21, 22, 30), Winter, NeutralRoomLight);

        Assert.Equal(AmbientSource.RoomModel, plan.Source);
        Assert.Equal(40, plan.Brightness);
        Assert.Equal(3676, plan.Kelvin);
    }

    [Fact]
    public void Warm_lights_at_night_make_the_screens_warmer()
    {
        // The night's 3400K averaged with 2700K bulbs: 332.244 mireds = 3009.8K.
        var settings = NeutralRoomLight with { RoomLight = NeutralRoomLight.RoomLight with { Colour = LightColour.Warm } };

        Assert.Equal(3010, Plan(Utc(2026, 12, 21, 22, 30), Winter, settings).Kelvin);
    }

    [Fact]
    public void Lit_room_does_not_lower_values_already_above_its_floor()
    {
        // Evening 35%/4500K under warm 2700K dim lights: floor 25%, warmest 2200K.
        var settings = NeutralRoomLight with
        {
            RoomLight = NeutralRoomLight.RoomLight with { Colour = LightColour.Warm, Level = LightLevel.Dim },
        };

        var plan = Plan(Utc(2026, 12, 21, 16, 0), Winter, settings);

        Assert.Equal(35, plan.Brightness);
        Assert.Equal(4500, plan.Kelvin);
    }

    [Fact]
    public void Full_screen_dims_only_the_other_monitor()
    {
        var plan = ComfortPlanner.Plan(Utc(2026, 12, 21, 16, 0), Winter, NoRoomLight, Stockholm, BothMonitors, Acer);

        Assert.Equal(35, Target(plan, Acer).Brightness);
        Assert.Equal(7, Target(plan, Samsung).Brightness);
        Assert.Equal(4500, Target(plan, Samsung).Kelvin);
    }

    [Fact]
    public void Full_screen_changes_nothing_when_dimming_is_off()
    {
        var settings = NoRoomLight with { Fullscreen = new FullscreenSettings { DimOtherMonitors = false } };

        var plan = ComfortPlanner.Plan(Utc(2026, 12, 21, 16, 0), Winter, settings, Stockholm, BothMonitors, Acer);

        Assert.Equal(35, Target(plan, Samsung).Brightness);
    }

    [Fact]
    public void Monitor_offset_shifts_brightness_and_is_clamped_to_the_valid_range()
    {
        var settings = NoRoomLight
            .WithMonitor(Samsung, new MonitorSettings { BrightnessOffset = -10 })
            .WithMonitor(Acer, new MonitorSettings { BrightnessOffset = 50 });

        var plan = ComfortPlanner.Plan(Utc(2026, 6, 21, 11, 0), Summer, settings, Stockholm, BothMonitors, null);

        Assert.Equal(100, Target(plan, Acer).Brightness);
        Assert.Equal(50, Target(plan, Samsung).Brightness);
    }

    [Fact]
    public void Disabled_monitor_gets_no_target()
    {
        var settings = NoRoomLight.WithMonitor(Samsung, new MonitorSettings { Enabled = false });

        var plan = ComfortPlanner.Plan(Utc(2026, 6, 21, 11, 0), Summer, settings, Stockholm, BothMonitors, null);

        Assert.Equal([Acer], plan.Targets.Select(t => t.MonitorId));
    }

    [Theory]
    [InlineData("12:00", 0.0)]
    [InlineData("22:30", 0.0)]
    [InlineData("23:00", 0.6667)]
    [InlineData("02:00", 1.0)]
    [InlineData("07:00", 1.0)]
    [InlineData("07:15", 0.6667)]
    [InlineData("08:00", 0.0)]
    public void Night_weight_ramps_in_after_bedtime_and_out_after_wake_time(string time, double expected)
    {
        var weight = ComfortPlanner.NightWeight(
            TimeOnly.Parse(time, System.Globalization.CultureInfo.InvariantCulture), new TimeOnly(22, 30), new TimeOnly(7, 0));

        Assert.Equal(expected, weight, 3);
    }

    // The zone is UTC+1, so 15:00 UTC is 16:00 local.
    [Theory]
    [InlineData(14, 59, false)]  // 15:59 local, a minute before the default 16:00 on time
    [InlineData(15, 0, true)]    // 16:00 local: lit at the on time
    [InlineData(21, 59, true)]   // 22:59 local
    [InlineData(22, 0, false)]   // 23:00 local: unlit at the off time
    [InlineData(5, 0, false)]    // 06:00 local
    public void Default_lights_are_on_from_16_to_23_local_time(int utcHour, int utcMinute, bool lit)
    {
        Assert.Equal(lit, ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, utcHour, utcMinute), Winter, new RoomLightSettings()));
    }

    [Theory]
    [InlineData(20, 59, false)]  // 21:59 local
    [InlineData(21, 0, true)]    // 22:00 local: lit at the on time
    [InlineData(22, 0, true)]    // 23:00 local
    [InlineData(23, 30, true)]   // 00:30 local, the next day
    [InlineData(0, 59, true)]    // 01:59 local
    [InlineData(1, 0, false)]    // 02:00 local: unlit at the off time
    [InlineData(11, 0, false)]   // 12:00 local
    public void Lights_window_may_cross_midnight(int utcHour, int utcMinute, bool lit)
    {
        var lights = new RoomLightSettings { OnTime = new TimeOnly(22, 0), OffTime = new TimeOnly(2, 0) };

        Assert.Equal(lit, ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, utcHour, utcMinute), Winter, lights));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(11, 0)]  // 12:00 local
    [InlineData(19, 0)]  // 20:00 local, the shared time itself
    public void Equal_on_and_off_times_never_light_the_room(int utcHour, int utcMinute)
    {
        var lights = new RoomLightSettings { OnTime = new TimeOnly(20, 0), OffTime = new TimeOnly(20, 0) };

        Assert.False(ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, utcHour, utcMinute), Winter, lights));
    }

    [Fact]
    public void Lights_window_uses_the_local_clock_not_the_sun()
    {
        // 16:00 local is lit in the summer as in the winter, though the sun is high.
        Assert.True(ComfortPlanner.IsRoomLit(Utc(2026, 6, 21, 14, 0), Summer, new RoomLightSettings()));
    }

    [Theory]
    [InlineData(0.0, 0.3333)]
    [InlineData(0.25, 0.6667)]
    [InlineData(0.5, 1.0)]
    [InlineData(0.75, 0.6667)]
    [InlineData(1.0, 0.3333)]
    public void Curve_factor_is_a_soft_bell_between_a_third_and_one(double fraction, double expected)
    {
        Assert.Equal(expected, RoomLightCurve.Factor(fraction), 4);
    }

    // 18:00–22:00 local with a 5% evening profile, so the floor decides the brightness.
    [Theory]
    [InlineData(17, 0, LightLevel.Normal, 13)]   // 18:00, t=0: 40/3 = 13.3
    [InlineData(17, 30, LightLevel.Normal, 17)]  // 18:30, t=0.125: 40 * (1/3 + 2/3 * 0.1464) = 17.2
    [InlineData(18, 0, LightLevel.Normal, 27)]   // 19:00, t=0.25: 40 * 2/3 = 26.7
    [InlineData(19, 0, LightLevel.Normal, 40)]   // 20:00, t=0.5: the full floor
    [InlineData(20, 0, LightLevel.Normal, 27)]   // 21:00, t=0.75: 26.7
    [InlineData(20, 30, LightLevel.Normal, 17)]  // 21:30, t=0.875: 17.2
    [InlineData(17, 0, LightLevel.Dim, 8)]       // 25/3 = 8.3
    [InlineData(17, 0, LightLevel.Bright, 18)]   // 55/3 = 18.3
    public void Curve_scales_the_brightness_floor_across_the_lit_window(int utcHour, int utcMinute, LightLevel level, int expected)
    {
        var settings = CurvedEvening(curve: true, level);

        Assert.Equal(expected, Plan(Utc(2026, 12, 21, utcHour, utcMinute), Winter, settings).Brightness);
    }

    [Theory]
    [InlineData(17, 0)]
    [InlineData(18, 0)]
    [InlineData(19, 0)]
    public void Without_the_curve_the_floor_stays_flat(int utcHour, int utcMinute)
    {
        var settings = CurvedEvening(curve: false, LightLevel.Normal);

        Assert.Equal(40, Plan(Utc(2026, 12, 21, utcHour, utcMinute), Winter, settings).Brightness);
    }

    [Fact]
    public void Curve_does_not_change_the_colour()
    {
        var flat = Plan(Utc(2026, 12, 21, 17, 0), Winter, CurvedEvening(curve: false, LightLevel.Normal));
        var curved = Plan(Utc(2026, 12, 21, 17, 0), Winter, CurvedEvening(curve: true, LightLevel.Normal));

        Assert.Equal(flat.Kelvin, curved.Kelvin);
        Assert.Equal(AmbientSource.RoomModel, curved.Source);
    }

    [Fact]
    public void Curve_never_lowers_a_schedule_brightness_above_its_floor()
    {
        // The default 35% evening profile beats the 13% floor at the start of the window.
        var settings = NeutralRoomLight with
        {
            RoomLight = NeutralRoomLight.RoomLight with { OnTime = new TimeOnly(18, 0), OffTime = new TimeOnly(22, 0), Curve = true },
        };

        Assert.Equal(35, Plan(Utc(2026, 12, 21, 17, 0), Winter, settings).Brightness);
    }

    private static ComfortSettings CurvedEvening(bool curve, LightLevel level) => NeutralRoomLight with
    {
        Evening = new DisplayProfile(5, 4500),
        RoomLight = NeutralRoomLight.RoomLight with
        {
            OnTime = new TimeOnly(18, 0),
            OffTime = new TimeOnly(22, 0),
            Curve = curve,
            Level = level,
        },
    };

    private static ComfortPlan Plan(DateTimeOffset now, TimeZoneInfo zone, ComfortSettings settings)
        => ComfortPlanner.Plan(now, zone, settings, Stockholm, BothMonitors, null);

    private static MonitorTarget Target(ComfortPlan plan, string id) => plan.Targets.Single(t => t.MonitorId == id);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
