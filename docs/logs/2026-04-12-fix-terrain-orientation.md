# 2026-04-12 Fix Terrain Orientation & Visual Polish

**Branch:** `claude/fix-terrain-orientation-Blb3L`

## Changes

- Fixed terrain mesh rendering upside-down by reversing triangle winding order from CW to CCW and swapping normal cross product operands (`dz.Cross(dx)` to `dx.Cross(dz)`) so normals point upward
- Added invisible collision barriers (4 StaticBody3D walls) around the terrain perimeter to prevent the tank from driving off the map edge
- Reduced environment glow intensity by 75% (`glow_intensity` 1.0 to 0.25, `glow_bloom` 0.2 to 0.05)
- Removed the GlowDisc MeshInstance3D (floating blue rectangle) from the hover glow system; the 4 OmniLight3D nodes remain to illuminate the ground beneath the tank
- Terrain lighting now works correctly as a result of the normal fix (StandardMaterial3D was already configured for PBR lighting)

## Architectural Decisions

- Edge barriers are collision-only (no mesh) to remain invisible; 20m tall, 2m thick, Y-offset lowered by `CraterDepth` to seal below crater floors
- Hover glow effect relies solely on OmniLight3D nodes rather than an emissive mesh, avoiding the visible floating geometry issue
