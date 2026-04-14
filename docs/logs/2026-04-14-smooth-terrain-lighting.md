# 2026-04-14 — Smooth terrain lighting

**Branch:** `claude/smooth-terrain-lighting-h1jmS`

## Changes

- **Terrain shape — standard mode**: lowered noise frequency 0.025→0.010, reduced FractalGain 0.5→0.40, increased HeightScale default 4→12. Hills are now 3× taller and ~2.5× wider.
- **Terrain shape — TestDrive infinite mode**: lowered hill noise frequency 0.012→0.007, reduced FractalGain 0.5→0.40; lowered jump noise frequency 0.05→0.025; increased InfiniteHillScale default 10→22, reduced InfiniteJumpScale default 6→3. Rolling hills dominate; jump bumps are fewer and smaller.
- **Triangle-seam artefact (standard terrain)**: added a 2-pass 3×3 box-blur (`SmoothHeights`) applied to the height array before mesh construction. Reduces planarity deviation within each quad, which damps the lighting discontinuity along the quad diagonal.
- **Triangle-seam artefact (both meshes)**: replaced the fixed TR–BL quad diagonal with a per-quad heuristic that chooses the diagonal whose two endpoint heights differ least. This makes quads as planar as possible and breaks up the visible uniform-diagonal-line pattern under specular light.
- **Normal stencil (TestDrive chunks)**: widened central-difference stencil from ±1 cell to ±2 cells (denominator updated 2·CellSize→4·CellSize). Larger stencil averages out high-frequency noise, producing smoother normals across chunk boundaries.
- **Specular highlights (TestDrive material)**: reduced `MetallicSpecular` 1.0→0.5 and increased `Roughness` 0.18→0.38. Specular lobes are broader and dimmer; metallic look is retained.

## Architectural notes

- `SmoothHeights` is called before `CarveCraters` so crater rims remain crisp.
- The diagonal-flip heuristic is O(1) per quad; no measurable overhead at build time.
- All changes are in `scripts/TerrainGenerator.cs`; no scene files modified.
