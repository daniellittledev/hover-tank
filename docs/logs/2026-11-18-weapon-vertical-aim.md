# 2026-11-18 — Weapon vertical aim

Branch: `claude/quirky-panini-9719d5`

## Summary

- Weapons now fire at a vertical angle that matches where the cursor points, instead of always firing flat along the horizontal turret forward.
- Added `ElevateTowardAim` to `WeaponManager`: keeps the existing turret-controlled horizontal (yaw) direction, but derives the pitch from the direction between the muzzle and the crosshair's world hit point (`AimTarget`, already raycast against terrain/ships by `FollowCamera`).
- Pitch is clamped to a configurable `[MinFireAngleDeg, MaxFireAngleDeg]` relative to the tank's hull (XZ) plane. Defaults: -30° depression, +60° elevation.
- Applied to all three weapons (minigun, rocket, tank shell). Rockets still curve toward `AimTarget` as before; the initial launch pitch now also points toward it.

## Decisions

- Horizontal aim stays sourced from the turret forward so the existing yaw clamp/slew (`TurretController`) keeps meaning; only the vertical component is taken from the aim ray. This matches the request ("vertical angle should match where the cursor is pointing") without bypassing turret yaw limits.
- Pitch clamp is relative to the hull plane (uses `_tankBody.GlobalBasis.Y`), so the limits hold even when the tank is tilted on terrain.

## Follow-up (not done)

- The barrel mesh elevation in `TurretController` is still clamped to roughly [-5°, +20°] and driven by camera pitch, so at steep fire angles the visible barrel won't line up with the shot. Worth reconciling if the mismatch reads as wrong.
