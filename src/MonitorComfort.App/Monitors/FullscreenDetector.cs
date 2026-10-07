using MonitorComfort.App.Native;
using MonitorComfort.Core.Context;

namespace MonitorComfort.App.Monitors;

/// <summary>Finds the monitor, if any, whose whole area the foreground window covers.</summary>
internal sealed class FullscreenDetector
{
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    /// <summary>The GDI device name (\\.\DISPLAYn) of the full-screen monitor, or null.</summary>
    public unsafe string? FullscreenDeviceName()
    {
        var window = NativeMethods.GetForegroundWindow();
        if (window == 0 || NativeMethods.IsIconic(window))
        {
            return null;
        }

        uint processId;
        if (NativeMethods.GetWindowThreadProcessId(window, &processId) == 0 || processId == _ownProcessId)
        {
            return null;
        }

        var hmonitor = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNull);
        var info = new MonitorInfoEx { Size = sizeof(MonitorInfoEx) };
        Rect rect;
        if (hmonitor == 0 || !NativeMethods.GetMonitorInfo(hmonitor, &info) || !NativeMethods.GetWindowRect(window, &rect))
        {
            return null;
        }

        const int classNameCapacity = 256;
        var className = stackalloc char[classNameCapacity];
        var length = NativeMethods.GetClassName(window, className, classNameCapacity);

        var covers = FullscreenRule.Covers(
            new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom),
            new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            new string(className, 0, Math.Max(length, 0)));
        return covers ? new string(info.Device) : null;
    }
}
