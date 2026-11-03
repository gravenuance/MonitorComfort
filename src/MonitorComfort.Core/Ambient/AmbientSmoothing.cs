namespace MonitorComfort.Core.Ambient;

/// <summary>
/// Steadies automatic readings, so the screens hold still while the room doesn't perceptibly change.
/// About 0.1-0.2 stops of reading noise would otherwise move brightness by several points.
/// </summary>
public static class AmbientSmoothing
{
    /// <summary>Changes smaller than this are taken as noise; about 8 brightness points at the user's calibration.</summary>
    public const double DeadbandStops = 0.25;

    /// <summary>Changes this large, such as the lights going off, are followed at once.</summary>
    public const double JumpStops = 1.0;

    /// <summary>
    /// The most the light's colour moves per reading while the light level drifts. At night the screens
    /// add to what the camera sees, so following their own warmth at once could pull them ever warmer.
    /// </summary>
    public const int MaximumKelvinStep = 300;

    /// <summary>
    /// The reading to act on after <paramref name="raw"/>: <paramref name="held"/> kept (and refreshed) for a
    /// change inside the deadband, half the way (and the colour at most <see cref="MaximumKelvinStep"/>) for a
    /// moderate change, and the new reading for a large one or when nothing fresh is held.
    /// </summary>
    public static AmbientReading Next(AmbientReading? held, AmbientReading raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        if (held is null || !AmbientMapping.IsFresh(held, raw.At))
        {
            return raw;
        }

        var change = raw.LightStops - held.LightStops;
        return Math.Abs(change) switch
        {
            < DeadbandStops => held with { At = raw.At },
            < JumpStops => raw with { LightStops = held.LightStops + (change / 2), Kelvin = StepColour(held.Kelvin, raw.Kelvin) },
            _ => raw,
        };
    }

    private static int? StepColour(int? held, int? raw)
        => held is { } from && raw is { } to ? from + Math.Clamp(to - from, -MaximumKelvinStep, MaximumKelvinStep) : raw;
}
