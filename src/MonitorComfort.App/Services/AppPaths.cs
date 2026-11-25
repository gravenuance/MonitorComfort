using MonitorComfort.Core.Storage;

namespace MonitorComfort.App.Services;

/// <summary>Settings and the instance lock in %APPDATA%; logs in <c>MONITORCOMFORT_CACHE_DIR</c> or %LOCALAPPDATA%; nothing next to the executable.</summary>
/// <param name="Warning">Why the configured cache folder was not used, for the host to log once logging is up.</param>
internal sealed record AppPaths(string SettingsDirectory, string LogDirectory, string InstanceLockFile, string? Warning)
{
    public static AppPaths Resolve()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        var locations = StorageLocations
            .Resolve(roaming, local, Environment.GetEnvironmentVariable(StorageLocations.CacheDirVariable))
            .CreateDirectories();
        return new AppPaths(locations.SettingsDirectory, locations.LogDirectory, locations.InstanceLockFile, locations.Warning);
    }
}
