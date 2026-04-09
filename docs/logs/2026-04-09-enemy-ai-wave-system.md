# 2026-04-09 — Enemy AI & Wave Spawn System

**Branch:** `claude/review-development-plans-19aDb`

## Changes

- **`scripts/HoverTank.cs`** — Added `[Export] bool IsEnemy` flag; when true, the CameraMount subtree is freed in `_Ready()` preventing viewport conflicts. Added `[Signal] Died` emitted once when health first reaches zero. `TakeDamage` guards against double-death emission.
- **`scripts/WeaponManager.cs`** — Added `bool AIFireRequested` property (AI sets it to request fire; WeaponManager fires if cooldown allows and clears it). Added `SelectWeapon(WeaponType)` for AI to choose weapon at spawn without player input cycling.
- **`scripts/EnemyAI.cs`** *(new)* — Node child of enemy HoverTank. Each physics tick generates `TankInput` (throttle toward player, AimYaw for auto-steer) and sets `TurretController.TargetAimYaw/Pitch` directly. Smoothed aim noise drifts every 0.25–0.65 s for organic feel. Supports lead targeting for higher waves. Fires when turret angle error is within `FireAngleThreshold`. Infinite ammo.
- **`scripts/WaveManager.cs`** *(new)* — Manages wave lifecycle: deferred first-wave start (terrain physics settle), even radial spawning at 65 m radius, per-wave difficulty config, red hull override, enemy death tracking, wave-complete → next-wave timer. Builds a CanvasLayer HUD: wave banner, enemy counter, game-over overlay with Restart/Menu buttons.
- **`scripts/GameSetup.cs`** — SinglePlayer case now instantiates and adds `WaveManager` after starting the simulation.

## Difficulty Table

| Wave | Enemies | Accuracy | Weapon | Lead |
|------|---------|----------|--------|------|
| 1 | 2 | 0.50 | MiniGun | no |
| 2 | 3 | 0.35 | MiniGun | no |
| 3 | 4 | 0.22 | Rocket | no |
| 4 | 5 | 0.15 | Rocket | yes |
| 5+ | 2+wave | 0.10 | Shell | yes |

## Architectural decisions

- Enemy tanks reuse `HoverTank.tscn` (identical physics/hover) rather than a separate scene. `IsEnemy=true` strips the camera at `_Ready()` time — no .tscn duplication.
- `EnemyAI` drives the tank via the existing `SetInput()` contract, identical to how the network layer drives players. No special-casing in HoverTank physics.
- `TurretController.TargetAimYaw/Pitch` were already public setters; EnemyAI writes them directly since `AimCamera` is null on enemy tanks (HoverTank won't overwrite them).
- WaveManager is added programmatically in GameSetup (SinglePlayer only) — no autoload, no scene file change required.
