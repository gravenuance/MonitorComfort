using System.Runtime.InteropServices;
using MonitorComfort.App.Native;
using MonitorComfort.Core.Ambient;
using Serilog;

namespace MonitorComfort.App.Ambient;

/// <summary>A camera result and the camera it came from, or a short reason it couldn't be had.</summary>
internal sealed record MeterResult<T>(T? Value, string? Error, CameraDevice? Camera = null)
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

    // Each takes the chosen camera's id, or null for the first camera that can meter.
    public Task<MeterResult<CameraSample>> MeasureAsync(string? cameraId, CancellationToken cancellationToken)
        => RunAsync(cameraId, (session, token) => session.Measure(token), "measure", cancellationToken);

    public Task<MeterResult<CameraSweeps>> SweepAsync(string? cameraId, CancellationToken cancellationToken)
        => RunAsync(cameraId, (session, token) => session.Sweep(token), "measure the response of", cancellationToken);

    public Task<MeterResult<CameraProbe>> ProbeAsync(string? cameraId, int framesPerStep, CancellationToken cancellationToken)
        => RunAsync(cameraId, (session, token) => session.Probe(framesPerStep, token), "probe", cancellationToken);

    /// <summary>The cameras Windows lists, or none when they can't be listed.</summary>
    public async Task<IReadOnlyList<CameraDevice>> ListAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(
                () =>
                {
                    EnsureStarted();
                    return CameraSession.List();
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is COMException or CameraException)
        {
            _log.Warning(ex, "Could not list the cameras (HRESULT 0x{HResult:X8})", ex.HResult);
            return [];
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<MeterResult<T>> RunAsync<T>(string? cameraId, Func<CameraSession, CancellationToken, T> work, string action, CancellationToken cancellationToken)
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
                    using var session = CameraSession.Open(
                        cameraId,
                        (camera, reason) => _log.Information("Skipping camera {Name} ({Id}): {Reason}", camera.Name, camera.Id, reason.Message));
                    if (session is null)
                    {
                        return new MeterResult<T>(null, cameraId is null ? "No camera found." : "The calibrated camera isn't connected.");
                    }

                    var value = work(session, cancellationToken);
                    _log.Debug("Camera {Name}: {Action} done", session.Device.Name, action);
                    return new MeterResult<T>(value, null, session.Device);
                },
                cancellationToken);
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.Warning(ex, "Could not {Action} the camera: access denied", action);
            return new MeterResult<T>(null, "Camera access is off in Windows privacy settings.");
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
