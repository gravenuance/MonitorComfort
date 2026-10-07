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

    // Also used by the Avalonia design-time tooling.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
