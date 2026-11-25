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

    /// <summary>The current user's folders, created, honouring <c>MONITORCOMFORT_CACHE_DIR</c>.</summary>
    public static StorageLocations ForCurrentUser()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.Create);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create);
        return Resolve(roaming, local, Environment.GetEnvironmentVariable(CacheDirVariable)).CreateDirectories();
    }

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

    /// <summary>Creates the folders. A log folder that cannot be created falls back to the default, so a bad override never stops the tray app from starting.</summary>
    public StorageLocations CreateDirectories()
    {
        Directory.CreateDirectory(SettingsDirectory);
        try
        {
            Directory.CreateDirectory(LogDirectory);
            return this;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            && !string.Equals(LogDirectory, DefaultLogDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(DefaultLogDirectory);
            return this with
            {
                LogDirectory = DefaultLogDirectory,
                Warning = $"{CacheDirVariable} points at '{LogDirectory}', which cannot be created ({ex.Message}); using {DefaultLogDirectory} instead.",
            };
        }
    }
}
