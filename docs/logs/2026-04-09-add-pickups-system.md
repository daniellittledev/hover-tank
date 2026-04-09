# 2026-04-09 — Add Pickups System

**Branch:** `claude/add-pickups-system-8Dfo5`

## Changes

- **`scripts/Pickup.cs`** (new) — `Area3D`-based pickup entity
  - `PickupType` enum: `Health`, `MiniGunAmmo`, `RocketAmmo`, `TankShellAmmo`
  - Proximity detection via `BodyEntered` — no physics collision, tank drives through
  - Applies effect to player tank only (enemies/allies ignored)
  - Restores 40 HP / 200 MiniGun ammo / 8 rockets / 5 shells per pickup
  - Procedural colored mesh + emissive material + OmniLight3D glow per type
  - Bob and spin animation in `_Process`; staggered phase so nearby pickups don't sync
  - Registers in `"pickups"` group for field-count capping

- **`scripts/WaveManager.cs`** (modified)
  - Added `using System;`
  - Added pickup config constants: 4 per wave, max 10 on field, spawn 12–35 m from player
  - `StartNextWave()` now calls `SpawnPickupsForWave()` after spawning enemies
  - `SpawnPickupsForWave()` — checks existing count, cycles through all 4 types
  - `FindPlayerPosition()` — locates the player tank in the `"hover_tanks"` group
  - `SpawnPickup()` — random polar offset from player, terrain-height-probed Y placement

## Architectural decisions

- Pickups are `Area3D` (not `RigidBody3D` or `StaticBody3D`) so they don't push the tank
  and don't require physics layer coordination beyond `CollisionMask = 1`
- Spawning logic lives in `WaveManager` (not a separate spawner class) since it already
  owns wave lifecycle and the terrain-height probe utility
- `PickupType` enum is defined alongside `Pickup` (same file), following the project
  convention of co-locating enums with their primary consumer
