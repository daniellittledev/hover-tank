# 2026-05-29 — Auto-steer: constant turn rate (no slow-down near target)

Branch: `claude/thirsty-turing-58334a`

## Changes

- Replaced the auto-steer PD controller in `HoverTank.cs:ProcessMovement` with a saturated rate-target controller. The old PD (`torque = Gain·error − Damp·yawRate`) reached a terminal turn rate proportional to error, so steering bled off as the hull approached the target heading.
- New scheme: command a constant yaw rate toward the target heading, clamped to `AutoSteerMaxRate`. The commanded rate only tapers inside `AutoSteerSettleBand` (linear ramp to zero). Torque is a stiff drive toward the commanded rate (`AutoSteerDamp · (desiredRate − yawRate)`), which also damps residual yaw when the commanded rate is zero.
- Removed the `AutoSteerGain` export (replaced by `AutoSteerMaxRate` + `AutoSteerSettleBand`).
- Updated stale comment referencing the old PD controller.

## Decisions

- Heading-undefined-near-vertical guard preserved: command zero rate when `TryGetHeading` fails so a garbage error can't spin an already-tilted tank; the drive term still bleeds residual yaw.
- Torque still applied about `GlobalBasis.Y` (hull up axis), not world up, to avoid roll/pitch leakage at high tilt.
