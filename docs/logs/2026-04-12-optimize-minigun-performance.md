# 2026-04-12 — Optimise Minigun Performance

**Branch:** `claude/optimize-minigun-performance-3N3sj`

## Problem

Frame rate dropped noticeably whenever the minigun was held down or when
multiple AI tanks engaged. Three compounding causes:

1. **Fire rate too high** — `MiniGunInterval = 0.05 s` (20 pulls/sec × 2
   barrels = 40 bullets/sec per shooter). Every live bullet does a physics
   ray-cast every tick.
2. **Raycast pre-filter dormant offline** — `ProjectileSpatialGrid` was only
   rebuilt by `ServerSimulation.Tick`, so in SinglePlayer / SplitScreen the
   grid stayed empty (`Count == 0`) and every bullet fell through to a full
   `IntersectRay` call each physics tick.
3. **AI continuous fire** — `EnemyAI` and `AllyAI` set `AIFireRequested =
   true` every physics tick their turret was on-target, producing an
   uninterrupted stream of bullets per AI tank.

## Changes

- **`scripts/WeaponManager.cs`**
  - `MiniGunInterval`: `0.05 → 0.10` s (halves bullet count at equal hold time).
  - Owns the AI minigun burst pacer (`BurstPacer` struct). Exposes
    `AIMinigunBurstLength` / `AIMinigunBurstRest` / `AIMinigunBurstJitter`
    exports. Player hold-trigger behaviour is unchanged — bursts apply only
    to the `AIFireRequested` path.
  - `Fire()` now returns `bool`; the burst pacer only consumes a slot when
    `Fire()` reports a bullet was actually spawned (handles 0-ammo).

- **`scripts/BurstPacer.cs`** *(new)*
  - Pure struct: `Tick(delta)`, `Ready`, `ConsumeShot(rand01)`. No Godot
    dependencies; caller supplies the random sample, so behaviour is
    deterministic under test.

- **`scripts/OfflineSimulation.cs`** *(new)*
  - Tiny `Node` that rebuilds `ProjectileSpatialGrid` each physics tick from
    the `hover_tanks` group. Added as a child of `GameSetup` in SinglePlayer
    and SplitScreen modes. Mirrors `ServerSimulation.Tick`'s grid-rebuild
    responsibility for the offline path.

- **`scripts/GameSetup.cs`**
  - Mode switch simplified: the grid-maintenance responsibility moved out to
    `OfflineSimulation`; a single post-switch guard
    (`mode is SinglePlayer or SplitScreen`) adds it to the tree.
  - No more `_rebuildProjectileGrid` flag, no inline `_PhysicsProcess`,
    no scratch list.

- **`scripts/EnemyAI.cs`** and **`scripts/AllyAI.cs`**
  - All burst state, exports, and `TickMinigunBurst` helpers **deleted**.
    Fire code is back to a single `AIFireRequested = true` line. Burst
    rhythm is now a property of the weapon, not of every shooter type.

## Architectural decisions

- **Burst pacing lives with the weapon, not the shooter.** Previously the
  same burst logic was copy-pasted across `EnemyAI` and `AllyAI` and was
  coupled to a leaky `ReadyToFire` probe on `WeaponManager`. Moving it
  inside `WeaponManager` (a) removes the duplication, (b) means any future
  AI that holds a minigun gets the same rhythm for free, and (c) keeps the
  "this weapon stutters in bursts" knowledge next to `MiniGunInterval`.
  `ReadyToFire` is gone — no longer needed.
- **`BurstPacer` is a pure struct** with no Godot references. It's unit
  testable without the scene tree; the codebase has no test harness today
  but the door is open.
- **`OfflineSimulation` instead of `GameSetup._PhysicsProcess`.** Grid
  maintenance is a simulation concern, not a scene-wiring concern. The new
  node sits parallel to `ServerSimulation` in role and keeps `GameSetup`
  focused on bootstrap + overlays.
- **`Fire()` → `bool`.** Prevents the subtle bug where a shot that early-
  returned on 0 ammo would still have consumed a burst slot. The player
  path ignores the return value, unchanged.
- **No change to `Projectile.cs`** — the existing spatial-grid pre-filter
  was already correct; it was simply starved of data in offline mode.
