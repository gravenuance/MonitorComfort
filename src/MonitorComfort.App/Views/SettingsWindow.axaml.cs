using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace MonitorComfort.App.Views;

public sealed partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    /// <summary>Shows the app mark in both the taskbar and the drawn title bar.</summary>
    public void SetAppIcon(Bitmap mark)
    {
        Icon = new WindowIcon(mark);
        TitleIcon.Source = mark;
    }
}
