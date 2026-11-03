using MonitorComfort.Core.Settings;

namespace MonitorComfort.Tests;

public sealed class UiStateStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(AppContext.BaseDirectory, "test-data", Guid.NewGuid().ToString("N"));

    private readonly List<string> _warnings = [];

    public UiStateStoreTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string StatePath => Path.Combine(_directory, "ui-state.json");

    private UiStateStore Open() => new(SettingsFiles.UiStateFile(_directory), _warnings.Add);

    [Fact]
    public void A_first_run_opens_General_without_writing_a_file()
    {
        var store = Open();

        Assert.Equal(SettingsPage.General, store.SettingsPage);
        Assert.Empty(_warnings);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void The_remembered_page_comes_back_next_time()
    {
        Open().RememberSettingsPage(SettingsPage.RoomLights);

        Assert.Equal(SettingsPage.RoomLights, Open().SettingsPage);
        Assert.Contains("\"settingsPage\": \"RoomLights\"", File.ReadAllText(StatePath), StringComparison.Ordinal);
        Assert.False(File.Exists(StatePath + ".tmp"));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void Remembering_a_page_leaves_the_settings_file_alone()
    {
        Open().RememberSettingsPage(SettingsPage.Camera);

        Assert.False(File.Exists(Path.Combine(_directory, "settings.json")));
    }

    [Fact]
    public void Remembering_the_current_page_writes_nothing()
    {
        var store = Open();

        store.RememberSettingsPage(SettingsPage.General);

        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public void A_file_without_the_page_opens_General()
    {
        File.WriteAllText(StatePath, """{ "schemaVersion": 1 }""");

        var store = Open();

        Assert.Equal(SettingsPage.General, store.SettingsPage);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void An_unknown_page_name_opens_General_and_keeps_a_copy()
    {
        File.WriteAllText(StatePath, """{ "schemaVersion": 1, "settingsPage": "Gamepad" }""");

        var store = Open();

        Assert.Equal(SettingsPage.General, store.SettingsPage);
        Assert.Single(_warnings);
        Assert.True(File.Exists(StatePath + ".unreadable.bak"));
    }

    [Fact]
    public void An_out_of_range_page_number_opens_General()
    {
        File.WriteAllText(StatePath, """{ "schemaVersion": 1, "settingsPage": 99 }""");

        var store = Open();

        Assert.Equal(SettingsPage.General, store.SettingsPage);
        Assert.Single(_warnings);
    }

    [Fact]
    public void A_file_from_a_newer_version_opens_General_and_is_kept()
    {
        File.WriteAllText(StatePath, """{ "schemaVersion": 2, "settingsPage": "Camera" }""");

        var store = Open();

        Assert.Equal(SettingsPage.General, store.SettingsPage);
        Assert.Single(_warnings);
        Assert.True(File.Exists(StatePath + ".unreadable.bak"));
    }

    [Fact]
    public void A_page_that_is_not_in_the_enum_is_refused()
    {
        var store = Open();

        Assert.Throws<ArgumentOutOfRangeException>(() => store.RememberSettingsPage((SettingsPage)99));
    }

    [Fact]
    public void A_write_that_fails_is_reported_and_the_page_still_changes()
    {
        Directory.CreateDirectory(StatePath + ".tmp");
        var store = Open();

        store.RememberSettingsPage(SettingsPage.Monitors);

        Assert.Equal(SettingsPage.Monitors, store.SettingsPage);
        Assert.Single(_warnings);
    }
}
