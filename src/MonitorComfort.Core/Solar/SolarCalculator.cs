namespace MonitorComfort.Core.Solar;

/// <summary>Whether the sun rises and sets on a given day at a location.</summary>
public enum DayKind
{
    Normal,
    PolarDay,
    PolarNight,
}

/// <summary>Sunrise and sunset in UTC; both are null on a polar day or night.</summary>
public sealed record SunEvents(DayKind Kind, DateTimeOffset? Sunrise, DateTimeOffset? Sunset);

/// <summary>
/// Solar position from the NOAA spreadsheet equations (accurate to about a minute for
/// sunrise and sunset between latitudes ±72°).
/// </summary>
public static class SolarCalculator
{
    // Apparent sunrise: the sun's upper limb on the horizon, including mean refraction.
    private const double SunriseZenithDegrees = 90.833;

    private static readonly DateTimeOffset J2000 = new(2000, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Geometric elevation of the sun's centre above the horizon, in degrees.</summary>
    public static double Elevation(DateTimeOffset instant, GeoLocation location)
    {
        var utc = instant.ToUniversalTime();
        var (declination, equationOfTime) = SunParameters(utc);
        var minutesOfDay = utc.TimeOfDay.TotalMinutes;
        var trueSolarMinutes = minutesOfDay + equationOfTime + (4.0 * location.Longitude);
        var hourAngle = (trueSolarMinutes / 4.0) - 180.0;

        var lat = ToRadians(location.Latitude);
        var cosZenith = (Math.Sin(lat) * Math.Sin(declination))
            + (Math.Cos(lat) * Math.Cos(declination) * Math.Cos(ToRadians(hourAngle)));
        return 90.0 - ToDegrees(Math.Acos(Math.Clamp(cosZenith, -1.0, 1.0)));
    }

    /// <summary>
    /// Sunrise and sunset for the solar day nearest <paramref name="date"/> at the location's
    /// longitude, which is the local calendar day wherever the time zone roughly follows it.
    /// </summary>
    public static SunEvents Events(DateOnly date, GeoLocation location)
    {
        var midnightUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var approximateNoon = midnightUtc.AddMinutes(720.0 - (4.0 * location.Longitude));

        var sunrise = EventMinutes(midnightUtc, approximateNoon, location, rising: true);
        var sunset = EventMinutes(midnightUtc, approximateNoon, location, rising: false);
        if (sunrise is null || sunset is null)
        {
            var (declination, _) = SunParameters(approximateNoon);
            var kind = HourAngleCosine(location.Latitude, declination) > 1.0 ? DayKind.PolarNight : DayKind.PolarDay;
            return new SunEvents(kind, null, null);
        }

        return new SunEvents(DayKind.Normal, midnightUtc.AddMinutes(sunrise.Value), midnightUtc.AddMinutes(sunset.Value));
    }

    // Two refinement passes evaluate the sun's position at the event itself rather than at noon.
    private static double? EventMinutes(DateTimeOffset midnightUtc, DateTimeOffset estimate, GeoLocation location, bool rising)
    {
        var instant = estimate;
        double minutes = 0;
        for (var pass = 0; pass < 3; pass++)
        {
            var (declination, equationOfTime) = SunParameters(instant);
            var cosHourAngle = HourAngleCosine(location.Latitude, declination);
            if (cosHourAngle is > 1.0 or < -1.0)
            {
                return null;
            }

            var hourAngle = ToDegrees(Math.Acos(cosHourAngle));
            var solarNoon = 720.0 - (4.0 * location.Longitude) - equationOfTime;
            minutes = rising ? solarNoon - (4.0 * hourAngle) : solarNoon + (4.0 * hourAngle);
            instant = midnightUtc.AddMinutes(minutes);
        }

        return minutes;
    }

    private static double HourAngleCosine(double latitudeDegrees, double declination)
    {
        var lat = ToRadians(latitudeDegrees);
        return (Math.Cos(ToRadians(SunriseZenithDegrees)) / (Math.Cos(lat) * Math.Cos(declination)))
            - (Math.Tan(lat) * Math.Tan(declination));
    }

    // Returns the declination (radians) and the equation of time (minutes).
    private static (double Declination, double EquationOfTime) SunParameters(DateTimeOffset utc)
    {
        var julianCentury = (utc - J2000).TotalDays / 36525.0;
        var t = julianCentury;

        var meanLongitude = Normalize(280.46646 + (t * (36000.76983 + (t * 0.0003032))));
        var meanAnomaly = 357.52911 + (t * (35999.05029 - (0.0001537 * t)));
        var eccentricity = 0.016708634 - (t * (0.000042037 + (0.0000001267 * t)));

        var m = ToRadians(meanAnomaly);
        var centre = (Math.Sin(m) * (1.914602 - (t * (0.004817 + (0.000014 * t)))))
            + (Math.Sin(2 * m) * (0.019993 - (0.000101 * t)))
            + (Math.Sin(3 * m) * 0.000289);
        var omega = ToRadians(125.04 - (1934.136 * t));
        var apparentLongitude = ToRadians(meanLongitude + centre - 0.00569 - (0.00478 * Math.Sin(omega)));

        var meanObliquity = 23.0 + ((26.0 + ((21.448 - (t * (46.815 + (t * (0.00059 - (t * 0.001813)))))) / 60.0)) / 60.0);
        var obliquity = ToRadians(meanObliquity + (0.00256 * Math.Cos(omega)));

        var declination = Math.Asin(Math.Sin(obliquity) * Math.Sin(apparentLongitude));

        var y = Math.Pow(Math.Tan(obliquity / 2.0), 2);
        var l0 = ToRadians(meanLongitude);
        var equationOfTime = 4.0 * ToDegrees(
            (y * Math.Sin(2 * l0))
            - (2 * eccentricity * Math.Sin(m))
            + (4 * eccentricity * y * Math.Sin(m) * Math.Cos(2 * l0))
            - (0.5 * y * y * Math.Sin(4 * l0))
            - (1.25 * eccentricity * eccentricity * Math.Sin(2 * m)));

        return (declination, equationOfTime);
    }

    private static double Normalize(double degrees)
    {
        var result = degrees % 360.0;
        return result < 0 ? result + 360.0 : result;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double ToDegrees(double radians) => radians * 180.0 / Math.PI;
}
