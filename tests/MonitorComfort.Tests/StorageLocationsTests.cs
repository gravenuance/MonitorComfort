using MonitorComfort.Core.Storage;

namespace MonitorComfort.Tests;

public sealed class StorageLocationsTests : IDisposable
{
    private readonly string _root =
        Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

    private const string Roaming = @"C:\Users\test\AppData\Roaming";
    private const string Local = @"C:\Users\test\AppData\Local";
    private const string DefaultLogs = @"C:\Users\test\AppData\Local\MonitorComfort\logs";
    private const string SettingsFolder = @"C:\Users\test\AppData\Roaming\MonitorComfort";
    private const string LockFile = @"C:\Users\test\AppData\Roaming\MonitorComfort\instance.lock";

    [Fact]
    public void UnsetVariable_KeepsLogsInLocalAppData()
    {
        var locations = StorageLocations.Resolve(Roaming, Local, cacheDirectory: null);

        Assert.Equal(DefaultLogs, locations.LogDirectory);
        Assert.Null(locations.Warning);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyVariable_FallsBackWithoutWarning(string value)
    {
        var locations = StorageLocations.Resolve(Roaming, Local, value);

        Assert.Equal(DefaultLogs, locations.LogDirectory);
        Assert.Null(locations.Warning);
    }

    [Fact]
    public void AbsoluteVariable_PutsLogsInItsLogsSubfolder()
    {
        var locations = StorageLocations.Resolve(Roaming, Local, @"D:\Cache\MonitorComfort");

        Assert.Equal(@"D:\Cache\MonitorComfort\logs", locations.LogDirectory);
        Assert.Equal(DefaultLogs, locations.DefaultLogDirectory);
        Assert.Null(locations.Warning);
    }

    [Theory]
    [InlineData(@"cache\mc")]
    [InlineData("cache")]
    [InlineData(@".\cache")]
    [InlineData(@"\cache")]
    [InlineData("D:cache")]
    public void RelativeVariable_FallsBackAndWarns(string value)
    {
        var locations = StorageLocations.Resolve(Roaming, Local, value);

        Assert.Equal(DefaultLogs, locations.LogDirectory);
        Assert.NotNull(locations.Warning);
        Assert.Contains(StorageLocations.CacheDirVariable, locations.Warning, StringComparison.Ordinal);
        Assert.Contains(value, locations.Warning, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"D:\Cache\MonitorComfort")]
    [InlineData(@"relative\path")]
    public void SettingsFolderAndLockFile_ignoreTheVariable(string? value)
    {
        var locations = StorageLocations.Resolve(Roaming, Local, value);

        Assert.Equal(SettingsFolder, locations.SettingsDirectory);
        Assert.Equal(LockFile, locations.InstanceLockFile);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void CreateDirectories_MakesSettingsAndOverriddenLogFolders()
    {
        var roaming = Path.Combine(_root, "roaming");
        var local = Path.Combine(_root, "local");
        var cache = Path.Combine(_root, "cache");

        var created = StorageLocations.Resolve(roaming, local, cache).CreateDirectories();

        Assert.True(Directory.Exists(Path.Combine(roaming, "MonitorComfort")));
        Assert.True(Directory.Exists(Path.Combine(cache, "logs")));
        Assert.False(Directory.Exists(Path.Combine(local, "MonitorComfort")));
        Assert.Null(created.Warning);
    }

    [Fact]
    public void CreateDirectories_FallsBackWhenTheOverrideIsBlockedByAFile()
    {
        Directory.CreateDirectory(_root);
        var blocker = Path.Combine(_root, "blocker");
        File.WriteAllText(blocker, "not a folder");
        var local = Path.Combine(_root, "local");

        var created = StorageLocations.Resolve(Path.Combine(_root, "roaming"), local, blocker).CreateDirectories();

        var defaultLogs = Path.Combine(local, "MonitorComfort", "logs");
        Assert.Equal(defaultLogs, created.LogDirectory);
        Assert.True(Directory.Exists(defaultLogs));
        Assert.Contains(StorageLocations.CacheDirVariable, created.Warning, StringComparison.Ordinal);
        Assert.Contains("cannot be created", created.Warning, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDirectories_FallsBackWhenTheOverrideHoldsAnIllegalCharacter()
    {
        var local = Path.Combine(_root, "local");
        var illegal = Path.Combine(_root, "bad\0name");

        var created = StorageLocations.Resolve(Path.Combine(_root, "roaming"), local, illegal).CreateDirectories();

        var defaultLogs = Path.Combine(local, "MonitorComfort", "logs");
        Assert.Equal(defaultLogs, created.LogDirectory);
        Assert.True(Directory.Exists(defaultLogs));
        Assert.NotNull(created.Warning);
    }
}
