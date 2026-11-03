using MonitorComfort.Core.Settings;

namespace MonitorComfort.App.ViewModels;

/// <summary>One entry in the settings window's navigation list. Glyphs are Segoe MDL2 Assets code points, which Segoe Fluent Icons also has.</summary>
public sealed record PageItem(SettingsPage Page, string Title, string Glyph)
{
    public static IReadOnlyList<PageItem> All { get; } =
    [
        new(SettingsPage.General, "General", "\uE713"),
        new(SettingsPage.Profiles, "Profiles", "\uE706"),
        new(SettingsPage.Monitors, "Monitors", "\uE7F4"),
        new(SettingsPage.Schedule, "Schedule", "\uE787"),
        new(SettingsPage.RoomLights, "Room lights", "\uEA80"),
        new(SettingsPage.Camera, "Camera", "\uE722"),
        new(SettingsPage.Fullscreen, "Full-screen", "\uE740"),
    ];

    public static PageItem For(SettingsPage page)
        => All.FirstOrDefault(item => item.Page == page)
            ?? throw new ArgumentOutOfRangeException(nameof(page), page, "No navigation entry for this page");
}
