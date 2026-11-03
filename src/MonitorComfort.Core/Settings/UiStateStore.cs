namespace MonitorComfort.Core.Settings;

/// <summary>
/// The remembered <see cref="UiState"/>, written at once when it changes (a page switch is rare and the file tiny).
/// Used from the UI thread only. Problems go to <c>warn</c>; they never stop the app.
/// </summary>
public sealed class UiStateStore
{
    private readonly VersionedJsonFile<UiState> _file;
    private readonly Action<string> _warn;
    private UiState _state;

    public UiStateStore(VersionedJsonFile<UiState> file, Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(warn);
        _file = file;
        _warn = warn;

        var loaded = file.Load();
        if (loaded.Problem is not null)
        {
            warn(loaded.Problem);
        }

        _state = loaded.Value;
        if (!Enum.IsDefined(_state.SettingsPage))
        {
            warn($"Unknown settings page {(int)_state.SettingsPage}; opening General");
            _state = _state with { SettingsPage = SettingsPage.General };
        }
    }

    public SettingsPage SettingsPage => _state.SettingsPage;

    public void RememberSettingsPage(SettingsPage page)
    {
        if (!Enum.IsDefined(page))
        {
            throw new ArgumentOutOfRangeException(nameof(page), page, "Not a settings page");
        }

        if (page == _state.SettingsPage)
        {
            return;
        }

        _state = _state with { SettingsPage = page };
        try
        {
            _file.Save(_state);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _warn($"Could not save {_file.Path}: {ex.Message}");
        }
    }
}
