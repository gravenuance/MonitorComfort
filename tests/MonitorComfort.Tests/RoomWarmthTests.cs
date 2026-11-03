using MonitorComfort.Core.Planning;

namespace MonitorComfort.Tests;

public sealed class RoomWarmthTests
{
    [Theory]
    [InlineData(6500, 3000, 6500)] // a warm lamp at noon leaves the day colour alone
    [InlineData(5500, 6500, 6000)] // but the screens still stay within 500K of a cooler room
    public void In_daylight_only_the_guard_applies(int schedule, int room, int expected)
    {
        Assert.Equal(expected, RoomWarmth.Fit(schedule, room, 1.0));
    }

    [Fact]
    public void In_the_dark_a_close_room_colour_is_averaged_in_mireds()
    {
        // (1e6/3400 + 1e6/2700) / 2 = 332.244 mireds = 3009.8K.
        Assert.Equal(3010, RoomWarmth.Fit(3400, 2700, 0.0));

        // (1e6/3400 + 1e6/4000) / 2 = 272.059 mireds = 3675.7K, above the 3500K guard.
        Assert.Equal(3676, RoomWarmth.Fit(3400, 4000, 0.0));
    }

    [Fact]
    public void At_dusk_the_room_counts_for_less()
    {
        // Half daylight gives the room a quarter: 0.75 * 294.118 + 0.25 * 370.370 = 313.181 mireds = 3193.0K.
        Assert.Equal(3193, RoomWarmth.Fit(3400, 2700, 0.5));
    }

    [Fact]
    public void The_room_share_fades_out_as_the_gap_grows()
    {
        // A 1250K gap halves the share to 0.25: 0.75 * 250 + 0.25 * 363.636 = 278.409 mireds = 3591.8K.
        Assert.Equal(3592, RoomWarmth.Fit(4000, 2750, 0.0));

        // From 1500K on, a warmer room is ignored.
        Assert.Equal(4500, RoomWarmth.Fit(4500, 2700, 0.0));
    }

    [Fact]
    public void A_warm_room_far_from_the_schedule_changes_it_smoothly()
    {
        Assert.InRange(RoomWarmth.Fit(4000, 2510, 0.0) - RoomWarmth.Fit(4000, 2490, 0.0), -30, 30);
    }

    [Fact]
    public void A_much_cooler_room_keeps_the_screens_within_500K_of_it()
    {
        // The 1400K gap leaves a share of 0.1 (3099K), below the guard's 3900K.
        Assert.Equal(3900, RoomWarmth.Fit(3000, 4400, 0.0));
        Assert.Equal(4500, RoomWarmth.Fit(3000, 5000, 0.0));
    }

    [Theory]
    [InlineData(0, 3000, 0.5)]
    [InlineData(3000, 0, 0.5)]
    [InlineData(3000, 3000, -0.1)]
    [InlineData(3000, 3000, 1.1)]
    public void Out_of_range_inputs_are_rejected(int schedule, int room, double daylight)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RoomWarmth.Fit(schedule, room, daylight));
    }
}
