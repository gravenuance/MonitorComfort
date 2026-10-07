using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using MonitorComfort.App.Services;
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

        var index = Array.FindIndex(args, a => a.Equals("--calibrate-light", StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], CultureInfo.InvariantCulture, out var brightness) || brightness is < 0 or > 100)
            {
                log.Error("--calibrate-light needs the comfortable brightness now, 0-100");
                return 2;
            }

            var error = Task.Run(() => host.Ambient.CalibrateAsync(brightness, null, CancellationToken.None)).GetAwaiter().GetResult();
            log.Information("Calibrate light at {Brightness}%: {Result}", brightness, error ?? "done");
            return error is null ? 0 : 1;
        }
#pragma warning restore CA2025

        return null;
    }

    // Also used by the Avalonia design-time tooling.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
