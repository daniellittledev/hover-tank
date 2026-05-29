# Aim-driven body pitch

- **Date:** 2026-05-29
- **Branch:** `claude/festive-almeida-7c3990`

## Summary

Made the hull pitch up/down with the look direction. Previously only the camera (`FollowCamera`) pitched while the `HoverTank` hull stayed level, so the gun (which fires along hull -Z) never elevated with the aim. All changes are in `scripts/HoverTank.cs`.

Two cooperating mechanisms, because grounded and airborne pitch have opposite physics:

- **Grounded — hover-height bias** (`ProcessHoverForces`): each of the 9 hover rays gets a per-ray *target height* offset derived from the desired pitch (`targetHeight = HoverHeight - zLocal * tan(pitch)`). The spring grid then *settles* the hull at an angle instead of being fought by a torque. Front rays lift / rear rays sink for nose-up. A pitch torque can't win against the strong springs (`SpringStrength=300`), so we move the equilibrium rather than push against it.
- **Airborne — PD torque** (`ProcessBodyPitchAir`): when fewer than `GroundedRayCount` (3) rays touch ground, the springs aren't engaged, so a PD torque about the hull pitch axis (`GlobalBasis.X`) tilts the nose toward the target. This is what lets the tank pitch up and over while jump jets hold it aloft.

## Requirements mapped to clamps

`FollowCamera.CurrentPitch` convention: negative = look up, positive = look down. `lookFrac = clamp(-CurrentPitch / LookPitchRef, -1, 1)` maps look angle to a signed hull-pitch target (+ve = nose up), clamped asymmetrically:

- **Front never dips below ~1 m looking down:** nose-down pins the front rays at `HoverHeight` (1 m) and lifts the rear to make the angle; `MaxPitchDown=0.18` keeps it mild.
- **Front lifts to ~2 m looking up, grounded, no jets:** `MaxPitchUpGround=0.62` rad → front ray (z=-1.4) target ≈ `1 + 1.4*tan(0.62) ≈ 2.0 m`.
- **Look up and over with jets:** the airborne torque target opens to `MaxPitchUpAir=2.4` rad when `JumpJet` is held; `PitchAirGain=80` is strong enough to beat self-righting (`UprightGain=25`) and carry the nose past vertical.

## Notes / decisions

- The grounded bias always uses `MaxPitchUpGround` (kept under 90°) so `Tan` stays well-defined even while jets are held and grounded; only the airborne torque uses the wide `MaxPitchUpAir`. This also gives a smooth handoff as the jets lift the tank off the ground.
- No camera changes were needed: `FollowCamera` already builds its own orientation each frame (orbit/look-at in TPS, world-space yaw/pitch in FPS), so the view still aims where the player points regardless of hull tilt.
- AI/server tanks (`AimCamera == null`) get target pitch 0 — behaviour unchanged.
- `_rayLocalZ` is cached in `_Ready` (per the "cache node refs in `_Ready`" convention); no transform reads in the hot path.
- All thresholds are `[Export]` (`LookPitchRef`, `MaxPitchDown`, `MaxPitchUpGround`, `MaxPitchUpAir`, `PitchAirGain`, `PitchAirDamp`) — the 1 m / 2 m targets are geometry-derived defaults and may want a feel pass in-editor (Godot can't be driven from CLI here).
