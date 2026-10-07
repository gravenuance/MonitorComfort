using MonitorComfort.Core.Solar;

namespace MonitorComfort.Tests;

public sealed class SolarCalculatorTests
{
    private static readonly GeoLocation Stockholm = new(59.3293, 18.0686);
    private static readonly GeoLocation Athens = new(37.9838, 23.7275);
    private static readonly GeoLocation Tromso = new(69.6492, 18.9553);

    // Expected times are published almanac values (timeanddate.com), converted to UTC.
    [Theory]
    [InlineData(59.3293, 18.0686, "2026-06-21", "01:31", "20:08")]
    [InlineData(59.3293, 18.0686, "2026-12-21", "07:44", "13:48")]
    [InlineData(37.9838, 23.7275, "2026-06-21", "03:02", "17:51")]
    public void Events_match_published_sunrise_and_sunset_within_three_minutes(
        double latitude, double longitude, string date, string sunriseUtc, string sunsetUtc)
    {
        var day = DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture);
        var events = SolarCalculator.Events(day, new GeoLocation(latitude, longitude));

        Assert.Equal(DayKind.Normal, events.Kind);
        AssertWithinMinutes(day, sunriseUtc, events.Sunrise!.Value, 3);
        AssertWithinMinutes(day, sunsetUtc, events.Sunset!.Value, 3);
    }

    [Fact]
    public void Events_report_polar_day_at_midsummer_above_the_arctic_circle()
    {
        var events = SolarCalculator.Events(new DateOnly(2026, 6, 21), Tromso);

        Assert.Equal(DayKind.PolarDay, events.Kind);
        Assert.Null(events.Sunrise);
        Assert.Null(events.Sunset);
    }

    [Fact]
    public void Events_report_polar_night_at_midwinter_above_the_arctic_circle()
    {
        Assert.Equal(DayKind.PolarNight, SolarCalculator.Events(new DateOnly(2026, 12, 21), Tromso).Kind);
    }

    [Fact]
    public void Elevation_at_midsummer_solar_noon_in_stockholm_is_colatitude_plus_tilt()
    {
        // 90 - 59.33 + 23.44 = 54.11 degrees; solar noon there is about 10:49 UTC.
        var elevation = SolarCalculator.Elevation(new DateTimeOffset(2026, 6, 21, 10, 49, 0, TimeSpan.Zero), Stockholm);

        Assert.InRange(elevation, 53.8, 54.4);
    }

    [Fact]
    public void Elevation_near_midwinter_midnight_in_stockholm_is_far_below_the_horizon()
    {
        var elevation = SolarCalculator.Elevation(new DateTimeOffset(2026, 12, 21, 22, 49, 0, TimeSpan.Zero), Stockholm);

        Assert.InRange(elevation, -54.4, -53.8);
    }

    [Fact]
    public void Elevation_ignores_the_offset_of_the_instant_it_is_given()
    {
        var utc = new DateTimeOffset(2026, 3, 20, 12, 0, 0, TimeSpan.Zero);
        var athensLocal = utc.ToOffset(TimeSpan.FromHours(2));

        Assert.Equal(SolarCalculator.Elevation(utc, Athens), SolarCalculator.Elevation(athensLocal, Athens), 9);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(0, -181)]
    public void Location_rejects_out_of_range_coordinates(double latitude, double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeoLocation(latitude, longitude));
    }

    private static void AssertWithinMinutes(DateOnly day, string expectedUtc, DateTimeOffset actual, int tolerance)
    {
        var expected = new DateTimeOffset(
            day.ToDateTime(TimeOnly.Parse(expectedUtc, System.Globalization.CultureInfo.InvariantCulture)),
            TimeSpan.Zero);
        var difference = Math.Abs((actual - expected).TotalMinutes);
        Assert.True(difference <= tolerance, $"Expected {expected:HH:mm} UTC, got {actual.UtcDateTime:HH:mm} UTC");
    }
}
