namespace MonitorComfort.Core.Planning;

/// <summary>
/// Fits the schedule's colour temperature to the room light's. Once the sun is down the two are
/// averaged, and the screens never end up much warmer than the room.
/// </summary>
public static class RoomWarmth
{
    /// <summary>A screen much warmer than the room's light reads as orange; it stays within this of it.</summary>
    public const int MaximumWarmerThanRoom = 500;

    /// <summary>The room's share of the colour once the sun is well below the horizon.</summary>
    public const double DarkRoomShare = 0.5;

    /// <summary>Up to this gap between the schedule and the room, the room counts in full.</summary>
    public const int FullTrustGap = 1000;

    /// <summary>From this gap on, the room's colour is probably misjudged (mixed light, screen glow) and isn't averaged in.</summary>
    public const int NoTrustGap = 1500;

    private const double MiredsTimesKelvin = 1_000_000;

    /// <summary>
    /// The colour temperature to show, given the schedule's, the room light's, and how far the sun is up
    /// (0 dark to 1 day). The average is taken in mireds, which follow how the eye sees colour shifts,
    /// and the room's share fades out between <see cref="FullTrustGap"/> and <see cref="NoTrustGap"/>
    /// so the result never jumps.
    /// </summary>
    public static int Fit(int scheduleKelvin, int roomKelvin, double daylight)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scheduleKelvin);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(roomKelvin);
        ArgumentOutOfRangeException.ThrowIfLessThan(daylight, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(daylight, 1.0);

        var share = DarkRoomShare * (1.0 - daylight) * Trust(Math.Abs(scheduleKelvin - roomKelvin));
        var mireds = ((1.0 - share) * MiredsTimesKelvin / scheduleKelvin) + (share * MiredsTimesKelvin / roomKelvin);
        var blended = (int)Math.Round(MiredsTimesKelvin / mireds);
        return Math.Max(blended, roomKelvin - MaximumWarmerThanRoom);
    }

    private static double Trust(int gap)
        => Math.Clamp((double)(NoTrustGap - gap) / (NoTrustGap - FullTrustGap), 0.0, 1.0);
}
