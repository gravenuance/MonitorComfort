using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using MonitorComfort.App.Native;
using MonitorComfort.Core.Context;
using MonitorComfort.Core.Monitors;
using Serilog;

namespace MonitorComfort.App.Monitors;

/// <summary>A monitor's identity, known without opening it.</summary>
internal sealed record MonitorName(string Id, string Name);

/// <summary>Finds the attached monitors and their DDC/CI handles.</summary>
internal sealed class DdcMonitorService
{
    private readonly ILogger _log;

    // Capability strings take a second or more to fetch and never change for a model.
    private readonly ConcurrentDictionary<string, Capabilities> _capabilities = new(StringComparer.OrdinalIgnoreCase);

    // Reading the EDID from the registry is repeated for every monitor on every scan otherwise.
    private readonly ConcurrentDictionary<string, (string Id, string Name)> _identities = new(StringComparer.OrdinalIgnoreCase);

    private volatile IReadOnlyList<MonitorName>? _names;

    public DdcMonitorService(ILogger log) => _log = log.ForContext<DdcMonitorService>();

    /// <summary>Opens a handle to every monitor; the caller disposes each one.</summary>
    public IReadOnlyList<DdcMonitor> Enumerate()
    {
        var monitors = new List<DdcMonitor>();
        Scan(monitors);
        return monitors;
    }

    /// <summary>The monitors' ids and names as of the last scan, without opening any handle.</summary>
    public IReadOnlyList<MonitorName> Names() => _names ?? Scan(null);

    /// <summary>The display layout changed, so identities are read afresh on the next scan.</summary>
    public void ForgetNames()
    {
        _names = null;
        _identities.Clear();
    }

    /// <summary>True once the monitor's capabilities have been read; until then a classification is provisional.</summary>
    public bool HasCapabilities(string monitorId) => _capabilities.ContainsKey(monitorId);

    private List<MonitorName> Scan(List<DdcMonitor>? handles)
    {
        var names = new List<MonitorName>();
        foreach (var hmonitor in DisplayMonitorHandles())
        {
            AddPhysicalMonitors(hmonitor, names, handles);
        }

        _names = names;
        return names;
    }

    /// <summary>
    /// The connected monitors and their layout, read without any DDC/CI traffic so it's cheap to poll;
    /// it changes when a monitor is connected, removed, swapped, woken or rearranged.
    /// </summary>
    public static unsafe string Topology()
    {
        var topology = new StringBuilder();
        foreach (var hmonitor in DisplayMonitorHandles())
        {
            var info = new MonitorInfoEx { Size = sizeof(MonitorInfoEx) };
            if (!NativeMethods.GetMonitorInfo(hmonitor, &info))
            {
                continue;
            }

            var deviceName = new string(info.Device);
            var bounds = info.Monitor;
            topology.Append(CultureInfo.InvariantCulture, $"{deviceName} {bounds.Left},{bounds.Top},{bounds.Right},{bounds.Bottom}");
            var device = new DisplayDevice { Size = sizeof(DisplayDevice) };
            for (uint i = 0; NativeMethods.EnumDisplayDevices(deviceName, i, &device, NativeMethods.EddGetDeviceInterfaceName); i++)
            {
                topology.Append(' ').Append(new string(device.DeviceId));
            }

            topology.Append(';');
        }

        return topology.ToString();
    }

    /// <summary>Asks the monitor what it supports; slow (a second or more) for a monitor that doesn't answer.</summary>
    public MonitorSupport SupportOf(DdcMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (monitor.Read(VcpCode.Brightness) is null)
        {
            return MonitorSupport.None;
        }

        return MonitorSupportRules.Classify(CapabilitiesOf(monitor), answersBrightness: true);
    }

    /// <summary>A failed read isn't cached: monitors still waking up after sign-in often don't answer yet.</summary>
    public Capabilities CapabilitiesOf(DdcMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        if (_capabilities.TryGetValue(monitor.Id, out var cached))
        {
            return cached;
        }

        var text = monitor.ReadCapabilities();
        if (text is null)
        {
            // The controller reports what the monitor can do; this repeats every cycle for a monitor without DDC/CI.
            _log.Debug("Monitor {MonitorId} ({Name}) returned no DDC/CI capabilities; will retry", monitor.Id, monitor.Name);
            return Capabilities.None;
        }

        _log.Information("Monitor {MonitorId} ({Name}) capabilities {Capabilities}", monitor.Id, monitor.Name, text);
        return _capabilities.GetOrAdd(monitor.Id, Capabilities.Parse(text));
    }

    // Without handles only the identities are collected, so nothing needs releasing.
    private unsafe void AddPhysicalMonitors(nint hmonitor, List<MonitorName> names, List<DdcMonitor>? handles)
    {
        var info = new MonitorInfoEx { Size = sizeof(MonitorInfoEx) };
        if (!NativeMethods.GetMonitorInfo(hmonitor, &info))
        {
            return;
        }

        var deviceName = new string(info.Device);
        var bounds = new PixelRect(info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);

        uint count;
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hmonitor, &count) || count == 0)
        {
            return;
        }

        var physical = new PhysicalMonitor[count];
        if (handles is not null)
        {
            fixed (PhysicalMonitor* pointer = physical)
            {
                if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(hmonitor, count, pointer))
                {
                    return;
                }
            }
        }

        for (var i = 0; i < physical.Length; i++)
        {
            var (id, name) = Identify(deviceName, (uint)i);
            if (names.Exists(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                id = $"{id}-{names.Count}";
            }

            names.Add(new MonitorName(id, name));
            handles?.Add(new DdcMonitor(physical[i].Handle, id, name, deviceName, bounds));
        }
    }

    private unsafe (string Id, string Name) Identify(string deviceName, uint index)
    {
        var device = new DisplayDevice { Size = sizeof(DisplayDevice) };
        if (NativeMethods.EnumDisplayDevices(deviceName, index, &device, NativeMethods.EddGetDeviceInterfaceName))
        {
            var path = new string(device.DeviceId);
            if (_identities.TryGetValue(path, out var known))
            {
                return known;
            }

            if (MonitorIdentity.ParseDeviceInterface(path) is var (hardwareId, instanceId))
            {
                return _identities[path] = (hardwareId, ReadEdidName(hardwareId, instanceId) ?? hardwareId);
            }
        }

        _log.Warning("Could not identify the monitor on {DeviceName}; using the device name", deviceName);
        return (deviceName.TrimStart('\\', '.'), deviceName);
    }

    private string? ReadEdidName(string hardwareId, string instanceId)
    {
        var keyPath = $@"SYSTEM\CurrentControlSet\Enum\DISPLAY\{hardwareId}\{instanceId}\Device Parameters";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(keyPath);
            return key?.GetValue("EDID") is byte[] edid ? MonitorIdentity.NameFromEdid(edid) : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            _log.Warning(ex, "Could not read the EDID of {HardwareId}", hardwareId);
            return null;
        }
    }

    private static unsafe List<nint> DisplayMonitorHandles()
    {
        var handles = new List<nint>();
        var gc = GCHandle.Alloc(handles);
        try
        {
            NativeMethods.EnumDisplayMonitors(0, 0, &CollectMonitor, GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        return handles;
    }

    [UnmanagedCallersOnly]
    private static int CollectMonitor(nint hmonitor, nint hdc, nint rect, nint data)
    {
        // The handle wraps the live list allocated in DisplayMonitorHandles and is freed only after the enumeration.
        ((List<nint>)GCHandle.FromIntPtr(data).Target!).Add(hmonitor);
        return 1;
    }
}
