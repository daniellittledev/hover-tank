# 2026-04-12 — TestDrive infinite metallic-grid terrain

**Branch:** `claude/procedural-terrain-generation-br9yW`

## Summary

- Added infinite chunk-streamed terrain for **TestDrive** single-player mode.
  Standard/wave/multiplayer/split-screen modes keep the original finite cratered
  heightmap unchanged.
- Terrain under TestDrive now uses a **shiny metallic material with a black
  grid texture**, procedurally generated at runtime.
- Replaced craters with a **rolling-hills + asymmetric-bumps** noise stack so
  driving around produces lots of hills and sharp jumps.

## Changes

- `scripts/TerrainGenerator.cs`
  - `_Ready` now branches on `GameState.SinglePlayerMode`: `TestDrive` →
    `SetupInfiniteTerrain`, everything else → original `GenerateTerrain`.
  - New chunk-streaming system:
    - `ChunkCells`, `ChunkLoadRadius`, `InfiniteHillScale`, `InfiniteJumpScale`
      `[Export]`s.
    - A chunk is a `ChunkCells × ChunkCells` heightfield with its own
      `ArrayMesh`, `HeightMapShape3D` collider, and `StaticBody3D`, parented
      under a `Node3D` at the chunk's world-space origin.
    - `_Process` tracks the player tank (via the `hover_tanks` group) and
      rebalances chunks whenever the player crosses a chunk boundary: frees
      out-of-range chunks, builds missing in-range ones. A `(2R+1)²` block is
      pre-built in `_Ready` so spawn frame has ground.
  - `SampleHeight(wx, wz)` is the single source of truth for terrain height,
    sampled in **world space** so adjacent chunks share boundary values and
    normals exactly — no seam stitching required.
  - `CreateMetallicGridMaterial` builds one `StandardMaterial3D` (shared across
    all chunks) with a 128² procedurally-drawn black-grid-on-silver texture,
    `Metallic=1.0`, `Roughness=0.18`. UVs on chunk meshes are set in
    world/cell units so the grid tiles exactly once per terrain cell and
    aligns across chunk boundaries.
- `docs/logs/2026-04-12-test-drive-infinite-terrain.md` — this log.

## Architectural decisions

- **Mode selected via `GameState`, not a new scene.** TestDrive reuses
  `Main.tscn` so HUD, pause menu, network setup etc. are unchanged. The
  terrain node decides its own strategy at `_Ready` time.
- **No edge barriers in infinite mode.** The streaming radius is always larger
  than the tank's draw distance, so there's no hard edge to wall off.
- **Shared heightfield function between mesh + collision.** Same `SampleHeight`
  feeds both the `ArrayMesh` and the `HeightMapShape3D.MapData`, matching the
  standard-mode pipeline's "one pass, two outputs" pattern.
- **Material is shared, not per-chunk.** One `StandardMaterial3D` / one
  `ImageTexture` for the whole world; chunks reference it via
  `SetSurfaceOverrideMaterial` so only one GPU texture upload happens.
