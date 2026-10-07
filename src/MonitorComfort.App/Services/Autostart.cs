using Microsoft.Win32;
using Serilog;

namespace MonitorComfort.App.Services;

/// <summary>Registers the running executable to start at sign-in through the user's Run key.</summary>
internal sealed class Autostart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MonitorComfort";

    private readonly ILogger _log;

    public Autostart(ILogger log) => _log = log.ForContext<Autostart>();

    public void Apply(bool enabled)
    {
        var executable = Environment.ProcessPath;
        if (executable is null || IsDevelopmentBuild(executable))
        {
            _log.Information("Not changing start-with-Windows from a development build at {Path}", executable);
            return;
        }

        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            var command = $"\"{executable}\"";
            if (enabled && !string.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, command);
                _log.Information("Start with Windows enabled for {Path}", executable);
            }
            else if (!enabled && key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName);
                _log.Information("Start with Windows disabled");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            _log.Warning(ex, "Could not update start-with-Windows");
        }
    }

    // A bin\Debug or bin\Release path would go stale with the next build.
    private static bool IsDevelopmentBuild(string executable)
        => executable.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase);
}
