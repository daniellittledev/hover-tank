# 2026-04-14 — Remove PNG heightmap, go procedural for finite mode

**Branch:** `claude/smooth-terrain-mesh-SgWNb`

## Summary

- Deleted `terrain/heightmap.png` and `terrain/heightmap.png.import`.
- Removed the PNG loader (`TryLoadHeightmapImage`, `SampleImageHeights`)
  and `HeightmapPath` export from `scripts/TerrainGenerator.cs`.
- Finite (StandardWaves / multiplayer / split-screen) terrain now always
  starts from `FastNoiseLite` fractal noise, then carves craters on top.
- Removed the 3×3 box-blur smoothing pass and `SmoothingIterations`
  export added earlier the same day — no longer needed now that heights
  come from continuous-valued noise or float32 files.
- Added `CustomMapPath` export + `TryLoadCustomHeightmap` method as a
  hook for future hand-authored campaign maps. Format: packed
  little-endian float32, row-major, exactly `(GridSize+1)²` values, no
  header, heights in world metres. Empty path → noise fallback.
- Updated `README.md` "Customising Terrain" section and `CLAUDE.md` key
  files table.

## Why

The 8-bit PNG quantised heights into 256 steps, producing visible
triangle faceting on the mesh. Earlier in the day we added a smoothing
pass as a band-aid. This change pulls the tooth instead: procedural
noise is already continuous, and the new float32 custom-map format
carries full precision end-to-end — neither needs the smoothing step.

## Architectural notes

- TestDrive was already procedural (chunk-streamed infinite noise) and
  never used the PNG — unchanged.
- `CustomMapPath` is deliberately a raw float array rather than EXR/TIFF
  to keep the loader branch-free and dependency-free. When campaign maps
  arrive we'll write a small authoring tool that dumps a `float[]` from
  whatever source art we settle on.
- Heights in the custom-map file are in **world metres** (not scaled by
  `HeightScale`). This lets map authors hit exact heights without having
  to reason about the scale multiplier.
