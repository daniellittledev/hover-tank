# CLAUDE.md

Godot 4.6 C# game — Battlezone-style hover tank on procedurally generated cratered
terrain. Started as a hover-tank sandbox; now a small game with networking, enemy/ally
AI, a wave survival loop, procedural audio, split-screen, and menus.

**Build/run:** Use Godot editor (Ctrl+B / F5). No CLI build step.

## Key files

| Path | Purpose |
|------|---------|
| `scripts/HoverTank.cs` | Hover spring-damper, WASD movement, jump jets, health, visual interp, TestDrive feel |
| `scripts/TerrainGenerator.cs` | Standard mode: procedural noise heightmap, crater carver, mesh + collision (optional float32 custom map via `CustomMapPath`). TestDrive mode: chunked-LOD render of the `TrackArena` field |
| `scripts/TrackArena.cs` | TestDrive height field: rounded ∞ channel, craters, ramps, eroded noise, background mountains. Pure `SampleHeight(x,z)` — sampled by render (per-LOD) and collision |
| `scripts/FollowCamera.cs` | Lag-smoothed follow camera |
| `scripts/GameSetup.cs` | Main.tscn root: bridges menu mode → game start, per-mode visuals, pause |
| `scripts/network/*` | ENet client/server simulation, prediction/reconciliation, snapshot RPCs |
| `scripts/WaveManager.cs` | Single-player wave/spawn loop, allies, pickups, score/game-over HUD |
| `scripts/UiTheme.cs` | Shared menu/HUD palette + panel/button/separator builders |
| `scenes/MainMenu.tscn` | Entry scene (`run/main_scene`); UI built in code by `MainMenu.cs` |
| `scenes/Main.tscn` | World root: env, sun, terrain, `Tanks` container, HUD |
| `scenes/HoverTank.tscn` | Tank scene: body, turret, 9 hover raycasts (3×3 grid), camera mount |
| `project.godot` | Project config, autoloads, input map |

## Major systems (high level)

- **Scene flow**: `MainMenu` sets mode on the `GameState` autoload → loads `Main.tscn`
  (or `SplitScreen.tscn`) → `GameSetup` reads the mode and starts the matching session.
- **Autoloads** (in `project.godot`, order matters): `GameState` (mode intent),
  `NetworkManager` (ENet lifecycle + RPC dispatch), `AudioManager` (procedural SFX pool).
- **Networking**: server-authoritative physics with client-side prediction and 0.5 m
  reconciliation; 20 Hz snapshots; remote tanks are `Freeze`d ghosts driven by an
  interpolator. See `scripts/network/`.
- **Single-player loop**: `WaveManager` (added by `GameSetup` only for StandardWaves)
  spawns escalating enemy waves, two allies, and pickups; `UnitCommander` handles ally orders.

## Weapons design notes

**Turret is hull-relative, not free-aiming.** The `Turret` node lives under the tank's
`Visual` node and carries `TurretController.cs`, which yaws/pitches it toward the camera aim
but **clamps it to ±MaxYaw from the hull heading**. So the tank still aims primarily by
turning its whole hull, Battlezone-style; the turret only tracks within a limited cone.
`TurretController.GetAimForward()` returns the turret's local -Z. New weapon code should
aim via the turret/aim target, not assume a freely world-aimed turret.

## Conventions

- Namespace: `HoverTank`, target: `net8.0`, physics tick: 60 Hz
- Terrain generation is runtime in `_Ready()` by default. **Baked heightmaps are permitted** for expensive detail/erosion that's too slow to compute every load — load them via the `CustomMapPath` float32 loader. (The old "no baked assets" rule was dropped to support the TestDrive arena's detail pass.)
- All `[Export]` physics params (`HoverHeight`, `ThrustForce`, etc.) are tunable in the Inspector

## Architecture notes

Only add entries here when future changes are very likely to need them.

