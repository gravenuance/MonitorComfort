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

/// <summary>WNDCLASSEXW.</summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct WindowClass
{
    public uint Size;
    public uint Style;
    public delegate* unmanaged<nint, uint, nuint, nint, nint> WindowProc;
    public int ClassExtra;
    public int WindowExtra;
    public nint Instance;
    public nint Icon;
    public nint Cursor;
    public nint Background;
    public char* MenuName;
    public char* ClassName;
    public nint SmallIcon;
}

/// <summary>MSG.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct WindowMessage
{
    public nint Window;
    public uint Message;
    public nuint WParam;
    public nint LParam;
    public uint Time;
    public int X;
    public int Y;
}

/// <summary>POWERBROADCAST_SETTING, for a setting whose data is one DWORD.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PowerBroadcastSetting
{
    public Guid PowerSetting;
    public uint DataLength;
    public uint Data;
}

[System.Security.SuppressUnmanagedCodeSecurity]
internal static unsafe partial class NativeMethods
{
    public const uint WmClose = 0x0010;
    public const uint WmDestroy = 0x0002;
    public const uint WmDisplayChange = 0x007E;
    public const uint WmPowerBroadcast = 0x0218;
    public const uint WmDeviceChange = 0x0219;
    public const int DbtDevNodesChanged = 0x0007;
    public const int PbtPowerSettingChange = 0x8013;

    [LibraryImport("kernel32.dll", EntryPoint = "GetModuleHandleW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint GetModuleHandle(string? moduleName);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClassExW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial ushort RegisterClassEx(WindowClass* windowClass);

    [LibraryImport("user32.dll", EntryPoint = "UnregisterClassW", StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClass(string className, nint instance);

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [LibraryImport("user32.dll", EntryPoint = "DefWindowProcW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint DefWindowProc(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll", EntryPoint = "GetMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int GetMessage(WindowMessage* message, nint window, uint filterMin, uint filterMax);

    [LibraryImport("user32.dll", EntryPoint = "DispatchMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint DispatchMessage(WindowMessage* message);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint window, uint message, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial void PostQuitMessage(int exitCode);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint window);

    // Flags 0 delivers WM_POWERBROADCAST to a window.
    [LibraryImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial nint RegisterPowerSettingNotification(nint recipient, Guid* powerSetting, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterPowerSettingNotification(nint handle);

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

    public struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetLastInputInfo(LastInputInfo* info);

    // Media Foundation. Interface pointers are passed as raw IUnknown pointers and wrapped by the caller.
    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFStartup(uint version, uint flags);

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFCreateAttributes(nint* attributes, uint initialSize);

    [LibraryImport("mfplat.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFCreateMediaType(nint* mediaType);

    [LibraryImport("mf.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFEnumDeviceSources(nint attributes, nint* activates, uint* count);

    [LibraryImport("mfreadwrite.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial int MFCreateSourceReaderFromMediaSource(nint source, nint attributes, nint* reader);

    [LibraryImport("ole32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial void CoTaskMemFree(nint memory);

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
