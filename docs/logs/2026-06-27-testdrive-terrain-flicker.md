# 2026-06-27 — Fix TestDrive terrain skirt flicker & visual artifacts

**Branch:** worktree-bridge-cse_019eoornKMj5ssjwrCujXKS5

## Problem

In TestDrive mode the terrain skirts flickered and the surface showed crawling/moiré artifacts in motion.

Root causes identified in `TerrainGenerator.cs` / the dune shader:

1. **LOD cross-fade dithering** — each reachable chunk stacked 2–3 LOD `MeshInstance3D`s with `VisibilityRangeFadeMode.Self`. The fade margins render two LODs at once with alpha-hash dithering → screen-door flicker as the camera moves.
2. **Inter-LOD skirt flicker** — the near and mid LOD of the *same* chunk sample each edge at different cell sizes, so their downward skirts sit at different depths. During a fade both are partially visible and z-fight → the visible "skirt flickering."
3. **Grid-shader moiré** — the panel-grid used `fract(world_pos.xz/period)` with a fixed line width and no derivative AA, so lines aliased/crawled at distance and on grazing mountain slopes.

## Changes

- **Removed camera-distance LOD entirely** (`BuildChunkedTerrain`). The reachable disc is small (~165 m), so uniform fine chunks are cheap (~60k verts total). Each chunk is now a single `MeshInstance3D`: fine `ReachableCell = 1.25 m` over the reachable disc, coarse `FarCell = 4 m` for backdrop mountains. Dropped the `LodBands` table, `VisibilityRange*` properties, and the per-chunk `Node3D` wrapper.
- **Skirts kept but no longer flicker.** Uniform-resolution neighbours sample identical shared-edge world positions → exact edge match, no cracks; their coplanar, identically shaded skirts overlap invisibly. Skirts now only matter at the fine↔coarse ring seam.
- **Antialiased the panel-grid shader** (`CreateDuneTerrainMaterial`): line thickness measured in pixels via `fwidth`, plus a distance fade that drops the grid where cells shrink below ~a couple of pixels. Kills the moiré/crawl.
- Updated CLAUDE.md TestDrive terrain-pipeline note and the in-code doc comments to match.

## Decision

LOD was not worth its artifacts at this arena scale. The analytic field already gives smooth normals at any cell size, and uniform chunks self-match at edges — so dropping LOD removes the entire class of cross-fade/skirt flicker for ~60k verts of fixed cost. Detail still comes only from sampling the field finer; never decimate or stitch.

## Verification

Built clean (`dotnet build`, 0 errors) and rendered `tools/shot-testdrive.png`: distant mountains no longer carry crawling grid lines, near grid is softly antialiased, terrain reads smooth. (Temporal flicker fix is structural — the dithered multi-LOD render is gone — so a still can't show it directly.)
