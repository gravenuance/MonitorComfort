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
    /// The reading to act on after <paramref name="raw"/>: <paramref name="held"/> kept (and refreshed) for a
    /// change inside the deadband, half the way for a moderate change, and the new reading for a large one or
    /// when nothing fresh is held.
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
            < JumpStops => raw with { LightStops = held.LightStops + (change / 2) },
            _ => raw,
        };
    }
}
