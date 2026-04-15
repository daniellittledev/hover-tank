# 2026-04-15 — TestDrive: matte material, smooth dunes only

**Branch:** `claude/smooth-terrain-mesh-SgWNb-round2`

## Summary

Two persistent complaints in TestDrive after the previous round of fixes:
the terrain still looked spiky, and the silver grid still produced
blinding specular highlights. Pull both problems out at the root rather
than tuning further.

- **Material → fully matte.** `CreatePanelGridMaterial` (renamed from
  `CreateMetallicGridMaterial`): `Metallic = 0`, `Roughness = 0.85`,
  dropped `MetallicSpecular`. Diffuse-only — no specular hotspot can
  exist regardless of light intensity.
- **Terrain → rolling dunes only.** Removed the entire jump-noise layer
  (`_jumpNoise`, `InfiniteJumpScale` export, the `j > 0 ? j*j*scale : 0`
  asymmetric-bump term in `SampleHeight`). Hill-noise octaves dropped
  4 → 3 and gain 0.40 → 0.35 to suppress the high-frequency ridges that
  caused per-triangle visual spikes.

## Why

- `Metallic = 1` on a near-white albedo means the surface reflects all
  incident light as colored specular. Lowering `MetallicSpecular` and
  bumping `Roughness` only widens the highlight — it can't make a
  metal not-shiny. The user wanted matte; matte means `Metallic = 0`.
- Jumps were *designed* to be sharp ("mounds that launch the tank at
  speed"). The user explicitly asked for "smooth rolling dunes" — that
  feature directly contradicts the new requirement, so it goes.
- High-octave fractal noise adds metre-scale ripples that fall *between*
  vertices (CellSize = 2 m), which produces the visible per-quad
  faceting even with smoothed normals. Trimming the upper octaves
  removes that spatial frequency entirely.

## Architectural notes

- `_jumpNoise` and `InfiniteJumpScale` are deleted, not stubbed. If
  jumps come back as a gameplay feature later, they belong in their own
  system (e.g. a deliberate ramp prefab) rather than as a noise layer
  that everyone has to drive over by accident.
- Method renamed `CreateMetallicGridMaterial` → `CreatePanelGridMaterial`
  to keep the name truthful — it's no longer metallic.
