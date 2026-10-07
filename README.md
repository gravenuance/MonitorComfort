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
| Full-screen | After 3 s settled, the other monitors drop to a share of their scheduled brightness (default 20%). |
| Per monitor | A brightness offset evens out panels; any monitor can be left alone. |

Defaults: day 60% / 6500K, evening 35% / 4500K, night 15% / 3400K, night 22:30–07:00,
neutral 4000K room lights on from sunset to sunrise.

The app only writes a value when it moves by 2 or more. Some monitors store every DDC/CI
write in their own memory, which wears with use.

Before it changes a monitor for the first time, the app saves that monitor's settings.
**Restore original monitor settings** in the tray menu puts them back and pauses the app.

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
