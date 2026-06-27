# TestDrive arena v2 — detail, variation & chunk-LOD

**Date:** 2026-06-27
**Branch:** main

## Summary

Reworked the TestDrive arena to add a lot more detail and variation around the figure-8, with a chunk-LOD render so the detail stays affordable. Same playable footprint as v1 (refining feel, not scaling up).

- **`scripts/TrackArena.cs` (rewrite)** — the height field is now feature-rich and fully analytic:
  - **Rounded ∞ channel** via *two overlapping circles* (radius 56, centres ±48) instead of the lemniscate — round lobes that cross at the origin. Channel distance is analytic (`|‖p−c‖−R|`), which also removed the old 720-point nearest-point search.
  - **Detail & variation**: domain-warped macro undulation + **ridged (eroded) noise** + micro roughness; **craters** scattered off the racing line (reusing the bowl/rim profile from the standard crater carver); a few hand-placed **ramps/kickers** along the loops for air; **roughened channel rims**.
  - **Background mountains** as a radial band beyond the boundary (backdrop, not playfield).
- **`scripts/TerrainGenerator.cs`** — `BuildTrackArena` replaced the v1 two-tier inset with a **chunked-LOD renderer**:
  - Grid of 30 m chunk `Node3D`s. Reachable chunks carry **3 prebuilt LOD meshes** (1/2/4 m cells) swapped by camera distance via Godot **`VisibilityRangeBegin/End` + `FadeMode.Self`**; far backdrop chunks get a single coarse mesh.
  - **Downward perimeter skirts** (double-sided) hide cracks where neighbouring chunks sit at different LODs.
  - **Fixed-epsilon analytic normals** (sampled from the field, independent of cell size) → smooth shading with **no visible triangle facets**, consistent across LOD transitions.
  - **Collision** = one uniform `HeightMapShape3D` (1.25 m) over the reachable disc — captures channel/craters/ramps; no LOD (the tank only touches nearby terrain).
  - **Circular boundary** = a ring of 64 tangent box walls, replacing the square edge barriers.
- **`tools/Screenshot.cs`** — `overhead` capture now also disables fog for a clean top-down layout view.
- **`CLAUDE.md`** — dropped the "no baked assets" rule (baked heightmaps now permitted via `CustomMapPath`); documented the TestDrive source → chunk-LOD-render + uniform-collision architecture.

## Decisions

- **Analytic source ⇒ LOD by resampling, never decimation/bake.** Because terrain is a function, a low LOD is just the function sampled coarser; "more detail" = sample finer. This sidesteps mesh-simplification tooling entirely.
- **Normals decoupled from mesh density.** Fixed-epsilon central difference of the field gives smooth normals at any LOD and reduces popping at LOD seams — the main lever for "no triangle edges."
- **Render LOD ≠ collision LOD.** Collision stays uniform and feature-accurate; only rendering uses distance LOD.
- **Phase 1 stays runtime-procedural.** Build is sub-second, so no bake needed yet. The offline erosion-sim bake (writing a float32 map loaded via `CustomMapPath`) is deferred to Phase 2, once driving feel is dialled in.

## Verification

`dotnet build` clean (one pre-existing unrelated warning). Captured overhead (round ∞, craters, erosion ridges, mountain backdrop, circular extent) and in-tank (rich varied terrain, smooth shading, no facets) via `godot-screenshots`.

## Knobs for tuning feel (next)

`TrackArena`: `CircleR/CircleC` (loop size), channel profile consts, crater count/size, the `BuildRamps` list, noise amplitudes/frequencies, `MtnMax`. `TerrainGenerator`: `LodBands` distances/cells, `ChunkSize`, `ChunkSkirt`, collision cell, `BoundaryRadius`.
