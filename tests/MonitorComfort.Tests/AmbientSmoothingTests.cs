using MonitorComfort.Core.Ambient;

namespace MonitorComfort.Tests;

public sealed class AmbientSmoothingTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 14, 21, 0, 0, TimeSpan.FromHours(2));

    private static AmbientReading At(int minutes, double stops, int? kelvin = 3300)
        => new(Start.AddMinutes(minutes), stops, kelvin);

    [Fact]
    public void Without_a_held_reading_the_new_one_is_used()
    {
        Assert.Equal(At(0, -1.5), AmbientSmoothing.Next(null, At(0, -1.5)));
    }

    [Fact]
    public void Noise_inside_the_deadband_keeps_the_held_light_and_refreshes_its_time()
    {
        var next = AmbientSmoothing.Next(At(0, -1.50, 3300), At(10, -1.70, 3100));

        Assert.Equal(At(10, -1.50, 3300), next);
    }

    [Fact]
    public void A_moderate_change_moves_half_the_way_with_the_new_colour()
    {
        var next = AmbientSmoothing.Next(At(0, -1.5, 3300), At(10, -2.1, 3100));

        Assert.Equal(-1.8, next.LightStops, 6);
        Assert.Equal(At(10, 0, 3100) with { LightStops = next.LightStops }, next);
    }

    [Fact]
    public void A_large_change_is_followed_at_once()
    {
        Assert.Equal(At(10, -6.5), AmbientSmoothing.Next(At(0, -1.5), At(10, -6.5)));
    }

    [Fact]
    public void A_held_reading_too_old_to_use_is_replaced()
    {
        Assert.Equal(At(31, -1.6), AmbientSmoothing.Next(At(0, -1.5), At(31, -1.6)));
    }

    [Fact]
    public void Alternating_readings_from_an_unchanged_room_hold_the_light_steady()
    {
        // The swing the camera's readings showed at one light level, once the gain no longer matters.
        double[] readings = [-1.50, -1.62, -1.45, -1.66, -1.52, -1.40, -1.58];
        var held = (AmbientReading?)null;
        for (var i = 0; i < readings.Length; i++)
        {
            held = AmbientSmoothing.Next(held, At(i * 10, readings[i]));
        }

        Assert.Equal(-1.50, held!.LightStops, 6);
    }

    [Fact]
    public void A_gradual_dimming_is_followed_within_the_deadband()
    {
        var held = (AmbientReading?)null;
        for (var i = 0; i <= 8; i++)
        {
            held = AmbientSmoothing.Next(held, At(i * 10, -1.0 - (0.15 * i)));
        }

        // The room ends at -2.2 stops.
        Assert.InRange(held!.LightStops, -2.2 - AmbientSmoothing.DeadbandStops, -2.2 + AmbientSmoothing.DeadbandStops);
    }
}
