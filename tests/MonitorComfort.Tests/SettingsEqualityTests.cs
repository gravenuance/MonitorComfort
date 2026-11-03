using MonitorComfort.Core.Settings;

namespace MonitorComfort.Tests;

// The settings service skips re-planning and saving when an update leaves the settings equal.
public sealed class SettingsEqualityTests
{
    [Fact]
    public void Rebuilding_the_same_monitor_settings_leaves_settings_equal()
    {
        var settings = new ComfortSettings().WithMonitor("ACR0B70", new MonitorSettings { BrightnessOffset = 5 });

        var rebuilt = settings.WithMonitor("ACR0B70", new MonitorSettings { BrightnessOffset = 5 });

        Assert.Equal(settings, rebuilt);
        Assert.Equal(settings.GetHashCode(), rebuilt.GetHashCode());
    }

    [Fact]
    public void Monitor_ids_compare_ignoring_case()
    {
        var upper = new ComfortSettings { Monitors = new Dictionary<string, MonitorSettings> { ["SAM08DA"] = new() { Enabled = false } } };
        var lower = new ComfortSettings { Monitors = new Dictionary<string, MonitorSettings> { ["sam08da"] = new() { Enabled = false } } };

        Assert.Equal(upper, lower);
        Assert.Equal(upper.GetHashCode(), lower.GetHashCode());
    }

    [Fact]
    public void A_changed_monitor_setting_makes_settings_differ()
    {
        var settings = new ComfortSettings().WithMonitor("ACR0B70", new MonitorSettings { BrightnessOffset = 5 });

        Assert.NotEqual(settings, settings.WithMonitor("ACR0B70", new MonitorSettings { BrightnessOffset = 6 }));
        Assert.NotEqual(settings, settings.WithMonitor("SAM08DA", new MonitorSettings { BrightnessOffset = 5 }));
    }
}
