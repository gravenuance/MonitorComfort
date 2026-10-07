using System.Runtime.InteropServices;
using MonitorComfort.Core.Solar;

namespace MonitorComfort.Tests;

public sealed class LocationLookupTests
{
    // HRESULT_FROM_WIN32(ERROR_SERVICE_DISABLED), what Windows returns when lfsvc is disabled.
    private const int ServiceDisabled = unchecked((int)0x80070422);

    [Fact]
    public async Task Successful_lookup_returns_the_location()
    {
        var result = await LocationLookup.RunAsync(_ => Task.FromResult(new GeoLocation(59.33, 18.07)), CancellationToken.None);

        Assert.Equal(new GeoLocation(59.33, 18.07), result.Location);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task Disabled_location_service_is_reported_not_thrown()
    {
        var exception = await Record.ExceptionAsync(async () =>
        {
            var result = await LocationLookup.RunAsync(_ => throw new COMException("disabled", ServiceDisabled), CancellationToken.None);
            Assert.Null(result.Location);
            Assert.Equal("The Windows location service is turned off.", result.Error);
        });

        Assert.Null(exception);
    }

    [Fact]
    public async Task Denied_access_is_reported_not_thrown()
    {
        var result = await LocationLookup.RunAsync(_ => throw new UnauthorizedAccessException(), CancellationToken.None);

        Assert.Equal("Location is off in Windows privacy settings.", result.Error);
    }

    [Fact]
    public async Task Timeout_is_reported_but_caller_cancellation_still_cancels()
    {
        var timedOut = await LocationLookup.RunAsync(_ => throw new OperationCanceledException(), CancellationToken.None);
        Assert.Equal("Windows couldn't find your location in time.", timedOut.Error);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => LocationLookup.RunAsync(token => Task.FromCanceled<GeoLocation>(token), cancelled.Token));
    }

    [Fact]
    public async Task Other_platform_failures_are_reported_not_thrown()
    {
        var result = await LocationLookup.RunAsync(_ => throw new COMException("boom", unchecked((int)0x80004005)), CancellationToken.None);

        Assert.Equal("Windows couldn't find your location.", result.Error);
    }
}
