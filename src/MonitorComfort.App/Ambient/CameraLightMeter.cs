using System.Runtime.InteropServices;
using MonitorComfort.App.Native;
using Serilog;

namespace MonitorComfort.App.Ambient;

/// <summary>A camera result, or a short reason it couldn't be had.</summary>
internal sealed record MeterResult<T>(T? Value, string? Error)
    where T : class;

/// <summary>
/// Uses the webcam as a light meter. One use at a time; frames are averaged in memory and
/// never stored.
/// </summary>
internal sealed class CameraLightMeter : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger _log;
    private int _started;

    public CameraLightMeter(ILogger log) => _log = log.ForContext<CameraLightMeter>();

    public Task<MeterResult<CameraSample>> MeasureAsync(CancellationToken cancellationToken)
        => RunAsync((session, token) => session.Measure(token), "measure", cancellationToken);

    public Task<MeterResult<CameraSweeps>> SweepAsync(CancellationToken cancellationToken)
        => RunAsync((session, token) => session.Sweep(token), "measure the response of", cancellationToken);

    public void Dispose() => _gate.Dispose();

    private async Task<MeterResult<T>> RunAsync<T>(Func<CameraSession, CancellationToken, T> work, string action, CancellationToken cancellationToken)
        where T : class
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // Media Foundation calls block, so the camera work runs off the caller's thread.
            return await Task.Run(
                () =>
                {
                    EnsureStarted();
                    using var session = CameraSession.OpenFirst();
                    if (session is null)
                    {
                        return new MeterResult<T>(null, "No camera found.");
                    }

                    var value = work(session, cancellationToken);
                    _log.Debug("Camera {Name}: {Action} done", session.Name, action);
                    return new MeterResult<T>(value, null);
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is COMException or CameraException)
        {
            _log.Warning(ex, "Could not {Action} the camera (HRESULT 0x{HResult:X8}); it may be in use", action, ex.HResult);
            return new MeterResult<T>(null, "Camera busy or unavailable.");
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidCastException or TimeoutException)
        {
            _log.Warning(ex, "Could not {Action} the camera", action);
            return new MeterResult<T>(null, "This camera can't be used as a light meter.");
        }
        finally
        {
            _gate.Release();
        }
    }

    private void EnsureStarted()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1)
        {
            return;
        }

        var hresult = NativeMethods.MFStartup(MediaFoundation.Version, 0);
        if (hresult < 0)
        {
            Interlocked.Exchange(ref _started, 0);
            throw new CameraException("Media Foundation did not start.", hresult);
        }
    }
}
