# 2026-04-09 — Battlezone-Style Unit Control & Formations

**Branch:** `claude/unit-control-formations-Zeh4A`

## Summary of Changes

### New files

- **`scripts/AllyAI.cs`** — State-machine controller for player-allied tanks.
  Five orders: `Idle`, `Follow`, `Hold`, `MoveToWaypoint`, `AttackTarget`.
  Allies opportunistically shoot the nearest enemy at all times (except when
  explicitly ordered to attack a specific target, which takes priority).
  Exposes `IsSelected` (triggers hull emission glow), `FormationSlot` (set by
  UnitCommander each frame while Following), `WaypointPosition`, and `Tank`
  (public property over the private `_tank` ref, read by UnitCommander for HUD).

- **`scripts/UnitCommander.cs`** — Attached to the player tank in single-player.
  - **Selection**: LMB replaces the selected unit; Shift+LMB adds; Ctrl+LMB
    toggles. All via a crosshair raycast (camera forward × 300 m) that checks
    for `IsFriendlyAI` tanks. Sets `WeaponManager.SuppressFireThisFrame` when
    consuming a click to prevent simultaneous fire.
  - **Orders**: G = Follow, H = Hold, V = Move Here, B = Attack Target,
    F = Cycle Formation, X = Deselect All.
  - **Formation slots**: Three formations (Wedge / Line / Column, 8 m spacing),
    calculated in player-local space and converted to world-space each frame.
  - **Terrain ring marker**: `ArrayMesh` circle of 48 line-segments, added to
    the scene root (not the player node). Green on terrain, red when crosshair
    is on an enemy. Pulses gently via scale animation. Visible only while units
    are selected; disappears on deselect.
  - **HUD**: `CanvasLayer` layer 5. Top-left card row (one 56×56 panel per
    selected ally) shows a health bar (green→yellow→red) and a status dot
    (cyan=Follow, orange=Hold, yellow=Moving, red=Attack). Action list below
    the cards highlights the active order and dims `[B] Attack` unless the
    crosshair is on an enemy.

### Modified files

- **`scripts/HoverTank.cs`** — Added `IsFriendlyAI` export flag. When true,
  camera mount is freed (same as `IsEnemy`) and the tank is added to both
  `"hover_tanks"` and `"ally_tanks"` groups.

- **`scripts/EnemyAI.cs`** — `FindPlayer()` now excludes `IsFriendlyAI` tanks
  so enemies target only the human player, not allied AI tanks.

- **`scripts/WeaponManager.cs`** — Added `SuppressFireThisFrame` property.
  When set in `_Input` (by UnitCommander), `_Process` skips player-input fire
  for that frame and clears the flag. Prevents a selection click from also
  firing a weapon.

- **`scripts/WaveManager.cs`** — Added `SpawnStartingAllies()` (called deferred
  from `_Ready`), which places two green-hulled allied tanks flanking the player
  start position. Each gets an `AllyAI` child named `"AllyAI"` so UnitCommander
  can find it after a crosshair raycast.

- **`scripts/network/NetworkManager.cs`** — `StartSinglePlayer()` now also adds
  a `UnitCommander` child to the player tank after the `LocalInputHandler`.

- **`project.godot`** — Six new input actions: `unit_order_follow` (G),
  `unit_order_hold` (H), `unit_order_move` (V), `unit_order_attack` (B),
  `unit_formation_cycle` (F), `unit_deselect_all` (X).

## Architectural Decisions

- **`IsFriendlyAI` flag on HoverTank** (not a separate scene) — allies reuse
  the same `HoverTank.tscn` with a flag and a dynamically added `AllyAI` child,
  identical to how enemies are set up. No new scenes needed.

- **UnitCommander on the player tank, not as an autoload** — keeps all
  command-and-control state local to the player node; consistent with the
  project's "no autoloads" policy.

- **Ring marker parented to scene root, not the player tank** — avoids
  inheriting the tank's scale and rotation, so it always lies flat on terrain.

- **`SuppressFireThisFrame` over `SetInputAsHandled()`** — `SetInputAsHandled`
  does not suppress `Input.IsActionJustPressed`, which is what WeaponManager
  uses. The flag approach is the only safe cross-script solution without
  refactoring WeaponManager's input path.

- **Formation slots recalculated every `_Process` frame** — allies smoothly
  follow the player's heading changes as they turn. No interpolation needed on
  the ally side; the auto-steer PD controller handles smooth slot pursuit.
