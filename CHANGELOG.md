# Changelog

## Unreleased

### Added
- Tray app that adjusts monitor brightness and colour warmth over DDC/CI from sun elevation, bedtime and modelled room lights.
- Dims the other monitors while something is full-screen.
- Settings window: location (typed or from Windows Location), bedtime, day/evening/night profiles, room lights, full-screen dimming, per-monitor offsets, start with Windows.
- Saves each monitor's own settings before the first change; tray action and `--restore` put them back.
- Stockholm is the default location.

### Fixed
- "Use my location" closed the app when the Windows location service is disabled; it now shows a message, and no UI error can close the app.
- First cycle compared colour gains from the previous colour preset, leaving a stale channel until the next minute.
