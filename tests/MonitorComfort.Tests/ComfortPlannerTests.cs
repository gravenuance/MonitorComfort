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
        RoomLight = new RoomLightSettings { Enabled = true, Colour = LightColour.Neutral, Level = LightLevel.Normal },
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
        Assert.False(plan.RoomLit);
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
        // Neutral 4000K tubes at normal level: at least 40% and no warmer than 3500K.
        var plan = Plan(Utc(2026, 12, 21, 22, 30), Winter, NeutralRoomLight);

        Assert.True(plan.RoomLit);
        Assert.Equal(40, plan.Brightness);
        Assert.Equal(3500, plan.Kelvin);
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

    [Theory]
    [InlineData(21, 30, true)]  // 22:30 local, after 14:48 sunset
    [InlineData(6, 0, true)]    // 07:00 local, before 08:44 sunrise, lit since yesterday's sunset
    [InlineData(9, 0, false)]   // 10:00 local, after sunrise
    [InlineData(12, 0, false)]  // 13:00 local, before sunset
    public void Lights_until_sunrise_follow_the_sun(int utcHour, int utcMinute, bool lit)
    {
        var lights = new RoomLightSettings { OffAt = LightsOff.AtSunrise };

        Assert.Equal(lit, ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, utcHour, utcMinute), Winter, lights, Stockholm));
    }

    [Theory]
    [InlineData(21, 30, true)]   // 22:30 local
    [InlineData(22, 30, false)]  // 23:30 local, after the 23:00 off time
    [InlineData(6, 0, false)]    // 07:00 local
    public void Lights_with_an_off_time_switch_off_then(int utcHour, int utcMinute, bool lit)
    {
        var lights = new RoomLightSettings { OffAt = LightsOff.AtTime, OffTime = new TimeOnly(23, 0) };

        Assert.Equal(lit, ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, utcHour, utcMinute), Winter, lights, Stockholm));
    }

    [Fact]
    public void Lights_on_offset_delays_switching_on_after_sunset()
    {
        // Sunset 13:48 UTC; lights on an hour later.
        var lights = new RoomLightSettings { OnMinutesAfterSunset = 60 };

        Assert.False(ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, 14, 30), Winter, lights, Stockholm));
        Assert.True(ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, 15, 0), Winter, lights, Stockholm));
    }

    [Fact]
    public void Polar_night_counts_as_lit_all_day()
    {
        var tromso = new GeoLocation(69.6492, 18.9553);

        Assert.True(ComfortPlanner.IsRoomLit(Utc(2026, 12, 21, 11, 0), Winter, new RoomLightSettings(), tromso));
    }

    private static ComfortPlan Plan(DateTimeOffset now, TimeZoneInfo zone, ComfortSettings settings)
        => ComfortPlanner.Plan(now, zone, settings, Stockholm, BothMonitors, null);

    private static MonitorTarget Target(ComfortPlan plan, string id) => plan.Targets.Single(t => t.MonitorId == id);

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.Zero);
}
