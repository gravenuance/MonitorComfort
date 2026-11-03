using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using MonitorComfort.App.ViewModels;
using MonitorComfort.Core.Settings;

namespace MonitorComfort.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly Dictionary<SettingsPage, Control> _pages;
    private SettingsPage _shown = SettingsPage.General;

    public SettingsWindow()
    {
        InitializeComponent();
        _pages = new()
        {
            [SettingsPage.General] = GeneralPage,
            [SettingsPage.Profiles] = ProfilesPage,
            [SettingsPage.Monitors] = MonitorsPage,
            [SettingsPage.Schedule] = SchedulePage,
            [SettingsPage.RoomLights] = RoomLightsPage,
            [SettingsPage.Camera] = CameraPage,
            [SettingsPage.Fullscreen] = FullscreenPage,
        };
        foreach (var page in Enum.GetValues<SettingsPage>())
        {
            if (!_pages.ContainsKey(page))
            {
                throw new InvalidOperationException($"The settings window has no panel for the {page} page");
            }
        }
    }

    /// <summary>Shows the app mark in both the taskbar and the drawn title bar.</summary>
    public void SetAppIcon(Bitmap mark)
    {
        Icon = new WindowIcon(mark);
        TitleIcon.Source = mark;
    }

    /// <summary>Sizes the window to the tallest page; call once the window is open and laid out. The caller keeps it within the screen.</summary>
    public void FitHeightToPages()
    {
        UpdateLayout();
        var width = PagesHost.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        var tallest = 0.0;
        foreach (var page in _pages.Values)
        {
            page.IsVisible = true;
            page.Measure(new Size(width, double.PositiveInfinity));
            tallest = Math.Max(tallest, page.DesiredSize.Height);
        }

        ShowPage(_shown);
        var padding = PageScroll.Padding;
        Height = TitleBar.Bounds.Height + StatusBar.Bounds.Height + padding.Top + tallest + padding.Bottom;
    }

    private void ShowPage(SettingsPage shown)
    {
        _shown = shown;
        foreach (var (page, panel) in _pages)
        {
            panel.IsVisible = page == shown;
        }
    }

    private void OnPageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems is [PageItem item])
        {
            ShowPage(item.Page);
            PageScroll.ScrollToHome();
        }
    }

    // BorderOnly leaves no system caption, so the title bar starts the move itself.
    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }
}
