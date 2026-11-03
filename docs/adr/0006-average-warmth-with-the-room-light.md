# 0006: Average the screens' warmth with the room light after dark
**Status:** Accepted · **Date:** 2026-11-04

Replaces the colour rule in 0002 ("the light's colour caps warmth"); the rest of 0002 stands.

## Context
Up to 0.5.0 the room light's colour, from the camera or the room-light model, only set a limit:
the screens could be no more than 500K warmer than the room. A cool room made the screens cooler,
but a warm room never made them warmer than the schedule. The user wanted the screens to follow
the room both ways. Two constraints shaped the choice. Daylight hours in Sweden swing across the
year, so a rule tied to clock times would need retuning by season. At night the screens add to
the light the camera sees, so following its colour can feed back on itself.

## Options
- **Keep the limit only**: stable and simple, but a warm-lit room never warms the screens.
- **Average the schedule and the room, always**: follows the room, but a warm lamp at noon turns
  the screens orange, and a misjudged colour moves them a long way.
- **Average only between set hours**: avoids noon, but the hours suit only part of the year.
- **Average with a share that follows the sun, fades out for large gaps, and keeps the limit**:
  the sun's elevation already drives the day-to-evening blend, so it adapts to the season; the
  fade avoids jumps at the edge of what counts as a believable reading.

## Decision
The last option, chosen by the user. The room's share is half of `1 − daylight`, where daylight
is the existing smoothstep over sun elevation from −6° to +6°. It counts in full when schedule
and room differ by up to 1000K and fades to nothing at 1500K. The average is taken in mireds,
which follow how the eye sees colour shifts. The old limit (no more than 500K warmer than the
room) still applies on top, so a much cooler room behaves as before. While the light level
drifts, the camera's colour moves at most 300K per reading; a jump of a stop or more, such as
lights switching, is followed at once.

## Consequences
- Warm lamps at night warm the screens; daytime colour is unchanged.
- Far north, the room counts partly even at a winter noon, because the sun stays low.
- A cool room's effect is the same as before when it is more than about 1000K cooler than the
  schedule, because the limit then wins over the average.
- Readings inside the light deadband keep the held colour, as before, so a colour change with no
  change in light level isn't noticed.
- Revisit the 50% share and the 300K step after a few weeks of evenings, and whether the colour
  should update inside the light deadband.
