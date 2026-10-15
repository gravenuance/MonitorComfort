using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using MonitorComfort.App.Services;
using MonitorComfort.App.ViewModels;
using MonitorComfort.App.Views;
using Serilog;

namespace MonitorComfort.App;

/// <summary>
/// The app's always-present entry point. Closing the settings window only hides it, so the
/// built view is reused the next time it opens.
/// </summary>
internal sealed class TrayIconController : IDisposable
{
    private static readonly TimeSpan PauseLength = TimeSpan.FromHours(1);

    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly AppHost _host;
    private readonly ILogger _log;
    private readonly TrayIcon _trayIcon;
    private readonly RenderTargetBitmap _activeBitmap;
    private readonly RenderTargetBitmap _pausedBitmap;
    private readonly WindowIcon _activeIcon;
    private readonly WindowIcon _pausedIcon;
    private readonly NativeMenuItem _statusItem;
    private readonly NativeMenuItem _pauseItem;
    private readonly NativeMenuItem _resumeItem;

    private SettingsWindow? _window;
    private SettingsViewModel? _viewModel;
    private bool _quitting;

    public TrayIconController(IClassicDesktopStyleApplicationLifetime desktop, AppHost host)
    {
        _desktop = desktop;
        _host = host;
        _log = host.Log.ForContext<TrayIconController>();

        _activeBitmap = AppIcon.Render(32);
        _pausedBitmap = AppIcon.Render(32, paused: true);
        _activeIcon = new WindowIcon(_activeBitmap);
        _pausedIcon = new WindowIcon(_pausedBitmap);

        _statusItem = new NativeMenuItem("Starting…") { IsEnabled = false };
        _pauseItem = new NativeMenuItem("Pause for 1 hour");
        _pauseItem.Click += (_, _) => _host.Controller.PauseUntil(_host.Time.GetUtcNow() + PauseLength);
        _resumeItem = new NativeMenuItem("Resume") { IsEnabled = false };
        _resumeItem.Click += (_, _) => _host.Controller.Resume();

        var settings = new NativeMenuItem("Settings");
        settings.Click += (_, _) => ShowSettings();
        var restore = new NativeMenuItem("Restore original monitor settings");
        restore.Click += (_, _) => _host.Controller.RestoreOriginals();
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) =>
        {
            _quitting = true;
            _desktop.Shutdown();
        };

        _trayIcon = new TrayIcon
        {
            Icon = _activeIcon,
            ToolTipText = "MonitorComfort",
            IsVisible = true,
            Menu = new NativeMenu
            {
                Items =
                {
                    _statusItem, new NativeMenuItemSeparator(), settings, _pauseItem, _resumeItem, restore,
                    new NativeMenuItemSeparator(), quit,
                },
            },
        };
        _trayIcon.Clicked += (_, _) => ShowSettings();

        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _host.Controller.StatusChanged += OnStatusChanged;
    }

    public void ShowSettings()
    {
        if (_window is null)
        {
            _viewModel = new SettingsViewModel(_host.Settings, _host.Controller, _host.Autostart, _host.Ambient, _host.Time);
            _window = new SettingsWindow { DataContext = _viewModel, Icon = new WindowIcon(_activeBitmap) };
            _window.Closing += OnWindowClosing;
        }

        _viewModel?.RefreshCamerasCommand.Execute(null);
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        _host.Controller.StatusChanged -= OnStatusChanged;
        if (_window is not null)
        {
            _window.Closing -= OnWindowClosing;
        }

        _viewModel?.Dispose();
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
        _activeBitmap.Dispose();
        _pausedBitmap.Dispose();
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_quitting || sender is not Window window)
        {
            return;
        }

        e.Cancel = true;
        window.Hide();
        _viewModel?.EndPreview();
        _host.Settings.Flush();
    }

    private void OnStatusChanged(object? sender, ComfortStatus status) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            var text = SettingsViewModel.Describe(status);
            var paused = status.State == ControllerState.Paused;
            _statusItem.Header = text;
            _trayIcon.ToolTipText = $"MonitorComfort — {text}";
            _trayIcon.Icon = paused ? _pausedIcon : _activeIcon;
            _pauseItem.IsEnabled = !paused;
            _resumeItem.IsEnabled = paused;
        }
        catch (Exception ex)
        {
            _log.Warning(ex, "Updating the tray icon failed");
        }
    });
}
