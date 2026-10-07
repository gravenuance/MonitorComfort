using MonitorComfort.Core.Colour;

namespace MonitorComfort.Core.Monitors;

/// <summary>MCCS VCP feature codes this app reads or writes.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design", "CA1008", Justification = "Values are fixed MCCS wire codes; 0x00 is not a valid feature.")]
public enum VcpCode
{
    Brightness = 0x10,
    ColourPreset = 0x14,
    RedGain = 0x16,
    GreenGain = 0x18,
    BlueGain = 0x1A,
}

public readonly record struct VcpWrite(VcpCode Code, int Value);

/// <summary>A monitor's brightness and colour gains as last read or written.</summary>
public sealed record MonitorLevels(int Brightness, RgbGains Gains);

/// <summary>
/// Decides which values to send. Small changes are held back because some monitors commit
/// every DDC/CI write to their own non-volatile memory, which wears with use.
/// </summary>
public static class WritePolicy
{
    public const int MinimumChange = 2;

    /// <summary>MCCS colour preset value for the user-adjustable gains mode.</summary>
    public const int UserColourPreset = 0x0B;

    public static IReadOnlyList<VcpWrite> Writes(MonitorLevels? current, MonitorLevels target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var writes = new List<VcpWrite>(4);
        Add(writes, VcpCode.Brightness, current?.Brightness, target.Brightness);
        Add(writes, VcpCode.RedGain, current?.Gains.Red, target.Gains.Red);
        Add(writes, VcpCode.GreenGain, current?.Gains.Green, target.Gains.Green);
        Add(writes, VcpCode.BlueGain, current?.Gains.Blue, target.Gains.Blue);
        return writes;
    }

    private static void Add(List<VcpWrite> writes, VcpCode code, int? current, int target)
    {
        if (current is null || Math.Abs(target - current.Value) >= MinimumChange)
        {
            writes.Add(new VcpWrite(code, target));
        }
    }
}
