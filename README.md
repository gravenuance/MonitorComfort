# MonitorComfort

A Windows tray app that adjusts external monitors over DDC/CI to reduce eye strain. It sets
brightness and colour warmth from the sun's position at your location, your bedtime, and
the light in your room. When a game or video goes full-screen on one monitor, it dims the
others.

It changes the monitors' own settings, not the graphics card's colour ramp. That keeps the
full colour range and works alongside any GPU. Developed against an Acer KA242Y and a
Samsung S24B350H; any monitor with DDC/CI enabled in its menu should work.

## How it decides

| Input | Effect |
|---|---|
| Sun elevation | Blends from the **day** profile to **evening** across civil twilight (sun from +6° to −6°). |
| Bedtime | Blends from **evening** to **night** over 45 minutes after *night starts*, and back after *night ends*. |
| Room lights | While modelled lights are on (from sunset plus an offset, until sunrise or a set time), brightness stays at or above a floor for the light level, and warmth stays within 500K of the light's colour. |
| Camera light meter (opt-in) | Every 10 minutes, the webcam's automatic exposure is read back to measure the room light. Brightness follows the line through your bright-room and dim-room calibrations, and warmth stays near the light's colour. This replaces the room-light model while readings are fresh (30 minutes). |
| Full-screen | After 3 s settled, the other monitors drop to a share of their scheduled brightness (default 20%). |
| Per monitor | A brightness offset evens out panels; any monitor can be left alone. |
| Display changes | Within a second of a monitor being connected, removed, swapped, woken or rearranged, the app checks what each monitor supports over DDC/CI and adjusts it. A monitor that doesn't answer yet is retried after about 2, 5, 10 and 20 s, then every minute. The settings window shows each monitor as *Brightness and colour*, *Brightness only*, or not answering DDC/CI. |

Defaults: day 60% / 6500K, evening 35% / 4500K, night 15% / 3400K, night 22:30–07:00,
neutral 4000K room lights on from sunset to sunrise.

The app only writes a value when it moves by 2 or more. Some monitors store every DDC/CI
write in their own memory, which wears with use.

Before it changes a monitor for the first time, the app saves that monitor's settings.
**Restore original monitor settings** in the tray menu puts them back and pauses the app.

### Camera light meter

Switch on **Camera light meter** in settings, then calibrate two levels:
1. **Bright room:** with the room lit as usual, drag **Comfortable now** until the screens feel
   right (they preview as you drag), then press **Save for bright room**. Keep the light steady
   for about 20 seconds. The first time, this also measures how the camera responds to exposure
   and gain.
2. **Dim room:** turn the lights down, set **Comfortable now** again, and press **Save for dim
   room**.

Brightness then follows the straight line through your two choices, measured in stops of light,
and continues past them, within 5–100%. With only one level saved, it changes 10 points per stop
around that level. Either level can be redone at any time.

The camera light comes on for about 3 seconds per reading. No image is saved or sent; only the
light level and colour are logged. Readings are skipped while the app is paused, after 15
minutes without keyboard or mouse input, and while another app (such as a video call) is using
the camera. `MonitorComfort.exe --measure-light` takes one reading and writes it to the log.
In a dark room the camera mostly sees light from the screens, so their brightness feeds back
into the reading. The adjustment settles rather than running away, but it may settle a little
off the calibrated level; calibrating with the room lit as usual keeps this small.
See `docs/adr/0002-webcam-as-light-meter.md` for how it works.

## Build and test

Requires the .NET 10 SDK (pinned in `global.json`).

```powershell
dotnet build MonitorComfort.slnx -c Release
dotnet run --project tests/MonitorComfort.Tests -c Release
```

## Run

```powershell
dotnet run --project src/MonitorComfort.App -c Release
```

On first start the settings window opens with Stockholm as the location; change it there if
needed, then close the window. The app keeps running in the tray. (**Use my location** needs
the Windows location service, which some managed PCs disable.)

## Install

```powershell
dotnet publish src/MonitorComfort.App -c Release -o publish
```

Copy `publish/MonitorComfort.exe` anywhere permanent and run it. **Start with Windows** in
settings registers that copy to start at sign-in. The executable is unsigned, so Windows
SmartScreen may warn on first launch. Check its SHA-256 (`Get-FileHash`) against the one
you published.

## Files

| What | Where |
|---|---|
| Settings | `%APPDATA%\MonitorComfort\settings.json` |
| Monitors' original settings | `%APPDATA%\MonitorComfort\original-monitor-settings.json` |
| Log (10 MB × 5) | `%LOCALAPPDATA%\MonitorComfort\logs\monitorcomfort.log` |

Set `MONITORCOMFORT_LOG_LEVEL=Debug` to log every value written.

## Licence

MIT; see `LICENSE`.