- **Hover physics**: `HoverTank.cs:_PhysicsProcess` applies spring-damper force at each of the 9 hover raycasts (3×3 grid, cached in `_hoverRays`). Force is proportional to compression + velocity — no separate balance controller needed.
- **Terrain pipeline (standard)**: `TerrainGenerator.cs` carves craters into the raw height array first, then builds `ArrayMesh` and `HeightMapShape3D` in one pass from the same data. No separate collision bake.
- **Terrain pipeline (TestDrive)**: `TrackArena.SampleHeight(x,z)` is the single source of truth (analytic — rounded ∞ channel + craters + ramps + erosion + backdrop mountains). `TerrainGenerator.BuildTrackArena` samples it two ways: **render** = a grid of single-mesh chunk `MeshInstance3D`s, one resolution each — fine (`ReachableCell` 1.25 m) over the reachable disc, coarse (`FarCell` 4 m) for the distant mountains — with fixed-epsilon analytic normals so shading stays smooth regardless of cell size; **collision** = one uniform `HeightMapShape3D` over the reachable disc (the tank only touches nearby terrain). The tank is contained by a ring-of-boxes circular boundary. **No camera-distance LOD / `VisibilityRange` cross-fade** — it was removed because the fade dithered overlapping LODs and made their mismatched downward skirts flicker; uniform-resolution chunks share exact edge samples (no cracks), so skirts only matter at the fine↔coarse ring seam. The panel-grid shader antialiases its lines via `fwidth` and fades them with distance to kill moiré. Detail = sample the field finer; never decimate or stitch.
- **Input actions**: Defined in `project.godot` under `[input]`, not in code. Add new actions there.
- **Scene ownership**: `Main.tscn` owns terrain and a `Tanks` container; `NetworkManager` instances `HoverTank.tscn` into it. Camera is a child of the tank, mounted to `CameraMount`, driven by `FollowCamera.cs`.
- **Autoloads**: Three singletons in `project.godot` — `GameState`, `NetworkManager`, `AudioManager` (registered in that order; `NetworkManager._Ready` reads `GameState`). `GameState`/`AudioManager` expose a static `Instance`; `NetworkManager` is reached via `/root/NetworkManager`. Prefer signals for other cross-scene communication.
- **Visual interpolation**: The tank's rendered meshes (`Body`, `Turret`, `Thruster`, `HoverGlow`) live under a `Visual` `Node3D`, NOT directly under the `RigidBody3D`. `HoverTank._Process` lerps `Visual` between the last two physics transforms (`Engine.GetPhysicsInterpolationFraction()`) so 60 Hz physics renders smoothly at any refresh rate. Any new mesh that should move with the hull goes under `Visual`; anything needing the true physics transform (collision, hover rays, weapon markers, `CameraMount`) stays on the body. Read `HoverTank.VisualTransform` for the render-smooth pose, and call `ResetVisualInterpolation()` after any teleport (e.g. reconcile snap). Interpolation is skipped when `Freeze` is true (remote ghosts). Godot's built-in `physics/common/physics_interpolation` is intentionally OFF — enabling it would double-interpolate.
- **Cache node refs and RIDs in `_Ready`**: Never call `GetParent()`, `GetNode()`, or `.GetRid()` in hot paths (`_Process`, `_PhysicsProcess`, `Fire()`). Resolve and store them as private fields during `_Ready`. This pattern already covers `_hoverRays`, `_ownerRid`, `_tankBody`, `_turret`, etc. — apply it consistently to any new cross-node reference.
- **Mesh winding is clockwise**: Godot renders front faces with clockwise vertex winding (when viewed from outside). In procedural mesh builders like `TankMeshBuilder.cs`, the `Quad`/`Tri` helpers already emit CW order — pass corners CW-as-seen-from-outside. If faces render inside-out (only back faces visible), the winding is reversed, not the normals.

## Session logs

Each session that makes meaningful changes must create `docs/logs/YYYY-MM-DD-<slug>.md` with: date, branch, bullet-point summary of changes, and any architectural decisions made.
