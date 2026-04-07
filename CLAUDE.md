# CLAUDE.md

## Project overview

Godot 4.3 C# game — Battlezone-style hover tank on procedurally generated cratered terrain.

## Build & run

```bash
# From within Godot editor:
# Build:  Project → Build  (Ctrl+B)
# Run:    F5
```

There is no CLI build step; Godot manages MSBuild internally.

## Key files

| Path | Purpose |
|------|---------|
| `scripts/HoverTank.cs` | RigidBody3D: hover spring-damper, WASD movement, jump jets |
| `scripts/TerrainGenerator.cs` | Heightmap loader, crater carver, ArrayMesh + HeightMapShape3D |
| `scripts/FollowCamera.cs` | Lag-smoothed follow camera |
| `scenes/Main.tscn` | World root: sky, sun, terrain, tank instance |
| `scenes/HoverTank.tscn` | Tank scene: body, turret, 4 hover raycasts, camera mount |
| `terrain/heightmap.png` | 128×128 grayscale base terrain (replaceable) |
| `project.godot` | Godot project config and input map |

## Physics tuning

All hover/movement parameters are Godot `[Export]` properties — adjust them in the editor Inspector without recompiling:

- `HoverHeight`, `SpringStrength`, `SpringDamping` — hover feel
- `ThrustForce`, `TurnTorque`, `MaxSpeed` — movement
- `JumpImpulse`, `JumpSustainForce` — jump jets
- `CraterCount`, `CraterDepth`, `CraterRadiusMin/Max` — terrain shape

## Conventions

- Namespace: `HoverTank`
- Target framework: `net8.0`
- Physics tick: 60 Hz (`project.godot`)
- All terrain generation happens at runtime in `_Ready()` — no baked assets
