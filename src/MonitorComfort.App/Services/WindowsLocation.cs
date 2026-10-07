using MonitorComfort.Core.Solar;
using Windows.Devices.Geolocation;

namespace MonitorComfort.App.Services;

/// <summary>One-shot position from the Windows location service; city-level accuracy is plenty.</summary>
internal static class WindowsLocation
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static Task<LocationResult> GetAsync(CancellationToken cancellationToken)
        => LocationLookup.RunAsync(LocateAsync, cancellationToken);

    private static async Task<GeoLocation> LocateAsync(CancellationToken cancellationToken)
    {
        // Both calls throw rather than return a status when the service is disabled or access is denied.
        if (await Geolocator.RequestAccessAsync().AsTask(cancellationToken) != GeolocationAccessStatus.Allowed)
        {
            throw new UnauthorizedAccessException("Location access denied.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
        var position = await locator.GetGeopositionAsync().AsTask(timeout.Token);
        var point = position.Coordinate.Point.Position;
        return new GeoLocation(Math.Round(point.Latitude, 2), Math.Round(point.Longitude, 2));
    }
}
