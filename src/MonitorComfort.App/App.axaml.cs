using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace MonitorComfort.App;

public sealed partial class App : Application
{
    /// <summary>Set by <see cref="Program"/> before Avalonia starts.</summary>
    internal static AppHost? Host { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            && Host is { } host)
        {
#pragma warning disable CA2000 // Disposed by the Exit handler below, on the UI thread, while Avalonia still runs.
            var tray = new TrayIconController(desktop, host);
#pragma warning restore CA2000
            desktop.Exit += (_, _) => tray.Dispose();
            host.Start();

            if (host.Settings.Current.Location is null)
            {
                tray.ShowSettings();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
}
