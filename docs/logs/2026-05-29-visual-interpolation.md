# Render-frame visual interpolation for the tank

- **Date:** 2026-05-29
- **Branch:** claude/competent-bell-654bd7

## Problem

Driving felt like it juddered roughly every other frame. Cause: the player tank is a
`RigidBody3D` whose transform only updates at the 60 Hz physics rate. With render and physics
both at ~60 Hz but not phase-locked, some render frames get zero physics ticks and some get two,
so the mesh (and the camera following it) stair-steps. This is the classic fixed-tick-vs-render
beat, not a performance issue.

## Change (Option B: manual fixed-timestep interpolation)

Decoupled the rendered meshes from the physics body and interpolate them between the last two
physics transforms each render frame.

- **`scenes/HoverTank.tscn`**: inserted a `Visual` (`Node3D`) child of the `HoverTank` body and
  reparented the render-only nodes under it — `Body`, `Turret`, `Thruster`, `HoverGlow`.
  Physics/logic nodes (`Collision`, the 9 `HoverRay`s, `WeaponManager` + muzzle markers,
  `CameraMount`) stay on the body so they keep the true physics transform.
- **`scripts/HoverTank.cs`**: capture prev/cur physics transforms at the end of `_PhysicsProcess`;
  in a new `_Process`, set `Visual.GlobalTransform = prev.InterpolateWith(cur, Engine.GetPhysicsInterpolationFraction())`.
  Exposes `VisualTransform` (render-smooth transform) and `ResetVisualInterpolation()` (collapse
  history after a teleport). Interpolation is skipped when `Freeze` is true.
- **`scripts/FollowCamera.cs`**: orbit centre and FPS eye now read `_tank.VisualTransform` instead
  of the raw physics transform, so the view tracks the same smoothed position the player sees.
  Steering/heading math still reads the raw `Basis`.
- **`scripts/network/ClientSimulation.cs`**: calls `ResetVisualInterpolation()` after the reconcile
  snap so the mesh doesn't smear from the predicted position to the corrected one.
- **Path fixups** after the reparent: `Turret` → `Visual/Turret` in `HoverTank`, `FollowCamera`,
  `WeaponManager`, `EnemyAI`, `AllyAI`; `Body` → `Visual/Body` in `AllyAI`, `WaveManager`;
  `TurretController` now finds the hull via `../..`.

Framerate was already capped at 60 (`run/max_fps=60`); vsync defaults to enabled. No change needed.

## Decisions

- **Manual interpolation, not Godot's built-in `physics/common/physics_interpolation`.** Built-in
  is left OFF — enabling it would double-interpolate against this manual path. The manual approach
  also stays consistent with how remote tanks already render (snapshot interpolation in
  `RemoteEntityInterpolator`).
- **Gated on `!Freeze`.** Remote ghosts and dead tanks are positioned externally
  (`RemoteEntityInterpolator` drives the body), so they skip the physics-tick interpolation and the
  `Visual` node simply tracks the body. Live, locally-simulated tanks (player + on-host AI) interpolate.
- **~1 physics tick (16 ms) of render latency** is the accepted cost of fixed-timestep interpolation.
- The death impulse needs no reset — it changes velocity, not position, so motion stays continuous.
