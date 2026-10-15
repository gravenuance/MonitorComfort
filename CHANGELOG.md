# Changelog

## Unreleased

## 0.5.0 — 2026-10-15

### Added
- Cameras other than the C920: the light meter uses the first camera with manual exposure, skipping virtual cameras, and keeps exposure and gain on the steps the camera reports. It reads at a frame size the camera offers, and works without a manual gain control.
- With more than one camera connected, a **Camera** list in settings picks the light meter's camera. Choosing a different camera clears the calibration, to be redone with that camera.
- Releases are built on GitHub from the tag, with a SHA-256 checksum and build provenance.

### Changed
- A calibration belongs to the camera it was made with, and readings never come from another camera; the same webcam on another USB port still counts as that camera. An existing calibration is assigned to the camera its next reading comes from, so it carries over without redoing.
- A camera whose exposure can't be set by hand now reports "This camera can't be used as a light meter" instead of "Camera busy or unavailable".

## 0.4.0 — 2026-10-14

### Fixed
- Brightness no longer swings 15-25 points every 10 minutes in an unchanged room. The C920 reports a stale gain while its automatic exposure runs, so readings were taken at whatever gain was left over, often a near-black picture at gain 0 that read 0.7 stops too dark. Readings now lock a fixed gain and step exposure until the picture is well exposed. Calibrations made before this give about 6 points less brightness until redone.

### Changed
- Automatic readings within 0.25 stops of the last one are ignored, changes up to 1 stop move half the way, and larger ones are followed at once. Measure now and calibration still act on the raw reading.

### Added
- `--probe-light` diagnostic: logs frame-by-frame picture brightness after locking what automatic exposure reported and across gain steps.

## 0.3.1 — 2026-10-08

### Fixed
- After sign-in or switching a monitor on, monitors now get the right settings without pressing Measure now: every value is rewritten for about 40 seconds after a display change, because a monitor still starting up can drop writes while reporting them as applied.

## 0.3.0 — 2026-10-08

### Added
- Reacts within a second to displays being connected, removed, swapped, woken or rearranged, and retries a new monitor quickly until it answers DDC/CI.
- Each monitor's row in the settings window shows what it supports: brightness and colour, brightness only, or no DDC/CI answer (its switch is then disabled).

### Changed
- A monitor that doesn't answer DDC/CI is left alone and logged once, instead of on every cycle.

## 0.2.0 — 2026-10-07

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
