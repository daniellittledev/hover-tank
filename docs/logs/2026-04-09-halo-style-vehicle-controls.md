# 2026-04-09 — Halo-Style Vehicle Aiming Controls

**Branch:** `claude/vehicle-aiming-controls-Moz1W`

## Summary of Changes

- **`scripts/FollowCamera.cs`** — Complete rewrite. Camera now orbits the tank at a fixed
  radius using independently controlled yaw/pitch (mouse + right analog stick). Spring-follows
  tank position for smooth lag. Exposes `CurrentYaw`, `CurrentPitch`, and `AimTarget`
  (world-space raycast hit from screen centre, used by rockets).

- **`scripts/TurretController.cs`** — New script attached to the `Turret` node. Rotates the
  turret toward the camera yaw (clamped ±90° from tank nose) at a configurable slew rate.
  Pitches the `Barrel` child for barrel elevation. Exposes `GetAimForward()` for weapons.

- **`scripts/HoverTank.cs`** — Replaced the simple `Steer * TurnTorque` with a PD
  auto-steer controller that drives the tank body toward `TankInput.AimYaw` (the camera's
  world-space yaw). A/D keys add a steering bias on top. Wires turret and weapon aim target
  each physics tick. Exposes `AimCamera` for input handlers.

- **`scripts/network/NetworkMessages.cs`** — Added `AimYaw` and `AimPitch` to `TankInput`.
  Updated `FromParts` to accept the new fields (defaulting to 0 for backwards compatibility).

- **`scripts/LocalInputHandler.cs`** — Added `Camera` property; populates `AimYaw`/`AimPitch`
  in `TankInput` from the camera each physics tick.

- **`scripts/WeaponManager.cs`** — Cannon and rockets now fire along the turret's aim
  direction via `TurretController.GetAimForward()`. Rockets receive `AimTarget` as a guided
  destination. Minigun remains fixed to tank body forward (unchanged).

- **`scripts/Projectile.cs`** — Added `TargetPosition` property for guided rockets. When set,
  rockets steer toward the target at `MaxTurnRadPerSec` (1.8 rad/s) per physics frame.

- **`scripts/CrosshairHUD.cs`** — New script; draws a gapped crosshair + centre dot using
  `_Draw()` on a fullscreen `Control` inside a `CanvasLayer`.

- **`scripts/network/NetworkManager.cs`** — Updated `SubmitInputRpc` and `SendInput` to
  include `aimYaw` and `aimPitch`. Wires `AimCamera` into `LocalInputHandler` and
  `ClientSimulation` after tank spawn.

- **`scripts/network/ClientSimulation.cs`** — Added `Camera` property; populates
  `AimYaw`/`AimPitch` in `CaptureInput()`.

- **`scenes/HoverTank.tscn`** — Turret node changed from `MeshInstance3D` to `Node3D`
  (with `TurretController` script); mesh moved to new `TurretMesh` child. Added `HUD`
  `CanvasLayer` with `Crosshair` Control node.

- **`project.godot`** — Added `look_right`, `look_left`, `look_down`, `look_up` input
  actions mapped to right analog stick axes 2 and 3 (deadzone 0.2).

## Architectural Decisions

- **Auto-steer is a PD controller** (`AutoSteerGain=120`, `AutoSteerDamp=20`) rather than
  a direct velocity assignment. This preserves the tank's physical inertia and gives a
  satisfying lag/overshoot before it aligns — matching the Halo Ghost feel.

- **Turret rotation in `_Process` not `_PhysicsProcess`**: visual-only transform, so it
  runs with render frames for smoothness without affecting physics.

- **Rockets are guided to a snapshot target** (where you aimed when you fired), not
  continuously homing. This matches Halo's rocket behaviour — they don't follow moving targets.

- **Minigun is intentionally fixed forward** on the tank body; it benefits from the
  auto-steer aligning the tank quickly, which is the correct gameplay feel.
