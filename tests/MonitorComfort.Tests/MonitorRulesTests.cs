using Microsoft.Extensions.Time.Testing;
using MonitorComfort.Core.Colour;
using MonitorComfort.Core.Context;
using MonitorComfort.Core.Monitors;

namespace MonitorComfort.Tests;

public sealed class MonitorRulesTests
{
    // Captured from the real monitors over DDC/CI.
    private const string AcerCapabilities =
        "(prot(monitor)type(LCD))model(ACER)cmds(01 02 03 07 0C E3 F3)vcp(04 10 12 14(05 06 08 0B) 16 18 1A 59 5A 5B 5C 5D 5E 60(01 11)  9B 9C 9D 9E 9F A0 CC(01 02 03 04 05 06 07 08 09 0a 0c 0d 0e 14 16 1E) D6(01 02 04 05) E0(00 04 05) E1(01 05 06) E2(00 01 02 03 04 09 0C 0D 14 15 16) E3 E4 E5 E7(00 01 02) E8(00 01 02 03 04)) mswhql(1)asset_eep(40)mccs_ver(2.2))";

    private const string SamsungCapabilities =
        "(prot(monitor)type(LCD)model(370)mccs_ver(2.0)vcp(04 05 08 10 12 14(03 04 02 07 08 0B) 16 18 1A 60(01 05) 87 B0(01 02) B6 C6 C8 C9 D6(01 04) DC(01 02 03 04 05 06 F0 F1 F2 F9 FA FB) DB(00 04 FD FE) DF E8(00 07 09 0A FE) E9 EB(00 01 09 FD) EC(00 01 02 03 04 06 05) F0(00 01 02 03) F2 F3(00 01 02) F6 F7(00 02 03) )mswhql(1))";

    [Fact]
    public void Write_policy_sends_everything_when_current_values_are_unknown()
    {
        var writes = WritePolicy.Writes(null, new MonitorLevels(40, new RgbGains(50, 41, 33)));

        Assert.Equal(
            [new(VcpCode.Brightness, 40), new(VcpCode.RedGain, 50), new(VcpCode.GreenGain, 41), new(VcpCode.BlueGain, 33)],
            writes);
    }

    [Fact]
    public void Write_policy_holds_back_one_step_changes_and_sends_larger_ones()
    {
        var current = new MonitorLevels(40, new RgbGains(50, 41, 33));

        var writes = WritePolicy.Writes(current, new MonitorLevels(41, new RgbGains(50, 39, 32)));

        Assert.Equal([new VcpWrite(VcpCode.GreenGain, 39)], writes);
    }

    [Theory]
    [InlineData(AcerCapabilities, 0x0B, true)]
    [InlineData(AcerCapabilities, 0x04, false)]
    [InlineData(SamsungCapabilities, 0x0B, true)]
    [InlineData(SamsungCapabilities, 0x04, true)]
    public void Capabilities_list_the_colour_presets_each_monitor_offers(string capabilities, int preset, bool expected)
    {
        Assert.Equal(expected, Capabilities.Parse(capabilities).SupportsValue(VcpCode.ColourPreset, preset));
    }

    [Theory]
    [InlineData(AcerCapabilities)]
    [InlineData(SamsungCapabilities)]
    public void Capabilities_include_brightness_and_all_three_gains(string capabilities)
    {
        var parsed = Capabilities.Parse(capabilities);

        Assert.True(parsed.Supports(VcpCode.Brightness));
        Assert.True(parsed.Supports(VcpCode.RedGain));
        Assert.True(parsed.Supports(VcpCode.GreenGain));
        Assert.True(parsed.Supports(VcpCode.BlueGain));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("(prot(monitor)type(LCD))")]
    public void Capabilities_without_a_vcp_list_support_nothing(string? capabilities)
    {
        Assert.False(Capabilities.Parse(capabilities).Supports(VcpCode.Brightness));
    }

    [Fact]
    public void Edid_name_comes_from_the_monitor_name_descriptor()
    {
        var edid = new byte[128];
        // Second descriptor (offset 72): tag 0xFC, name padded with LF and spaces.
        byte[] descriptor = [0, 0, 0, 0xFC, 0, .. "KA242Y E\n   "u8];
        descriptor.CopyTo(edid, 72);

        Assert.Equal("KA242Y E", MonitorIdentity.NameFromEdid(edid));
    }

    [Fact]
    public void Edid_without_a_name_descriptor_or_truncated_has_no_name()
    {
        Assert.Null(MonitorIdentity.NameFromEdid(new byte[128]));
        Assert.Null(MonitorIdentity.NameFromEdid(new byte[60]));
    }

    [Fact]
    public void Device_interface_path_splits_into_hardware_and_instance_ids()
    {
        var parsed = MonitorIdentity.ParseDeviceInterface(
            @"\\?\DISPLAY#ACR0B70#5&27c96da4&0&UID28928#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}");

        Assert.Equal(("ACR0B70", "5&27c96da4&0&UID28928"), parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\.\DISPLAY1")]
    public void Unrecognised_device_paths_parse_to_nothing(string? path)
    {
        Assert.Null(MonitorIdentity.ParseDeviceInterface(path));
    }

    [Fact]
    public void Window_covering_the_monitor_is_full_screen()
    {
        var monitor = new PixelRect(1920, 0, 3840, 1080);

        Assert.True(FullscreenRule.Covers(new PixelRect(1920, 0, 3840, 1080), monitor, "UnityWndClass"));
        Assert.True(FullscreenRule.Covers(new PixelRect(1912, -8, 3848, 1088), monitor, "Chrome_WidgetWin_1"));
    }

    [Fact]
    public void Maximised_window_above_the_taskbar_is_not_full_screen()
    {
        Assert.False(FullscreenRule.Covers(
            new PixelRect(0, 0, 1920, 1032), new PixelRect(0, 0, 1920, 1080), "Chrome_WidgetWin_1"));
    }

    [Fact]
    public void Desktop_is_not_full_screen_content()
    {
        var monitor = new PixelRect(0, 0, 1920, 1080);

        Assert.False(FullscreenRule.Covers(monitor, monitor, "Progman"));
        Assert.False(FullscreenRule.Covers(monitor, monitor, "WorkerW"));
    }

    [Fact]
    public void Settled_value_changes_only_after_holding_for_the_settle_time()
    {
        var time = new FakeTimeProvider();
        var value = new SettledValue<string?>(null, TimeSpan.FromSeconds(3), time);

        Assert.False(value.Observe("ACR0B70"));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(value.Observe("ACR0B70"));
        Assert.Null(value.Current);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(value.Observe("ACR0B70"));
        Assert.Equal("ACR0B70", value.Current);
    }

    [Fact]
    public void Settled_value_ignores_a_brief_change_that_reverts()
    {
        var time = new FakeTimeProvider();
        var value = new SettledValue<string?>("ACR0B70", TimeSpan.FromSeconds(3), time);

        Assert.False(value.Observe(null));
        time.Advance(TimeSpan.FromSeconds(2));
        Assert.False(value.Observe("ACR0B70"));
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.False(value.Observe("ACR0B70"));

        Assert.Equal("ACR0B70", value.Current);
    }
}
