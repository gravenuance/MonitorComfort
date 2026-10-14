# 0004: Measure at a locked metering gain, and steady automatic readings
**Status:** Accepted; supersedes the measurement step of 0002 · **Date:** 2026-10-14

## Context
With the room unchanged, readings flipped between two levels about 0.7 stops apart, moving
brightness 15–25 points every 10 minutes. The high and low readings lined up exactly with the gain
the camera reported: 227–255 or 0–23. A `--probe-light` run on the C920 showed two things:
- While automatic exposure runs, the camera reports a stale gain. It said gain 0 while delivering a
  picture 14 times brighter than gain 0 gives. Locking the reported values then measured a
  near-black picture (mean 0.006).
- At gain 0 the picture is 0.7 stops darker than the fitted gain curve says. Gains 63–255 agree
  with the curve within 0.2 stops. A new setting takes effect within one frame, so the 4 frames
  discarded after each change are enough.

Even a correct meter keeps 0.1–0.2 stops of noise, about 3–6 points at the user's calibration.

## Options
- **Refit the gain curve:** keeps 0002's method, but still trusts a gain readback that is wrong,
  and still measures dark pictures near the sensor's black level.
- **Lock a fixed metering gain and step exposure** until the picture is well exposed: doesn't
  depend on the readback, and stays where the response was measured. Costs a few extra
  half-second steps per reading.
- **Smooth the readings only:** hides the swing, but settles on a biased average of two wrong
  levels.

## Decision
Lock the metering gain (a quarter of the gain range, 63 on the C920, the gain the exposure sweep
uses). Start at the exposure automatic exposure reported, and step it one stop at a time until
the picture's mean is between 0.02 and 0.15. Raise gain a quarter-range at a time only once
exposure is at its longest. On top of that, automatic readings within 0.25 stops of the held one
are ignored, changes up to 1 stop move half the way, and larger changes are followed at once.
"Measure now" and calibration act on the raw reading.

## Consequences
- An unchanged room reads the same: three readings in a row gave −1.27 stops each.
- The gain curve now matters only in dark rooms (gain 126 and up), where it was measured to fit.
- Readings sit about 0.18 stops below the old high-gain ones, so calibrations made before 0.4.0
  give about 6 points less brightness until redone.
- A real change of 0.25–1 stop takes two or three readings (20–30 minutes) to be followed fully,
  and up to 0.25 stops of it can be left unfollowed.
- Revisit the deadband if the user finds the screens slow to follow the room lights.
