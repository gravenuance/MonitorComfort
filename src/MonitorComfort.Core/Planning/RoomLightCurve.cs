namespace MonitorComfort.Core.Planning;

/// <summary>How the room-light brightness floor swells and fades across the lit window.</summary>
public static class RoomLightCurve
{
    // A fixed soft bell: a third of the floor at the on and off times, the full floor in the middle.
    private const double EdgeFactor = 1.0 / 3.0;

    /// <summary>
    /// The share of the brightness floor at <paramref name="fraction"/> of the way from the on time to the
    /// off time (0 to 1): 1/3 at both ends, 1 at the middle.
    /// </summary>
    public static double Factor(double fraction)
    {
        var sine = Math.Sin(Math.PI * fraction);
        return EdgeFactor + ((1.0 - EdgeFactor) * sine * sine);
    }
}
