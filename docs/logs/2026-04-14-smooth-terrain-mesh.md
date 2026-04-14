# 2026-04-14 — Smooth terrain mesh

**Branch:** `claude/smooth-terrain-mesh-SgWNb`

## Summary

- Added a 3×3 box-blur smoothing pass (`SmoothHeights`) in
  `scripts/TerrainGenerator.cs`, applied to the base height array after
  PNG/noise sampling and before crater carving.
- New `[Export] int SmoothingIterations = 3` controls how many blur passes
  run. Runs only in standard (finite) terrain mode.
- Collision data (`HeightMapShape3D.MapData`) is built from the smoothed
  heights, so physics matches the visible mesh.

## Why

The 8-bit `terrain/heightmap.png` quantises elevation into 256 steps. With
`HeightScale = 4`, adjacent vertices can sit on identical plateau heights
with sharp breaks between them, producing visible triangle faceting on the
rendered mesh. Averaging each vertex with its 3×3 neighbourhood removes
the stair-step artefact while crater rims stay crisp because craters are
carved *after* smoothing.

## Architectural notes

- Smoothing runs before crater carving by design: the crater formula is
  already smooth, and blurring afterwards would soften rims.
- Infinite TestDrive mode samples continuous `FastNoiseLite` directly and
  is already smooth — no blur applied there.
- `SmoothHeights` clamps neighbour indices at the grid edge so the
  perimeter height is preserved, which keeps the edge barrier walls
  aligned with the mesh boundary.
