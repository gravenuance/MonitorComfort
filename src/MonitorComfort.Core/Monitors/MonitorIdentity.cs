using System.Text;

namespace MonitorComfort.Core.Monitors;

/// <summary>Reads a monitor's model name and PnP hardware ID from what Windows exposes.</summary>
public static class MonitorIdentity
{
    private const int DescriptorStart = 54;
    private const int DescriptorLength = 18;
    private const int DescriptorCount = 4;
    private const byte MonitorNameTag = 0xFC;

    /// <summary>The model name from an EDID's monitor-name descriptor, or null if it has none.</summary>
    public static string? NameFromEdid(ReadOnlySpan<byte> edid)
    {
        for (var i = 0; i < DescriptorCount; i++)
        {
            var offset = DescriptorStart + (i * DescriptorLength);
            if (edid.Length < offset + DescriptorLength)
            {
                return null;
            }

            var descriptor = edid.Slice(offset, DescriptorLength);
            if (descriptor[0] != 0 || descriptor[1] != 0 || descriptor[2] != 0 || descriptor[3] != MonitorNameTag)
            {
                continue;
            }

            var text = descriptor[5..];
            var end = text.IndexOf((byte)0x0A);
            var name = Encoding.ASCII.GetString(end >= 0 ? text[..end] : text).Trim();
            return name.Length > 0 ? name : null;
        }

        return null;
    }

    /// <summary>
    /// Splits a monitor device interface path such as
    /// <c>\\?\DISPLAY#ACR0B70#5&amp;27c96da4&amp;0&amp;UID28928#{guid}</c> into its hardware and instance IDs.
    /// </summary>
    public static (string HardwareId, string InstanceId)? ParseDeviceInterface(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        var parts = path.Split('#');
        return parts.Length >= 3 && parts[1].Length > 0 && parts[2].Length > 0 ? (parts[1], parts[2]) : null;
    }
}
