using MonitorComfort.App.Native;
using MonitorComfort.Core.Context;

namespace MonitorComfort.App.Monitors;

/// <summary>A foreground window covering a whole monitor; the details are for the log.</summary>
internal sealed record FullscreenWindow(string DeviceName, string WindowClass, string ProcessName, PixelRect Bounds);

/// <summary>Finds the monitor, if any, whose whole area the foreground window covers.</summary>
internal sealed class FullscreenDetector
{
    private readonly uint _ownProcessId = (uint)Environment.ProcessId;

    public unsafe FullscreenWindow? Detect()
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

        var bounds = new PixelRect(rect.Left, rect.Top, rect.Right, rect.Bottom);
        var windowClass = new string(className, 0, Math.Max(length, 0));
        int cloaked;
        var candidate = new FullscreenCandidate(
            bounds,
            new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom),
            windowClass,
            NativeMethods.IsWindowVisible(window),
            NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DwmCloaked, &cloaked, sizeof(int)) == 0 && cloaked != 0,
            (int)NativeMethods.GetWindowLongPtr(window, NativeMethods.ExtendedStyleIndex),
            (info.Flags & NativeMethods.MonitorInfoPrimary) != 0,
            ShellReportsFullscreen());
        return FullscreenRule.IsFullscreen(candidate)
            ? new FullscreenWindow(new string(info.Device), windowClass, ProcessName(processId), bounds)
            : null;
    }

    // QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE.
    private static unsafe bool ShellReportsFullscreen()
    {
        int state;
        return NativeMethods.SHQueryUserNotificationState(&state) == 0 && state is 2 or 3 or 4;
    }

    private static string ProcessName(uint processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return $"pid {processId}";
        }
    }
}
