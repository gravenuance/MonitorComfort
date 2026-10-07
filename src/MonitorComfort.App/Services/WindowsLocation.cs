using MonitorComfort.Core.Solar;
using Windows.Devices.Geolocation;

namespace MonitorComfort.App.Services;

public sealed record LocationResult(GeoLocation? Location, string? Error);

/// <summary>One-shot position from the Windows location service; city-level accuracy is plenty.</summary>
internal static class WindowsLocation
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public static async Task<LocationResult> GetAsync(CancellationToken cancellationToken)
    {
        var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken);
        if (access != GeolocationAccessStatus.Allowed)
        {
            return new LocationResult(null, "Location is off in Windows privacy settings.");
        }

        try
        {
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(Timeout);
            var position = await locator.GetGeopositionAsync().AsTask(timeout.Token);
            var point = position.Coordinate.Point.Position;
            return new LocationResult(new GeoLocation(Math.Round(point.Latitude, 2), Math.Round(point.Longitude, 2)), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LocationResult(null, "Windows couldn't find your location in time.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return new LocationResult(null, "Windows couldn't find your location.");
        }
    }
}
