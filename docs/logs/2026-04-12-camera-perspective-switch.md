# 2026-04-12 — First-person / third-person camera switch

Branch: claude/add-camera-perspective-switch-CW6Sp

## Changes

- Added `ViewMode` enum and `_mode` field to `FollowCamera`. Default stays third-person.
- F1 / F2 bound in `project.godot` to new `camera_first_person` / `camera_third_person` actions; `FollowCamera._Process` watches `IsActionJustPressed` each frame and calls `SetViewMode`.
- TPS branch in `_Process` is the unchanged orbital code. New FPS branch puts the eye inside the hull (`FpsEyeOffset` = turret height, slightly aft of barrel) and orients the camera directly from `CurrentYaw`/`CurrentPitch`.
- FPS yaw is clamped each frame to `hullYaw ± FpsYawConeHalfWidth` (default ~20°). The existing `HoverTank` auto-steer PD then torques the hull toward the (clamped) reticle yaw at its natural turn rate, giving Battlezone-style "reticle leads the hull" feel without any change to tank physics.
- Pitch uses a wider FPS range (`FpsPitchMin/Max`, ±0.6 rad) so the reticle can aim vertically, and is re-clamped whenever mode changes.
- Turret mesh (`Turret` Node3D) is hidden in FPS to avoid clipping the in-cockpit camera. `TurretController` rotation logic stays active so weapons keep aiming correctly — only the mesh toggles.
- Cached `_turretNode` reference in `_Ready` per CLAUDE.md "cache node refs in `_Ready`" rule.

## Decisions

- **Single source of truth for aim state.** Both modes share `CurrentYaw` / `CurrentPitch` / `AimTarget`, so `LocalInputHandler`, `TurretController`, and `WeaponManager` needed zero changes — they already read these properties and inherit the FPS reticle-aim behaviour automatically.
- **FPS orientation via explicit basis composition.** `new Basis(Up, yaw) * new Basis(Right, -pitch)` (yaw post-multiplied onto pitch) keeps the camera unrolled at all yaw angles, matching the standard FPS Euler order and avoiding the roll artefact that would appear if both rotations were composed in world space via chained `Rotated` calls.
- **Cone clamp, not a separate reticle offset.** Re-clamping `CurrentYaw` against `hullYaw ± FpsYawConeHalfWidth` each frame was cheaper than introducing a separate reticle-yaw field, and reuses the auto-steer PD rather than adding a new hull-turning path. Side-effect: when the player pushes the reticle to the cone edge, the hull rotates continuously to catch up (exactly the Battlezone feel).
- **Turret mesh visibility, not removal.** Hiding the mesh rather than detaching it keeps the turret's pitch animation and weapon muzzle transforms intact — switching back to TPS requires no re-wiring.
