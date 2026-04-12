# 2026-04-12 — Unify angular drag / steering damping

Branch: main

## Changes

- Removed unused `TurnTorque` export (A/D strafes via `Steer` branch, no yaw torque path uses it).
- Replaced isotropic `AngularDrag` with `TiltDrag` — applied only on X/Z axes (roll/pitch) to kill tumbling from terrain jolts.
- Folded old Y-axis drag into `AutoSteerDamp` (35 → 65) so yaw damping lives entirely with the PD controller that owns it.

## Decisions

- One knob per concern: `AutoSteerGain` for turn responsiveness, `AutoSteerDamp` for yaw stability, `TiltDrag` for tumble recovery. No cross-coupling between yaw PD and bulk angular drag.
- Net yaw damping unchanged (prior 30+35 = 65), so the earlier spin-resonance fix still holds.
