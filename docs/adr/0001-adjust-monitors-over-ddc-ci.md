# 0001: Adjust the monitors over DDC/CI, not the GPU gamma ramp
**Status:** Accepted · **Date:** 2026-10-07

## Context
The goal is less eye strain from two external monitors (Acer KA242Y, Samsung S24B350H) on one
RTX 4070. Their brightness and warmth should follow the time of day, the room light and whether
something is full-screen. Both monitors answer DDC/CI: brightness (VCP 0x10), colour preset
(0x14, including the user preset 0x0B) and red/green/blue gains (0x16/0x18/0x1A). There is no
ambient light sensor, so room light has to be modelled.

## Options
- **GPU gamma ramp** (f.lux, Windows Night Light): instant and wear-free, but it can't lower the
  backlight. Dimming by gamma throws away colour levels and leaves black as bright as before.
- **DDC/CI monitor controls**: lowers the real backlight and warms the panel without losing
  colour levels. But each write takes about 50 ms and some monitors store it in their own
  memory, which wears.
- **Existing tools** (Twinkle Tray, ClickMonitorDDC): brightness schedules, but no room-light
  model and no full-screen rules.

## Decision
A dedicated tray app that drives DDC/CI. Warmth comes from scaling the user-preset gains by
blackbody white points (Redshift's table). Windows Night Light stays off so warming doesn't
happen twice. To limit wear, the app writes only when a value moves by 2 or more, re-plans once
a minute, and reacts to full-screen changes only after they hold for 3 seconds.

## Consequences
- Brightness changes reach the backlight, so night use is really dimmer, not just darker.
- Colour changes are as fine as each monitor's gain steps allow (0–100), coarser than a
  gamma ramp. Below about 2000K blue runs out, so warmth is limited to 2000K and up.
- A monitor's user colour preset is taken over. Its original settings are saved first and can
  be restored from the tray.
- Revisit if a monitor without DDC/CI is added: it would need a gamma-ramp fallback.
