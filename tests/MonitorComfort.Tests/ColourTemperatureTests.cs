using MonitorComfort.Core.Colour;

namespace MonitorComfort.Tests;

public sealed class ColourTemperatureTests
{
    private static readonly RgbGains Neutral = new(50, 50, 50);

    [Fact]
    public void Neutral_temperature_keeps_neutral_gains()
    {
        Assert.Equal(new RgbGains(50, 50, 50), ColourTemperature.Gains(6500, Neutral));
    }

    [Fact]
    public void Table_point_scales_green_and_blue_by_the_blackbody_ratios()
    {
        // 3000K: green 0.7198, blue 0.4286 of red.
        Assert.Equal(new RgbGains(50, 36, 21), ColourTemperature.Gains(3000, Neutral));
    }

    [Fact]
    public void Between_table_points_the_ratios_are_interpolated()
    {
        // Midway between 3000K and 3500K: green 0.7498, blue 0.4875.
        Assert.Equal(new RgbGains(50, 37, 24), ColourTemperature.Gains(3250, Neutral));
    }

    [Fact]
    public void Temperatures_cooler_than_neutral_are_clamped_to_neutral()
    {
        Assert.Equal(new RgbGains(50, 50, 50), ColourTemperature.Gains(9300, Neutral));
    }

    [Fact]
    public void Temperatures_below_the_minimum_are_clamped_to_the_minimum()
    {
        // 2000K: green 0.5436, blue 0.0868.
        Assert.Equal(new RgbGains(50, 27, 4), ColourTemperature.Gains(1000, Neutral));
    }

    [Fact]
    public void Gains_scale_from_the_monitor_neutral_point()
    {
        Assert.Equal(new RgbGains(80, 58, 34), ColourTemperature.Gains(3000, new RgbGains(80, 80, 80)));
    }
}
