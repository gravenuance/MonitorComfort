using System.Runtime.InteropServices;
using MonitorComfort.App.Native;
using MonitorComfort.Core.Context;
using MonitorComfort.Core.Monitors;

namespace MonitorComfort.App.Monitors;

/// <summary>One physical monitor reachable over DDC/CI. Owns its handle; dispose promptly.</summary>
internal sealed class DdcMonitor : IDisposable
{
    private const int WriteAttempts = 3;

    // MCCS asks hosts to leave at least 50 ms between commands to the same monitor.
    private static readonly TimeSpan CommandSpacing = TimeSpan.FromMilliseconds(50);

    private nint _handle;

    public DdcMonitor(nint handle, string id, string name, string deviceName, PixelRect bounds)
    {
        _handle = handle;
        Id = id;
        Name = name;
        DeviceName = deviceName;
        Bounds = bounds;
    }

    /// <summary>PnP hardware ID, e.g. ACR0B70; the key for per-monitor settings.</summary>
    public string Id { get; }

    public string Name { get; }

    /// <summary>GDI device name, e.g. \\.\DISPLAY1, as reported for windows on this monitor.</summary>
    public string DeviceName { get; }

    public PixelRect Bounds { get; }

    public unsafe string? ReadCapabilities()
    {
        uint length;
        if (!NativeMethods.GetCapabilitiesStringLength(_handle, &length) || length == 0)
        {
            return null;
        }

        var buffer = new byte[length];
        fixed (byte* pointer = buffer)
        {
            if (!NativeMethods.CapabilitiesRequestAndCapabilitiesReply(_handle, pointer, length))
            {
                return null;
            }
        }

        var end = Array.IndexOf(buffer, (byte)0);
        return System.Text.Encoding.ASCII.GetString(buffer, 0, end >= 0 ? end : buffer.Length);
    }

    public unsafe int? Read(VcpCode code)
    {
        uint type, current, maximum;
        return NativeMethods.GetVCPFeatureAndVCPFeatureReply(_handle, (byte)code, &type, &current, &maximum)
            ? (int)current
            : null;
    }

    /// <summary>Writes one value, retrying a busy monitor; returns the Win32 error of the last failure.</summary>
    public async Task<int?> WriteAsync(VcpCode code, int value, CancellationToken cancellationToken)
    {
        var error = 0;
        for (var attempt = 0; attempt < WriteAttempts; attempt++)
        {
            if (attempt > 0)
            {
                await Task.Delay(CommandSpacing * (attempt + 1), cancellationToken);
            }

            if (NativeMethods.SetVCPFeature(_handle, (byte)code, (uint)value))
            {
                await Task.Delay(CommandSpacing, cancellationToken);
                return null;
            }

            error = Marshal.GetLastPInvokeError();
        }

        return error;
    }

    public void Dispose()
    {
        if (_handle != 0)
        {
            NativeMethods.DestroyPhysicalMonitor(_handle);
            _handle = 0;
        }
    }
}
