namespace MonitorComfort.Core.Settings;

/// <summary>The settings window's pages, in navigation order; persisted by name.</summary>
public enum SettingsPage
{
    General,
    Profiles,
    Monitors,
    Schedule,
    RoomLights,
    Camera,
    Fullscreen,
}

/// <summary>
/// Window state that is not a setting: kept apart from <see cref="ComfortSettings"/> because saving a
/// setting re-applies the monitors, and switching a page must not.
/// </summary>
public sealed record UiState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public SettingsPage SettingsPage { get; init; } = SettingsPage.General;
}
