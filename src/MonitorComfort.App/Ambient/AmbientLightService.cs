using MonitorComfort.App.Native;
using MonitorComfort.App.Services;
using MonitorComfort.Core.Ambient;
using MonitorComfort.Core.Settings;
using Serilog;

namespace MonitorComfort.App.Ambient;

/// <summary>
/// Takes a webcam light reading on the configured interval while the light meter is on and
/// calibrated, and handles calibration. Readings are kept in memory only.
/// </summary>
internal sealed class AmbientLightService : IAsyncDisposable
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    // Nobody to adjust the screens for: skip readings so the camera light doesn't blink in an empty room.
    private static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(15);

    private readonly SettingsService _settings;
    private readonly CameraLightMeter _meter;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private readonly Func<bool> _isPaused;
    private readonly CancellationTokenSource _stop = new();
    private Task _loop = Task.CompletedTask;
    private AmbientReading? _latest;
    private long _lastAttemptTicks = DateTimeOffset.MinValue.UtcTicks;

    public AmbientLightService(SettingsService settings, CameraLightMeter meter, TimeProvider time, ILogger log, Func<bool> isPaused)
    {
        _settings = settings;
        _meter = meter;
        _time = time;
        _log = log.ForContext<AmbientLightService>();
        _isPaused = isPaused;
    }

    /// <summary>Raised from a background thread after each new reading.</summary>
    public event EventHandler? ReadingChanged;

    public AmbientReading? Latest => Volatile.Read(ref _latest);

    public string? LastError { get; private set; }

    public void Start() => _loop = Task.Run(() => RunAsync(_stop.Token));

    /// <summary>Takes a reading now and acts on it as is; null with <see cref="LastError"/> set when it couldn't.</summary>
    public Task<AmbientReading?> MeasureNowAsync(CancellationToken cancellationToken)
        => MeasureAsync(steady: false, cancellationToken);

    private async Task<AmbientReading?> MeasureAsync(bool steady, CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _lastAttemptTicks, _time.GetUtcNow().UtcTicks);
        var response = _settings.Current.CameraLight.Response;
        if (response is null)
        {
            LastError = "Calibrate the camera first.";
            return null;
        }

        var result = await _meter.MeasureAsync(cancellationToken);
        if (result.Value is not { } sample)
        {
            LastError = result.Error;
            return null;
        }

        return Record(sample, response, steady);
    }

    /// <summary>
    /// Measures the camera's response the first time, then records the current light as the
    /// <paramref name="level"/> point at which <paramref name="comfortableBrightness"/> feels right.
    /// Returns an error message or null.
    /// </summary>
    public async Task<string?> CalibrateAsync(
        CalibrationLevel level, int comfortableBrightness, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        var response = _settings.Current.CameraLight.Response;
        var existing = _settings.Current.CameraLight.Calibration ?? AmbientCalibration.Empty;
        if (response is null)
        {
            // Points measured against an earlier response sit on a different scale.
            existing = AmbientCalibration.Empty;
            progress?.Report("Measuring the camera…");
            var sweep = await _meter.SweepAsync(cancellationToken);
            if (sweep.Value is not { } sweeps)
            {
                return sweep.Error;
            }

            response = CameraResponse.Fit(sweeps.Exposure, sweeps.Gain);
            _log.Information(
                "Camera response from sweeps {Exposure} and {Gain}: {Response}",
                sweeps.Exposure,
                sweeps.Gain,
                response);
            if (response is null)
            {
                return "Couldn't measure the camera. Try with more light.";
            }
        }

        progress?.Report("Measuring the room light…");
        var result = await _meter.MeasureAsync(cancellationToken);
        if (result.Value is not { } sample)
        {
            return result.Error;
        }

        var reading = Record(sample, response, steady: false);
        if (reading is null)
        {
            return LastError;
        }

        var (calibration, error) = existing.With(level, new CalibrationPoint(reading.LightStops, comfortableBrightness));
        if (calibration is null)
        {
            _log.Information("Rejected the {Level} calibration at {Stops:0.00} stops and {Brightness}%: {Error}", level, reading.LightStops, comfortableBrightness, error);
            return error;
        }

        _settings.Update(s => s with
        {
            CameraLight = s.CameraLight with { Enabled = true, Response = response, Calibration = calibration },
        });
        _log.Information("Calibrated the light meter ({Level}): {Calibration}", level, calibration);
        ReadingChanged?.Invoke(this, EventArgs.Empty);
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _loop;
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        _stop.Dispose();
    }

    // A steady reading goes through AmbientSmoothing; the returned reading is always the raw one.
    private AmbientReading? Record(CameraSample sample, CameraResponse response, bool steady)
    {
        var stops = response.LightStops(sample.MeanLinearY, sample.ExposureLog2, sample.Gain);
        _log.Information(
            "Light reading: exposure 2^{Exposure} s, gain {Gain}, white balance {WhiteBalance}K, brightness {MeanY:0.0000} -> {Stops:0.00} stops",
            sample.ExposureLog2,
            sample.Gain,
            sample.WhiteBalanceKelvin,
            sample.MeanLinearY,
            stops);
        if (stops is not { } value)
        {
            LastError = "Too dark to measure.";
            return null;
        }

        LastError = null;
        var reading = new AmbientReading(_time.GetUtcNow(), value, sample.WhiteBalanceKelvin);
        var acted = steady ? AmbientSmoothing.Next(Latest, reading) : reading;
        if (acted.LightStops != reading.LightStops)
        {
            _log.Information("Acting on {Acted:0.00} stops to keep the screens steady", acted.LightStops);
        }

        Volatile.Write(ref _latest, acted);
        ReadingChanged?.Invoke(this, EventArgs.Empty);
        return reading;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Tick, _time);
        do
        {
            try
            {
                var camera = _settings.Current.CameraLight;
                var lastAttempt = new DateTimeOffset(Interlocked.Read(ref _lastAttemptTicks), TimeSpan.Zero);
                var due = _time.GetUtcNow() - lastAttempt >= TimeSpan.FromMinutes(camera.IntervalMinutes);
                if (camera.IsReady && due && !_isPaused() && !UserIsIdle())
                {
                    await MeasureAsync(steady: true, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.Error(ex, "Light reading failed; retrying next interval");
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }

    private static unsafe bool UserIsIdle()
    {
        var info = new NativeMethods.LastInputInfo { Size = (uint)sizeof(NativeMethods.LastInputInfo) };
        if (!NativeMethods.GetLastInputInfo(&info))
        {
            return false;
        }

        var idleMilliseconds = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(idleMilliseconds) >= IdleLimit;
    }
}
