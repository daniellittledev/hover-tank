# 2026-06-27 — Terrain multi-view capture harness

**Branch:** `worktree-bridge-cse_01WhoTNcxAERL7E1moxgvxxH`

## Summary

- Added `tools/TerrainShots.cs` + `tools/TerrainShots.tscn`: a throwaway visual-capture harness that instances `Main.tscn` in TestDrive, lets the world settle (~240 frames), then flies a single free `Camera3D` to three viewpoints over the terrain and saves a PNG of each:
  - `terrain-shot-1.png` — high aerial overview (~70 m, angled down)
  - `terrain-shot-2.png` — low ground-skim across the dunes (~4 m, eye-level)
  - `terrain-shot-3.png` — wide mid-altitude from the opposite corner (~32 m)
- Camera altitudes are derived per-viewpoint from `TerrainGenerator.HeightAt(x, z)` (terrain found via the `terrain` group) so the camera clears the dune crests instead of clipping through them.
- Added `tools/terrain-shot-*.png` to `.gitignore`, matching the existing convention for the `shot-*.png` / `hull-preview.png` capture outputs.

## Notes

- Mirrors the existing `tools/Screenshot.cs` harness but captures multiple poses in one run by stepping a frame-counter state machine between shots (5 settle frames per move).
- The harness installs its own camera and calls `MakeCurrent()`; because the tank's camera only asserts `current` once (not per-frame), the free camera holds. Bypassing the tank camera produces benign per-frame exceptions in the log, but all captures save cleanly.
- Dev-only harness, not shipped. Output PNGs are gitignored.
