using System.Text.Json.Serialization;

namespace MonitorComfort.Core.Ambient;

/// <summary>One webcam measurement: light in stops on the camera's scale, and the light's colour if known.</summary>
public sealed record AmbientReading(DateTimeOffset At, double LightStops, int? Kelvin);

/// <summary>A light level and the screen brightness the user found comfortable in it.</summary>
public sealed record CalibrationPoint(double Stops, int Brightness);

public enum CalibrationLevel
{
    Bright,
    Dim,
}

/// <summary>
/// The user's comfortable brightness in a bright room and in a dim one. With both, the dimming rate
/// comes from the user's own two choices; with one, a fixed rate per stop is used around it.
/// </summary>
public sealed record AmbientCalibration(CalibrationPoint? Bright, CalibrationPoint? Dim)
{
    /// <summary>Closer than this, the two levels can't give a trustworthy dimming rate.</summary>
    public const double MinimumSeparationStops = 0.25;

    public static AmbientCalibration Empty { get; } = new(null, null);

    [JsonIgnore]
    public bool HasAnyPoint => Bright is not null || Dim is not null;

    /// <summary>The brightest calibrated light level, or null without a point.</summary>
    [JsonIgnore]
    public double? BrightestStops => Bright?.Stops ?? Dim?.Stops;

    /// <summary>
    /// Sets one level's point, or explains why it doesn't fit the other level: the bright room must
    /// be measurably brighter than the dim one, and must not want a dimmer screen.
    /// </summary>
    public (AmbientCalibration? Calibration, string? Error) With(CalibrationLevel level, CalibrationPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        var (bright, dim) = level switch
        {
            CalibrationLevel.Bright => (point, Dim),
            CalibrationLevel.Dim => (Bright, point),
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unhandled calibration level"),
        };

        if (bright is not null && dim is not null)
        {
            if (bright.Stops - dim.Stops < MinimumSeparationStops)
            {
                return (null, "The light barely changed. Dim the room more and try again.");
            }

            if (bright.Brightness < dim.Brightness)
            {
                return (null, "The dim room needs a lower brightness than the bright one.");
            }
        }

        return (new AmbientCalibration(bright, dim), null);
    }
}

public static class AmbientMapping
{
    /// <summary>Readings older than this fall back to the room-light model.</summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(30);

    /// <summary>Below this many stops under the brightest calibration, the camera mostly sees the screens' own glow.</summary>
    public const double ColourReliableBelowReference = 3.0;

    private const int LowestBrightness = 5;
    private const int HighestBrightness = 100;

    public static bool IsFresh(AmbientReading reading, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var age = now - reading.At;
        return age >= TimeSpan.Zero && age <= MaximumAge;
    }

    /// <summary>
    /// With both points, brightness follows the straight line through them (in stops), continuing
    /// beyond them; with one point, it moves <paramref name="brightnessPerStop"/> per stop from it.
    /// </summary>
    public static int Brightness(AmbientCalibration calibration, double lightStops, double brightnessPerStop)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        var anchor = calibration.Bright ?? calibration.Dim
            ?? throw new ArgumentException("The calibration has no point.", nameof(calibration));
        var perStop = calibration is { Bright: { } bright, Dim: { } dim }
            ? (bright.Brightness - dim.Brightness) / (bright.Stops - dim.Stops)
            : brightnessPerStop;

        var brightness = anchor.Brightness + (perStop * (lightStops - anchor.Stops));
        return (int)Math.Clamp(Math.Round(brightness), LowestBrightness, HighestBrightness);
    }

    /// <summary>The light's colour, when the room is bright enough for the camera to judge it.</summary>
    public static int? ReliableKelvin(AmbientCalibration calibration, AmbientReading reading)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(reading);
        return calibration.BrightestStops is { } reference && reading.LightStops >= reference - ColourReliableBelowReference
            ? reading.Kelvin
            : null;
    }
}
