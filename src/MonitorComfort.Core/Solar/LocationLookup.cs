using System.Runtime.InteropServices;

namespace MonitorComfort.Core.Solar;

public sealed record LocationResult(GeoLocation? Location, string? Error);

/// <summary>Runs a platform location lookup and turns its failures into a short message.</summary>
public static class LocationLookup
{
    // HRESULT_FROM_WIN32(ERROR_SERVICE_DISABLED): the Windows geolocation service (lfsvc) is disabled.
    private const int ServiceDisabled = unchecked((int)0x80070422);

    public static async Task<LocationResult> RunAsync(Func<CancellationToken, Task<GeoLocation>> source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            return new LocationResult(await source(cancellationToken), null);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new LocationResult(null, "Windows couldn't find your location in time.");
        }
        catch (UnauthorizedAccessException)
        {
            return new LocationResult(null, "Location is off in Windows privacy settings.");
        }
        catch (COMException ex) when (ex.HResult == ServiceDisabled)
        {
            return new LocationResult(null, "The Windows location service is turned off.");
        }
        catch (COMException)
        {
            return new LocationResult(null, "Windows couldn't find your location.");
        }
    }
}
