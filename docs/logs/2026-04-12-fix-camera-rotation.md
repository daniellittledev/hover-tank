# 2026-04-12 — Camera/tank rotation fixes + self-righting

**Branch:** `claude/fix-camera-rotation-kIlLB`

## Changes

- **`scripts/MathUtils.cs`** — Fixed `AngleDiff` to use `Mathf.PosMod` instead
  of C#'s `%`. The old implementation depended on `%` wrapping into
  `[0, 2π)`, but C# `%` preserves the sign of the dividend, so for large
  negative inputs (e.g. accumulated free-look yaw) it returned values outside
  `[-π, π]`. This fed garbage into `HoverTank.ProcessMovement`'s yaw PD
  controller and produced an unrecoverable spin after fast mouse turns.
  Added `WrapAngle` helper.
- **`scripts/FollowCamera.cs`** — Wrap `CurrentYaw` to `[-π, π]` on every
  mouse/stick delta so long play sessions don't degrade float precision.
  Bumped `PositionLag` default from `6` → `25`. The orbit centre now
  time-constants at ~40 ms, which keeps the camera (and therefore the
  crosshair) tight on the tank during strafe; previously the ~170 ms lag let
  the orbit centre fall up to 2 m behind at max strafe speed, misaligning
  the crosshair from where bullets actually went.
- **`scripts/HoverTank.cs`** — Added `ProcessSelfRighting()` + tunable
  `UprightGain`. Computes a restoring torque as
  `(localUp × worldUp) * UprightGain` — magnitude = sin(tilt), so it's
  gentle near upright and peaks at 90°. Falls back to a roll-axis nudge
  when the tank is within a few degrees of fully inverted (where the cross
  product degenerates). Combined with existing `TiltDrag` this is a PD
  controller that lets the tank tumble through a backflip but always
  recovers to upright.
- **`scenes/HoverTank.tscn`** — Set `center_of_mass_mode = Custom`,
  `center_of_mass = (0, -0.35, 0)`. Physically bottom-heavy body
  reinforces the self-righting behaviour.

## Architectural decisions

- Rejected the user's quaternion suggestion for the spinning-camera bug.
  The actual defect was a numerical bug in angle wrapping, not a
  representation problem — a quaternion rewrite would have added complexity
  without fixing the root cause. The one-line `PosMod` fix covers it.
- Self-righting is an *active* PD torque rather than a passive CoM-only
  trick because a low centre of mass does nothing to right a body in
  free-fall (gravity acts at the CoM and creates no torque). The CoM
  offset only helps while the hover springs are grounded. Both mechanisms
  are applied; the explicit torque is what handles the inverted-in-air
  case the user cares about.
