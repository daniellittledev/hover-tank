# TestDrive track arena — figure-8 channel map

**Date:** 2026-06-27
**Branch:** main

## Summary

Replaced the TestDrive terrain (previously infinite chunk-streamed dunes) with a finite, authored "track arena":

- **New `scripts/TrackArena.cs`** — a plain (non-Node) generator owning the arena's analytic height field. Composes: gentle ground noise, a **sunken figure-8 channel** (lemniscate of Gerono, loops along X, crossing at the origin) with smooth banked walls, a **raised berm lip** at each channel rim (bobsled feel), and **mountains** rising in a square band near the arena edge. The loop crossing sits at (0,0) so the player — which spawns there — drops into the central intersection.
- **`scripts/TerrainGenerator.cs`** — TestDrive now routes to a new `BuildTrackArena()` instead of `SetupInfiniteTerrain()`. `HeightAt` delegates to `TrackArena.SampleHeight` in track mode (keeps `NetworkManager.SpawnPoint` probing correct). Added a general `BuildGridMesh(...)` helper (shared by the fine inset and coarse ring) and `SnapFineBoundary(...)` for a watertight LOD seam. Removed the now-dead infinite-streaming code (`SetupInfiniteTerrain`, `UpdateStreaming`, `BuildChunk`/`BuildChunkMesh`, `_Process` streaming, chunk fields, and the `InfiniteHillScale`/`CrestGlow*`/`Chunk*` exports). Kept `CreateDuneTerrainMaterial` (reused for the arena).
- **`tools/Screenshot.cs`** (dev harness) — added an `overhead` arg: top-down orthographic capture framing the whole arena, with fog disabled, for verifying terrain layout.

## Design decisions

- **Two-tier render, uniform-fine collision.** Render is split into a fine 2 m central inset (over the figure-8, where detail matters) and a coarse 6 m outer ring (carrying the mountains, whose craggy silhouette hides the lower tessellation). A single `HeightMapShape3D` can only be one resolution, so collision is baked uniformly at the fine 2 m cell over the whole arena — cheap (physics only narrow-phases cells near the body) and guarantees the channel floor and berms are collidable everywhere, avoiding the overlap pitfalls of a two-resolution collision.
- **Watertight seam by colinear snapping.** Coarse cell = 3× fine cell and the inset boundary lands on coarse grid lines, so every coarse boundary vertex coincides with a fine vertex. `SnapFineBoundary` forces the in-between fine perimeter vertices onto the straight coarse edge (linear interp of the bracketing aligned heights), making the two meshes share a colinear seam — no T-junction cracks. (Verified seamless in capture.)
- **Channel + berm profile.** Inside `ChannelHalfWidth` the floor is flat at `ChannelFloor` (−4 m); it ramps back to ground across `WallWidth`; a gaussian berm (`+2.5 m`) is centred on the wall top for the banked lip. Channel/berm math is gated to a bounding box around the figure-8 so far/mountain vertices skip the nearest-point search.
- **Arena scale ~400 m** (`±204` half-size), figure-8 ≈176×112 m including banks, mountains ramp from 150 m out to the rim (entirely within the coarse ring).

## Verification

`dotnet build` clean (one pre-existing unrelated warning). Captured overhead (figure-8 shape, berms, mountain ring, no seam) and in-tank (tank spawns in the channel intersection, mountains on the horizon) via the `godot-screenshots` workflow.
