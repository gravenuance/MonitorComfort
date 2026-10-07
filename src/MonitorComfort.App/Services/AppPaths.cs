namespace MonitorComfort.App.Services;

/// <summary>Settings in %APPDATA%, logs in %LOCALAPPDATA%; nothing next to the executable.</summary>
internal sealed record AppPaths(string SettingsDirectory, string LogDirectory)
{
    private const string FolderName = "MonitorComfort";

    public static AppPaths Resolve()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        var paths = new AppPaths(Path.Combine(roaming, FolderName), Path.Combine(local, FolderName, "logs"));
        Directory.CreateDirectory(paths.SettingsDirectory);
        Directory.CreateDirectory(paths.LogDirectory);
        return paths;
    }
}
