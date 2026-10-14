# 0002: Use the webcam as an opt-in light meter, reading its automatic exposure
**Status:** Accepted; calibration mapping superseded by 0003, measurement by 0004 · **Date:** 2026-10-07

## Context
The room-light model (ADR 0001's app, lights on from sunset) only knows what the user typed in.
The PC has a Logitech C920 webcam and no ambient light sensor. A webcam's automatic exposure
makes a dark room and a bright room look alike, so the brightness of an ordinary snapshot says
little about the light. A probe of the C920 over Media Foundation found:
- It reports the exposure (whole powers of two, 2^-11 to 2^-2 s) and gain (0–255) that its
  automatic exposure picks, and the white balance its automatic white balance picks.
- Its response isn't linear. Each exposure stop multiplies picture brightness by about 2.3×
  (a slope of about 1.2), and gain steps are uneven.
- Settling takes about 2 s. Settle, lock what was chosen and average a few frames: repeated
  readings agree to about 0.1 stop.

## Options
- **A: read back automatic exposure** and correct the picture brightness for it. This works in
  any light the camera can expose for, but needs the camera's response.
- **B: fixed manual exposure**, with a second exposure when the picture clips or goes black.
  No response curve is needed, but there's no single exposure for both a dim room and daylight,
  and gain still needs a model.
- **Pixel colour for the light's colour**: meaningless under automatic white balance, while the
  camera's own white-balance choice already estimates the light's colour.

## Decision
Option A, chosen by the user. Each reading lets automatic exposure and white balance settle,
locks the exposure and gain the camera chose, averages 8 frames, and restores the camera's own
settings, which other apps share. Light is expressed in stops:
`log2(brightness) / slope − exposure − gainStops(gain)`. The camera's response (the slope and the
gain curve) is measured by sweeping exposure and gain the first time the user calibrates, so no
camera model is hard-coded. One calibration point (the light now, and the brightness that feels
right in it) anchors the mapping. Brightness then moves 10 points per stop, within 5–100%. The
light's colour, taken from the white balance, caps warmth once the room is within 3 stops of the
calibration light. When the room is darker than that, the camera mostly sees the screens' own glow.

## Consequences
- Brightness follows real changes: clouds, lamps, blinds.
- The camera light comes on for about 3 s per reading. Readings are skipped while paused, while
  the PC has been idle for 15 minutes, and when another app holds the camera; after 30 minutes
  without a reading the room-light model takes over again.
- The camera sees the user and the room, not the desk, so the scale is relative; a white wall
  or dark clothing shifts it, and calibration absorbs that.
- No image is stored or sent; only the two numbers are logged.
- Revisit the 10-points-per-stop slope once there is real-use feedback, and if a camera
  without exposure and gain controls has to be supported.
