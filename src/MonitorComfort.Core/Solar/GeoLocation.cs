using System.Text.Json.Serialization;

namespace MonitorComfort.Core.Solar;

/// <summary>A point on Earth in decimal degrees; north and east are positive.</summary>
public readonly record struct GeoLocation
{
    // Without this, deserialisation uses the implicit parameterless constructor and skips validation.
    [JsonConstructor]
    public GeoLocation(double latitude, double longitude)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latitude, -90.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(latitude, 90.0);
        ArgumentOutOfRangeException.ThrowIfLessThan(longitude, -180.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(longitude, 180.0);
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }

    public static bool IsValid(double latitude, double longitude)
        => latitude is >= -90.0 and <= 90.0 && longitude is >= -180.0 and <= 180.0;
}
