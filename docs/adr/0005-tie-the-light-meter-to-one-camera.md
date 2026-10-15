# 0005: Tie the light meter to one camera, and meter with whatever controls it has
**Status:** Accepted · **Date:** 2026-10-15

## Context
Up to 0.4.0 the light meter opened the first video capture device Windows listed and assumed it
behaved like the C920: manual exposure and gain on a step of 1, and a 640×480 mode. Publishing the
app means other cameras. Four cases broke:
- A virtual camera (OBS, NVIDIA Broadcast) or a second webcam listed first was opened instead.
  Virtual cameras have no exposure control, so the meter failed even with a usable camera present.
- The camera response and calibration were stored without saying which camera they came from. After
  swapping cameras, or a change in the order Windows lists them, readings from one sensor were put on
  another's scale. Brightness would be wrong with no error shown.
- Exposure and gain were stepped by 1 and by a quarter of the range. A driver with a coarser step
  rejects those values, and the failure showed as "Camera busy or unavailable".
- A camera without a 640×480 mode, or without a manual gain control, couldn't be used at all.

## Options
- **Keep the first device and document the C920.** No work, but the most likely setups (a laptop
  camera beside a webcam, a virtual camera from streaming software) fail or measure wrong.
- **Store one calibration per camera.** Switching back and forth keeps both, but it needs a new
  settings shape and migration for a case that is rare: most people meter with one camera.
- **Store which camera the one calibration belongs to; choosing another clears it.** A small
  additive setting, and a reading can never use a calibration made with another camera.

## Decision
The third option. `CameraLightSettings.CameraId` holds the camera's symbolic link, which stays the
same across restarts. A webcam without a serial number gets a new id on another USB port, so when the
stored id isn't connected, the one connected camera of the same model (the same vendor and product
part of the id) stands in, and its id is stored. With none or several such cameras, the reading fails
with "The calibrated camera isn't connected." rather than falling back to another camera. Without an
id, the meter uses the first camera that has a manual exposure control. A camera lacking the controls
is skipped, but any other failure, such as the camera being in use, is reported and not skipped, so a
busy webcam never silently hands over to a different one. The first calibration records the camera it
used. A calibration from before this change is assigned to the camera its next reading comes from:
the first that can meter, which is the camera those versions must have calibrated with. It carries
over without redoing.

Controls are read from the driver: exposure and gain move on the step grid the driver reports. A camera
with no gain control meters on exposure alone, with an empty gain curve. A camera whose gain only sets
itself is refused, because that gain would change under a locked reading. The output frame size is
640×480 when offered, as before, otherwise the smallest the camera offers.

## Consequences
- On a C920 the code path is the same as 0.4.0: the same metering gain (63), the same sweep and probe
  gains, the same frame size. A hardware check gave 3.09 stops with 0.4.0 and 3.09 with 0.5.0 in the
  same light, both at 2^-7 s and gain 63.
- Choosing another camera in settings clears the calibration. Revisit per-camera storage if people
  switch cameras often.
- Exposure is still assumed to be in log2 seconds, as UVC drivers report it. A driver using other
  units gives a response fit outside the plausible gamma range, so calibration fails with "Couldn't
  measure the camera" rather than calibrating wrongly.
- Only the C920 has been tested on hardware. Other cameras rely on the unit tests of the choice,
  step and frame-size rules.
