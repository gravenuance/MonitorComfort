using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using MonitorComfort.App.Services;
using MonitorComfort.Core.Ambient;
using Serilog;

namespace MonitorComfort.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var paths = AppPaths.Resolve();
        var log = Diagnostics.CreateLogger(paths);
        Log.Logger = log;
        Diagnostics.InstallCrashHandlers(log);

        using var instance = SingleInstanceGuard.TryAcquire(Path.Combine(paths.LogDirectory, "instance.lock"));
        if (instance is null)
        {
            log.Information("Another MonitorComfort instance is running; exiting");
            Log.CloseAndFlush();
            return 0;
        }

        log.Information(
            "MonitorComfort {Version} starting on {OS} ({Architecture}); settings in {SettingsDirectory}; log level from {LogLevelVariable}",
            Diagnostics.Version,
            RuntimeInformation.OSDescription,
            RuntimeInformation.OSArchitecture,
            paths.SettingsDirectory,
            Diagnostics.LogLevelVariable);

        try
        {
            App.Host = new AppHost(paths, log);
            if (args.Contains("--restore", StringComparer.OrdinalIgnoreCase))
            {
                log.Information("Restoring original monitor settings on request");
#pragma warning disable CA2025 // One-shot command before any UI exists; no synchronization context to deadlock.
                Task.Run(() => App.Host.Controller.RestoreAsync(CancellationToken.None)).GetAwaiter().GetResult();
#pragma warning restore CA2025
                return 0;
            }

            if (TryRunLightCommand(App.Host, args, log) is { } lightExitCode)
            {
                return lightExitCode;
            }

            var exitCode = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            log.Information("MonitorComfort exited with code {ExitCode}", exitCode);
            return exitCode;
        }
        catch (Exception ex)
        {
            log.Fatal(ex, "MonitorComfort terminated unexpectedly");
            return 1;
        }
        finally
        {
            if (App.Host is { } host)
            {
                // Shutdown boundary: the UI loop has ended. Task.Run keeps the dispose's awaits off any
                // leftover synchronization context, so blocking here cannot deadlock.
#pragma warning disable CA2025 // Completes before the host goes out of scope.
                Task.Run(() => host.DisposeAsync().AsTask()).GetAwaiter().GetResult();
#pragma warning restore CA2025
            }

            Log.CloseAndFlush();
        }
    }

    // Diagnostics for the webcam light meter; results go to the log, as the app has no console.
    private static int? TryRunLightCommand(AppHost host, string[] args, ILogger log)
    {
#pragma warning disable CA2025 // One-shot commands before any UI exists; no synchronization context to deadlock.
        if (args.Contains("--measure-light", StringComparer.OrdinalIgnoreCase))
        {
            var reading = Task.Run(() => host.Ambient.MeasureNowAsync(CancellationToken.None)).GetAwaiter().GetResult();
            log.Information("Measure light: {Reading} {Error}", reading, host.Ambient.LastError);
            return reading is null ? 1 : 0;
        }

        if (args.Contains("--probe-light", StringComparer.OrdinalIgnoreCase))
        {
            var result = Task.Run(() => host.Meter.ProbeAsync(host.Settings.Current.CameraLight.CameraId, 30, CancellationToken.None)).GetAwaiter().GetResult();
            if (result.Value is not { } probe)
            {
                log.Error("Probe light: {Error}", result.Error);
                return 1;
            }

            log.Information(
                "Probe light: automatic exposure reported 2^{Exposure} s, gain {Gain}; frames {Frames}",
                probe.SettledExposureLog2,
                probe.SettledGain,
                Format(probe.SettledFramesY));
            foreach (var step in probe.Steps)
            {
                log.Information("Probe light: locked 2^{Exposure} s, gain {Gain}; frames {Frames}", step.ExposureLog2, step.Gain, Format(step.FramesY));
            }

            return 0;
        }

        var index = Array.FindIndex(args, a => a.Equals("--calibrate-light", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out var brightness) || brightness is < 0 or > 100)
            {
                log.Error("--calibrate-light needs the comfortable brightness now, 0-100");
                return 2;
            }

            var level = args.Contains("--dim", StringComparer.OrdinalIgnoreCase) ? CalibrationLevel.Dim : CalibrationLevel.Bright;
            var error = Task.Run(() => host.Ambient.CalibrateAsync(level, brightness, null, CancellationToken.None)).GetAwaiter().GetResult();
            log.Information("Calibrate light ({Level}) at {Brightness}%: {Result}", level, brightness, error ?? "done");
            return error is null ? 0 : 1;
        }
#pragma warning restore CA2025

        return null;
    }

    private static string Format(IReadOnlyList<double> values)
        => string.Join(' ', values.Select(v => v.ToString("0.0000", CultureInfo.InvariantCulture)));

    // Also used by the Avalonia design-time tooling.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
