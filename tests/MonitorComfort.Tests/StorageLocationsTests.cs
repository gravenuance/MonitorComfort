using MonitorComfort.Core.Storage;

namespace MonitorComfort.Tests;

public sealed class StorageLocationsTests
{
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
}
