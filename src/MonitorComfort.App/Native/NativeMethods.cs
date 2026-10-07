using System.Runtime.InteropServices;

namespace MonitorComfort.App.Native;

[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct MonitorInfoEx
{
    public int Size;
    public Rect Monitor;
    public Rect Work;
    public uint Flags;
    public fixed char Device[32];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct DisplayDevice
{
    public int Size;
    public fixed char DeviceName[32];
    public fixed char DeviceString[128];
    public int StateFlags;
    public fixed char DeviceId[128];
    public fixed char DeviceKey[128];
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct PhysicalMonitor
{
    public nint Handle;
    public fixed char Description[128];
}

[System.Security.SuppressUnmanagedCodeSecurity]
internal static unsafe partial class NativeMethods
{
    public const uint MonitorDefaultToNull = 0;

    // Returns the monitor device interface path (\\?\DISPLAY#…) in DisplayDevice.DeviceId.
    public const uint EddGetDeviceInterfaceName = 1;

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayMonitors(
        nint hdc, nint clip, delegate* unmanaged<nint, nint, nint, nint, int> callback, nint data);

    [LibraryImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfo(nint monitor, MonitorInfoEx* info);

    [LibraryImport("user32.dll", EntryPoint = "EnumDisplayDevicesW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumDisplayDevices(string? device, uint index, DisplayDevice* displayDevice, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint MonitorFromWindow(nint window, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint window, Rect* rect);

    [LibraryImport("user32.dll", EntryPoint = "GetClassNameW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetClassName(nint window, char* className, int maxCount);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetWindowThreadProcessId(nint window, uint* processId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint window);

    public const uint MonitorInfoPrimary = 1;
    public const int ExtendedStyleIndex = -20;
    public const uint DwmCloaked = 14;

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint window);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int DwmGetWindowAttribute(nint window, uint attribute, int* value, int size);

    /// <summary>The shell's own "an app is full-screen" signal (as Focus Assist uses); primary monitor only.</summary>
    [LibraryImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int SHQueryUserNotificationState(int* state);

    [LibraryImport("dxva2.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNumberOfPhysicalMonitorsFromHMONITOR(nint monitor, uint* count);

    [LibraryImport("dxva2.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetPhysicalMonitorsFromHMONITOR(nint monitor, uint count, PhysicalMonitor* monitors);

    [LibraryImport("dxva2.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyPhysicalMonitor(nint physicalMonitor);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCapabilitiesStringLength(nint physicalMonitor, uint* length);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CapabilitiesRequestAndCapabilitiesReply(nint physicalMonitor, byte* buffer, uint length);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetVCPFeatureAndVCPFeatureReply(
        nint physicalMonitor, byte code, uint* codeType, uint* current, uint* maximum);

    [LibraryImport("dxva2.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetVCPFeature(nint physicalMonitor, byte code, uint value);
}
