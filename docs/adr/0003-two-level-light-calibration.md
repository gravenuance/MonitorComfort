# 0003: Calibrate the light meter at a bright and a dim level
**Status:** Accepted · **Date:** 2026-10-07 · Supersedes the calibration mapping in 0002

## Context
ADR 0002 mapped one calibration point to brightness at a fixed 10 points per stop of light. In the
user's room, the bright-room calibration was 60% with the ceiling lights at 90%. Dimming those
lights to 30% and then 1% moved the camera reading by only −0.26 and −1.16 stops, so the screens
went only from 60% to 57% and 48%. The camera sees the user and the room, and once the ceiling
lights are low, the screens themselves light much of that. So the measured range is compressed
compared with the change in the lamps' light. How compressed depends on the room and the camera,
so no fixed rate fits.

## Options
- **A steeper fixed rate**: one number to tune, but it's wrong for any other room or setup.
- **A user-set rate**: precise, but asks for an abstract number nobody knows.
- **Two calibration points**: the user picks a comfortable brightness in a bright room and in a dim
  one, and the rate comes from those two choices, in the user's own room, with the screens' glow
  already included.

## Decision
Two points, chosen by the user (lights at 90% and at 1%). The calibration becomes
`{ bright: {stops, brightness}, dim: {stops, brightness} }`. Brightness follows the straight line
through them, in stops, and continues past them within 5–100%. A new point is rejected when the
two levels are less than 0.25 stop apart, or when the dim room asks for a higher brightness than
the bright one. With one point, the fixed rate from 0002 still applies around it. Re-measuring the
camera's response clears both points, because they would be on a different scale. Settings
schema 2 migrates a version 1 single point into the bright point.

## Consequences
- Each user's dimming curve comes from their own choices, including the screens' feedback.
- Calibration is two steps, and the dim one needs the room darkened.
- The line continues beyond the calibrated range; revisit if extreme light gives unwanted
  brightness, for example by flattening it past the points.
