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
| `scenes/HoverTank.tscn` | Tank scene: body, turret, 9 hover raycasts (3×3 grid), camera mount |
| `terrain/heightmap.png` | 128×128 grayscale base terrain (replaceable) |
| `project.godot` | Godot project config and input map |

## Physics tuning

All hover/movement parameters are Godot `[Export]` properties — adjust them in the editor Inspector without recompiling:

- `HoverHeight`, `SpringStrength`, `SpringDamping` — hover feel
- `ThrustForce`, `TurnTorque`, `MaxSpeed` — movement
- `JumpImpulse`, `JumpSustainForce` — jump jets
- `CraterCount`, `CraterDepth`, `CraterRadiusMin/Max` — terrain shape

## Weapons design notes

**Turret does not rotate independently.** The `Turret` node (and its child `Barrel`) are
`MeshInstance3D` nodes parented directly to the `HoverTank` `RigidBody3D`. They carry no
script and have no rotation logic — they rotate only as part of the tank body. This is a
deliberate design decision: the tank aims by turning its entire hull, Battlezone-style.
Any future weapon system should fire along the hull's local -Z axis and must not assume an
independently aimed turret exists.

## Conventions

- Namespace: `HoverTank`
- Target framework: `net8.0`
- Physics tick: 60 Hz (`project.godot`)
- All terrain generation happens at runtime in `_Ready()` — no baked assets

## Architecture notes

Key architectural decisions that future changes are likely to need:

- **Hover physics**: Spring-damper force is applied per-raycast in `HoverTank.cs:_PhysicsProcess`. Each of the 4 corner raycasts casts downward; if it hits, a vertical force proportional to compression and velocity is added at that point. This keeps the tank stable on slopes without a separate balance controller.
- **Terrain mesh + collision**: `TerrainGenerator.cs` builds both an `ArrayMesh` (for rendering) and a `HeightMapShape3D` (for collision) from the same heightmap data in a single pass. Craters are carved into the raw height array before either is constructed — there is no separate collision bake step.
- **Input map**: All input actions (`move_forward`, `move_back`, `turn_left`, `turn_right`, `jump`) are defined in `project.godot` under `[input]`. Add new actions there, not in code.
- **Scene structure**: `Main.tscn` owns the terrain `Node3D` and instances `HoverTank.tscn` as a child. The camera is a child of the tank scene mounted to a `CameraMount` node, driven by `FollowCamera.cs` with lag smoothing.
- **No singletons / autoloads**: All state is local to each script. If you need cross-scene communication, prefer signals over autoloads.

## Session logs

Each Claude session that makes meaningful changes must append a summary entry to `docs/logs/`. Create a new file named `YYYY-MM-DD-<short-slug>.md` (e.g. `2026-04-08-add-logging.md`). Include:

- Date
- Branch worked on
- Summary of changes made (bullet points)
- Any architectural decisions or trade-offs noted

This keeps a lightweight audit trail without polluting commit history.
