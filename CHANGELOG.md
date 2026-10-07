# Changelog

## Unreleased

### Added
- Opt-in camera light meter: reads the webcam's automatic exposure and white balance every 10 minutes to set brightness and warmth from the measured room light. Calibrate a bright-room and a dim-room brightness, with live preview; brightness follows the line through the two. Falls back to the room-light model when there's no recent reading.
- `--measure-light` and `--calibrate-light <brightness> [--dim]` diagnostics, logged.

### Changed
- Settings schema 2: the camera calibration holds a bright and a dim point. A version 1 file is upgraded once on start, and kept as `settings.json.v1.bak`.

## 0.1.0 — 2026-10-07

### Added
- Tray app that adjusts monitor brightness and colour warmth over DDC/CI from sun elevation, bedtime and modelled room lights.
- Dims the other monitors while something is full-screen.
- Settings window: location (typed or from Windows Location), bedtime, day/evening/night profiles, room lights, full-screen dimming, per-monitor offsets, start with Windows.
- Saves each monitor's own settings before the first change; tray action and `--restore` put them back.
- Stockholm is the default location.

### Fixed
- "Use my location" closed the app when the Windows location service is disabled; it now shows a message, and no UI error can close the app.
- First cycle compared colour gains from the previous colour preset, leaving a stale channel until the next minute.
- Shell surfaces and overlays could count as full-screen; on the primary monitor Windows' own full-screen signal must now agree.
- Starting at sign-in: the settings window no longer opens on every start, a monitor that isn't answering yet is retried instead of losing colour control, and a monitor is only changed once its original settings were read completely.
