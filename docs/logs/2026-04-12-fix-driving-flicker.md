# Fix driving flicker in single-player

- **Date:** 2026-04-12
- **Branch:** main

## Summary

Player reported the tank "flickers back a frame before jumping to its actual
position" while driving. Real root cause (after ruling out physics
interpolation): the `FollowCamera` smooths its orbit centre with
`Lerp(target, PositionLag * dt)` in `_Process`. When a render frame hitched
(C#/Mono GC pause), the oversized `dt` made the lerp catch up too far that
frame, snapping the camera forward — which reads visually as the tank
jumping backward, then popping forward again the next frame. Only showed
while moving, because it requires relative motion between camera and tank.

## Changes

- `project.godot`: capped `run/max_fps=60` so render rate matches the
  60 Hz physics tick (avoids "hold then snap" on high-refresh monitors).
- `scripts/FollowCamera.cs`: cap the smoothing `dt` at `1/60 s` before
  feeding it to the orbit-centre lerp. One physics tick is the natural
  upper bound — the tank's position can't change faster than that, so a
  longer render frame shouldn't produce a larger camera correction.

## Decisions tried and reverted

Enabling `physics/common/physics_interpolation=true` plus
`GetGlobalTransformInterpolated` on the camera made the tank mesh shake
badly (known issues with C# RigidBody3D and per-tick child transforms).
Physics interpolation is NOT a good fit for this project right now —
stick with matching render/physics rate at 60 Hz.
