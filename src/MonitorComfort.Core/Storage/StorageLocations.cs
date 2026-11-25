namespace MonitorComfort.Core.Storage;

/// <summary>Where the app keeps its files, decided from plain strings so it can be tested.</summary>
/// <param name="SettingsDirectory">Fixed in the roaming profile.</param>
/// <param name="LogDirectory">Rebuildable; <c>MONITORCOMFORT_CACHE_DIR</c> can move it.</param>
/// <param name="DefaultLogDirectory">Where logs go without the variable.</param>
/// <param name="InstanceLockFile">In the settings folder, never the log folder, so no setting can make two instances disagree about it.</param>
/// <param name="Warning">Set when the variable held a value that cannot be used and the default applies.</param>
public sealed record StorageLocations(
    string SettingsDirectory,
    string LogDirectory,
    string DefaultLogDirectory,
    string InstanceLockFile,
    string? Warning)
{
    public const string CacheDirVariable = "MONITORCOMFORT_CACHE_DIR";

    private const string FolderName = "MonitorComfort";

    public static StorageLocations Resolve(string roamingAppData, string localAppData, string? cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roamingAppData);
        ArgumentException.ThrowIfNullOrWhiteSpace(localAppData);

        var settings = Path.Combine(roamingAppData, FolderName);
        var defaultLogs = Path.Combine(localAppData, FolderName, "logs");
        var lockFile = Path.Combine(settings, "instance.lock");

        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            return new StorageLocations(settings, defaultLogs, defaultLogs, lockFile, Warning: null);
        }

        if (!Path.IsPathFullyQualified(cacheDirectory))
        {
            return new StorageLocations(
                settings,
                defaultLogs,
                defaultLogs,
                lockFile,
                $"{CacheDirVariable} is '{cacheDirectory}', which is not an absolute path; using {defaultLogs} instead.");
        }

        return new StorageLocations(settings, Path.Combine(cacheDirectory, "logs"), defaultLogs, lockFile, Warning: null);
    }
}
