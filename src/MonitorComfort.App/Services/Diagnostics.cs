using System.Globalization;
using System.Reflection;
using Serilog;
using Serilog.Events;

namespace MonitorComfort.App.Services;

internal static class Diagnostics
{
    public const string LogLevelVariable = "MONITORCOMFORT_LOG_LEVEL";

    private const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}";

    public static string Version { get; } =
        typeof(Diagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>A size-rotated log: 10 MB per file, five files kept.</summary>
    public static ILogger CreateLogger(AppPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var level = Enum.TryParse<LogEventLevel>(Environment.GetEnvironmentVariable(LogLevelVariable), ignoreCase: true, out var parsed)
            ? parsed
            : LogEventLevel.Information;

        return new LoggerConfiguration()
            .MinimumLevel.Is(level)
            .WriteTo.File(
                Path.Combine(paths.LogDirectory, "monitorcomfort.log"),
                fileSizeLimitBytes: 10 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                retainedFileCountLimit: 5,
                shared: true,
                formatProvider: CultureInfo.InvariantCulture,
                outputTemplate: OutputTemplate)
            .CreateLogger();
    }

    /// <summary>Logs unhandled and unobserved exceptions before the process goes down.</summary>
    public static void InstallCrashHandlers(ILogger log)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; terminating: {Terminating}", e.IsTerminating);
            Log.CloseAndFlush();
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error(e.Exception, "Unobserved task exception");
            e.SetObserved();
        };
    }
}

/// <summary>Holds an exclusive lock on a file for the process lifetime so a second copy can detect the first.</summary>
internal sealed class SingleInstanceGuard : IDisposable
{
    private readonly FileStream _stream;

    private SingleInstanceGuard(FileStream stream) => _stream = stream;

    public static SingleInstanceGuard? TryAcquire(string lockFilePath)
    {
        try
        {
            return new SingleInstanceGuard(new FileStream(lockFilePath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None));
        }
        catch (IOException)
        {
            return null;
        }
    }

    public void Dispose() => _stream.Dispose();
}
