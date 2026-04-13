# 2026-04-13 — Improve Game Visuals

**Branch:** `claude/improve-game-visuals-AebtZ`

## Changes

- **Sky fix**: ProceduralSkyMaterial colours were too dark; ACES tonemapping crushed them to black. Raised sky_top and horizon luminance to a visible overcast grey-blue. Reduced sun energy (1.3→0.85) and tonemap exposure (1.1→0.9) to narrow the scene dynamic range. Boosted ambient fill (0.5→0.9).
- **Grid lines**: Terrain grid line width reduced from 8px to 2px; colour changed from pure black to dark grey `(0.20, 0.22, 0.25)`. Removes wireframe look, produces subtle panel-seam appearance.
- **Atmospheric fog**: Added distance fog to WorldEnvironment (`density=0.003`, `aerial_perspective=0.6`, colour matches sky horizon). Hides terrain edge and creates depth.
- **SSAO intensity**: Reduced from 2.0 to 1.0 — was darkening grid crevices into heavy black trenches.
- **Glow bloom**: Reduced from 0.05 to 0.02 — reduces bleed from point lights.
- **Tank lights**: Removed red/green nav lights (NavLightPort, NavLightStarboard). Replaced 4 corner orange hover lights (3.5 energy each) with a single centred cool-white OmniLight3D (2.5 energy, range 4.5). Reduces visual noise from 7 point lights to 2.

## Architectural decisions

None — all changes are purely visual parameters, no new nodes or scripts introduced.
