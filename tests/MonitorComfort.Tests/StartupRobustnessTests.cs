using MonitorComfort.Core.Context;
using MonitorComfort.Core.Settings;

namespace MonitorComfort.Tests;

public sealed class StartupRobustnessTests
{
    private const int ToolWindow = 0x80;
    private const int NoActivate = 0x08000000;
    private const int Transparent = 0x20;

    private static readonly PixelRect Primary = new(0, 0, 1920, 1080);
    private static readonly PixelRect Secondary = new(1920, 0, 3840, 1080);

    private static FullscreenCandidate Game(PixelRect monitor, bool primary, bool shellReports) => new(
        monitor, monitor, "UnityWndClass", Visible: true, Cloaked: false, ExtendedStyle: 0,
        OnPrimaryMonitor: primary, ShellReportsFullscreen: shellReports);

    [Fact]
    public void Primary_monitor_counts_only_when_windows_also_reports_full_screen()
    {
        Assert.True(FullscreenRule.IsFullscreen(Game(Primary, primary: true, shellReports: true)));
        Assert.False(FullscreenRule.IsFullscreen(Game(Primary, primary: true, shellReports: false)));
    }

    [Fact]
    public void Secondary_monitor_counts_without_the_shell_signal_windows_never_gives_there()
    {
        Assert.True(FullscreenRule.IsFullscreen(Game(Secondary, primary: false, shellReports: false)));
    }

    [Theory]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("MultitaskingViewFrame")]
    [InlineData("ForegroundStaging")]
    public void Shell_surfaces_are_not_full_screen_content(string windowClass)
    {
        Assert.False(FullscreenRule.IsFullscreen(Game(Secondary, primary: false, shellReports: false) with { WindowClass = windowClass }));
    }

    [Theory]
    [InlineData(ToolWindow)]
    [InlineData(NoActivate)]
    [InlineData(Transparent)]
    public void Overlay_windows_are_not_full_screen_content(int extendedStyle)
    {
        Assert.False(FullscreenRule.IsFullscreen(Game(Secondary, primary: false, shellReports: false) with { ExtendedStyle = extendedStyle }));
    }

    [Fact]
    public void Hidden_or_cloaked_windows_are_not_full_screen_content()
    {
        var game = Game(Secondary, primary: false, shellReports: false);

        Assert.False(FullscreenRule.IsFullscreen(game with { Visible = false }));
        Assert.False(FullscreenRule.IsFullscreen(game with { Cloaked = true }));
    }

    [Fact]
    public void Snapshot_missing_brightness_is_incomplete_and_not_kept()
    {
        var partial = new MonitorSnapshot { MonitorId = "ACR0B70", Name = "KA242Y", ColourPreset = 5 };

        Assert.False(partial.IsComplete);
        Assert.Empty(new OriginalMonitorValues().WithFirstCapture(partial).Monitors);
    }

    [Fact]
    public void Snapshot_with_some_colour_values_missing_is_incomplete()
    {
        var partial = new MonitorSnapshot { MonitorId = "ACR0B70", Name = "KA242Y", Brightness = 60, ColourPreset = 5, RedGain = 50 };

        Assert.False(partial.IsComplete);
    }

    [Fact]
    public void Snapshot_is_complete_with_all_values_or_with_brightness_alone()
    {
        var full = new MonitorSnapshot
        {
            MonitorId = "ACR0B70", Name = "KA242Y", Brightness = 60, ColourPreset = 5, RedGain = 50, GreenGain = 50, BlueGain = 50,
        };
        var brightnessOnly = new MonitorSnapshot { MonitorId = "DEL0001", Name = "Basic", Brightness = 70 };

        Assert.True(full.IsComplete);
        Assert.True(brightnessOnly.IsComplete);
    }
}
