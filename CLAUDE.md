# CLAUDE.md

Godot 4.3 C# game — Battlezone-style hover tank on procedurally generated cratered terrain.

**Build/run:** Use Godot editor (Ctrl+B / F5). No CLI build step.

## Key files

| Path | Purpose |
|------|---------|
| `scripts/HoverTank.cs` | Hover spring-damper, WASD movement, jump jets |
| `scripts/TerrainGenerator.cs` | Heightmap loader, crater carver, mesh + collision |
| `scripts/FollowCamera.cs` | Lag-smoothed follow camera |
| `scenes/Main.tscn` | World root: sky, sun, terrain, tank instance |
| `scenes/HoverTank.tscn` | Tank scene: body, turret, 9 hover raycasts (3×3 grid), camera mount |
| `terrain/heightmap.png` | 128×128 grayscale base terrain |
| `project.godot` | Project config and input map |

## Weapons design notes

**Turret does not rotate independently.** The `Turret` node (and its child `Barrel`) are
`MeshInstance3D` nodes parented directly to the `HoverTank` `RigidBody3D`. They carry no
script and have no rotation logic — they rotate only as part of the tank body. This is a
deliberate design decision: the tank aims by turning its entire hull, Battlezone-style.
Any future weapon system should fire along the hull's local -Z axis and must not assume an
independently aimed turret exists.

## Conventions

- Namespace: `HoverTank`, target: `net8.0`, physics tick: 60 Hz
- Terrain generation is runtime in `_Ready()` — no baked assets
- All `[Export]` physics params (`HoverHeight`, `ThrustForce`, etc.) are tunable in the Inspector

## Architecture notes

Only add entries here when future changes are very likely to need them.

- **Hover physics**: `HoverTank.cs:_PhysicsProcess` applies spring-damper force at each of 4 corner raycasts. Force is proportional to compression + velocity — no separate balance controller needed.
- **Terrain pipeline**: `TerrainGenerator.cs` carves craters into the raw height array first, then builds `ArrayMesh` and `HeightMapShape3D` in one pass from the same data. No separate collision bake.
- **Input actions**: Defined in `project.godot` under `[input]`, not in code. Add new actions there.
- **Scene ownership**: `Main.tscn` owns terrain and instances `HoverTank.tscn`. Camera is a child of the tank, mounted to `CameraMount`, driven by `FollowCamera.cs`.
- **No autoloads**: All state is script-local. Use signals for cross-scene communication.

## Session logs

Each session that makes meaningful changes must create `docs/logs/YYYY-MM-DD-<slug>.md` with: date, branch, bullet-point summary of changes, and any architectural decisions made.
