namespace MonitorComfort.Core.Monitors;

/// <summary>What this app can change on a monitor over DDC/CI.</summary>
public enum MonitorSupport
{
    /// <summary>Not asked yet: the app doesn't talk to monitors while paused or without a location.</summary>
    Unknown,

    /// <summary>The monitor doesn't answer a brightness read.</summary>
    None,
    BrightnessOnly,
    BrightnessAndColour,
}

public static class MonitorSupportRules
{
    /// <summary>
    /// A live brightness read decides whether the monitor answers at all, because capabilities are cached
    /// and outlive DDC/CI being switched off; some monitors also answer reads despite a broken capabilities string.
    /// </summary>
    public static MonitorSupport Classify(Capabilities capabilities, bool answersBrightness)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!answersBrightness)
        {
            return MonitorSupport.None;
        }

        return CanSetColour(capabilities) ? MonitorSupport.BrightnessAndColour : MonitorSupport.BrightnessOnly;
    }

    // Gains only take effect in the user colour preset, so the monitor must offer that preset too.
    private static bool CanSetColour(Capabilities capabilities)
        => capabilities.SupportsValue(VcpCode.ColourPreset, WritePolicy.UserColourPreset)
            && capabilities.Supports(VcpCode.RedGain)
            && capabilities.Supports(VcpCode.GreenGain)
            && capabilities.Supports(VcpCode.BlueGain);
}
