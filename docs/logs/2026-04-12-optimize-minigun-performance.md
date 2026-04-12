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
  - Added `public bool ReadyToFire => _cooldown <= 0f` so AI burst pacing can
    tell whether a fire-request this tick will actually produce a shot.

- **`scripts/GameSetup.cs`**
  - New `_PhysicsProcess` that rebuilds `ProjectileSpatialGrid` once per tick
    from the `hover_tanks` group, but **only** in offline modes
    (`SinglePlayer`, `SplitScreen`). `NetworkHost` continues to rebuild via
    `ServerSimulation.Tick`; `NetworkJoin` clients keep the grid empty (their
    projectiles are `IsVisualOnly` and never ray-cast).
  - Scratch `_gridPositions` list reused each tick — no per-tick allocation.

- **`scripts/EnemyAI.cs`** and **`scripts/AllyAI.cs`**
  - Added `BurstLength` (default 6 pulls) and `BurstRestSeconds` (≈1.0–1.5 s
    with random jitter) exports.
  - `TryFire` / `TryFireAt` now gate `AIFireRequested` on a burst counter;
    counter is decremented **only on ticks where `_weapons.ReadyToFire` is
    true**, so bursts last the intended number of shots instead of being
    consumed in a single cooldown window.
  - EnemyAI burst logic only applies when `PreferredWeapon == MiniGun`;
    rockets/shells stay on their existing per-shot cadence.

## Architectural decisions

- **Burst counting via `ReadyToFire`** rather than subscribing to the
  `WeaponManager.Fired` event. The event fires twice per minigun pull (one
  per barrel), which would complicate counting; the cooldown probe is simpler
  and reads cleanly from the AI side.
- **Grid rebuild centralised in `GameSetup`** (which always exists and owns
  mode dispatch) rather than in `WaveManager` (single-player only) or a new
  autoload. Keeps the offline vs. networked branching next to the existing
  `StartSinglePlayer` / `StartHost` switch.
- **No change to `Projectile.cs`** — the existing spatial-grid pre-filter is
  already correct; it was simply starved of data in offline mode.
- Dead tanks (`Health == 0`) are still inserted into the grid, matching
  `ServerSimulation.Tick`'s behaviour. They're removed from the scene tree
  shortly after death, so the divergence is negligible.
