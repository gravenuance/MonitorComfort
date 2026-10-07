namespace MonitorComfort.Core.Ambient;

/// <summary>One webcam measurement: light in stops on the camera's scale, and the light's colour if known.</summary>
public sealed record AmbientReading(DateTimeOffset At, double LightStops, int? Kelvin);

/// <summary>The light level at which the user chose a comfortable brightness.</summary>
public sealed record AmbientCalibration(double ReferenceStops, int ReferenceBrightness);

public static class AmbientMapping
{
    /// <summary>Readings older than this fall back to the room-light model.</summary>
    public static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(30);

    /// <summary>Below this many stops under the calibration light, the camera mostly sees the screens' own glow.</summary>
    public const double ColourReliableBelowReference = 3.0;

    private const int LowestBrightness = 5;
    private const int HighestBrightness = 100;

    public static bool IsFresh(AmbientReading reading, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(reading);
        var age = now - reading.At;
        return age >= TimeSpan.Zero && age <= MaximumAge;
    }

    /// <summary>Brightness moves by <paramref name="brightnessPerStop"/> for each doubling or halving of the light.</summary>
    public static int Brightness(AmbientCalibration calibration, double lightStops, double brightnessPerStop)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        var brightness = calibration.ReferenceBrightness + (brightnessPerStop * (lightStops - calibration.ReferenceStops));
        return (int)Math.Clamp(Math.Round(brightness), LowestBrightness, HighestBrightness);
    }

    /// <summary>The light's colour, when the room is bright enough for the camera to judge it.</summary>
    public static int? ReliableKelvin(AmbientCalibration calibration, AmbientReading reading)
    {
        ArgumentNullException.ThrowIfNull(calibration);
        ArgumentNullException.ThrowIfNull(reading);
        return reading.LightStops >= calibration.ReferenceStops - ColourReliableBelowReference ? reading.Kelvin : null;
    }
}
