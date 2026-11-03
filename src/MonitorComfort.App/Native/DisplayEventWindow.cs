using System.Runtime.InteropServices;
using Serilog;

namespace MonitorComfort.App.Native;

/// <summary>
/// A hidden window on its own thread that hears about display changes, device changes and the
/// console display turning on or off. It is a top-level window, not message-only, because
/// Windows broadcasts WM_DISPLAYCHANGE and WM_DEVICECHANGE to top-level windows only.
/// Events are raised on that thread.
/// </summary>
internal sealed class DisplayEventWindow : IDisposable
{
    private const string ClassName = "MonitorComfort.DisplayEvents";
    private const uint ConsoleDisplayOff = 0;
    private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    // GUID_CONSOLE_DISPLAY_STATE: 0 off, 1 on, 2 dimmed.
    private static readonly Guid ConsoleDisplayState = new("6fe69556-704a-47a0-8f24-c28d936fda47");

    // The window procedure is static and runs on the window's thread, which is the only thread that sets this.
    [ThreadStatic]
    private static DisplayEventWindow? _current;

    private readonly ILogger _log;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private nint _window;
    private nint _powerNotification;
    private bool _consoleDisplayOn = true;
    private int _disposed;

    public DisplayEventWindow(ILogger log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log.ForContext<DisplayEventWindow>();
    }

    /// <summary>A display or device change that may have added, removed or rearranged monitors.</summary>
    public event EventHandler? TopologyMayHaveChanged;

    /// <summary>The console display turned off (false) or on (true).</summary>
    public event EventHandler<bool>? ConsoleDisplayChanged;

    /// <summary>Starts the thread and waits until the window exists; on failure it logs and stays silent.</summary>
    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Display events" };
        _thread.Start();
        if (!_ready.Wait(StartTimeout))
        {
            _log.Warning("The display event window did not start within {Timeout}", StartTimeout);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        if (_window != 0)
        {
            // The window is destroyed on its own thread, which ends the message loop.
            NativeMethods.PostMessage(_window, NativeMethods.WmClose, 0, 0);
        }

        if (_thread is { } thread && !thread.Join(StopTimeout))
        {
            _log.Warning("The display event thread did not stop within {Timeout}", StopTimeout);
        }

        _ready.Dispose();
    }

    [UnmanagedCallersOnly]
    private static nint WindowProc(nint window, uint message, nuint wParam, nint lParam)
    {
        try
        {
            if (_current is { } self && self.TryHandle(window, message, wParam, lParam, out var result))
            {
                return result;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An exception must not unwind into native code.
            _current?._log.Error(ex, "Handling window message {Message:X} failed", message);
        }

        return NativeMethods.DefWindowProc(window, message, wParam, lParam);
    }

    private void Run()
    {
        _current = this;
        var instance = NativeMethods.GetModuleHandle(null);
        var created = Initialise(instance);
        _ready.Set();
        if (created)
        {
            Pump();
            NativeMethods.UnregisterClass(ClassName, instance);
        }

        _current = null;
    }

    private unsafe bool Initialise(nint instance)
    {
        fixed (char* className = ClassName)
        {
            var windowClass = new WindowClass
            {
                Size = (uint)sizeof(WindowClass),
                WindowProc = &WindowProc,
                Instance = instance,
                ClassName = className,
            };
            if (NativeMethods.RegisterClassEx(&windowClass) == 0)
            {
                _log.Warning("Could not register the display event window class (Win32 error {Error})", Marshal.GetLastPInvokeError());
                return false;
            }
        }

        _window = NativeMethods.CreateWindowEx(0, ClassName, ClassName, 0, 0, 0, 0, 0, 0, 0, instance, 0);
        if (_window == 0)
        {
            _log.Warning("Could not create the display event window (Win32 error {Error})", Marshal.GetLastPInvokeError());
            NativeMethods.UnregisterClass(ClassName, instance);
            return false;
        }

        var setting = ConsoleDisplayState;
        _powerNotification = NativeMethods.RegisterPowerSettingNotification(_window, &setting, 0);
        if (_powerNotification == 0)
        {
            _log.Warning("Could not watch the console display state (Win32 error {Error})", Marshal.GetLastPInvokeError());
        }

        return true;
    }

    private static unsafe void Pump()
    {
        WindowMessage message;
        while (NativeMethods.GetMessage(&message, 0, 0, 0) > 0)
        {
            NativeMethods.DispatchMessage(&message);
        }
    }

    private unsafe bool TryHandle(nint window, uint message, nuint wParam, nint lParam, out nint result)
    {
        result = 0;
        switch (message)
        {
            case NativeMethods.WmDisplayChange:
                TopologyMayHaveChanged?.Invoke(this, EventArgs.Empty);
                return false;
            case NativeMethods.WmDeviceChange when wParam == NativeMethods.DbtDevNodesChanged:
                TopologyMayHaveChanged?.Invoke(this, EventArgs.Empty);
                return false;
            case NativeMethods.WmPowerBroadcast when wParam == NativeMethods.PbtPowerSettingChange:
                OnPowerSetting((PowerBroadcastSetting*)lParam);
                result = 1;
                return true;
            case NativeMethods.WmClose:
                NativeMethods.DestroyWindow(window);
                return true;
            case NativeMethods.WmDestroy:
                if (_powerNotification != 0)
                {
                    NativeMethods.UnregisterPowerSettingNotification(_powerNotification);
                    _powerNotification = 0;
                }

                NativeMethods.PostQuitMessage(0);
                return true;
            default:
                return false;
        }
    }

    private unsafe void OnPowerSetting(PowerBroadcastSetting* setting)
    {
        if (setting->PowerSetting != ConsoleDisplayState || setting->DataLength != sizeof(uint))
        {
            return;
        }

        var on = setting->Data != ConsoleDisplayOff;
        if (on == _consoleDisplayOn)
        {
            return;
        }

        _consoleDisplayOn = on;
        ConsoleDisplayChanged?.Invoke(this, on);
    }
}
