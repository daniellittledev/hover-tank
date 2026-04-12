# 2026-04-12 — Update Tank Appearance

**Branch:** `claude/update-tank-appearance-tCyz3`

## Changes

- Redesigned `TankMeshBuilder.cs` with an angular stealth-fighter style hull:
  - Arrow/wedge silhouette with wide swept-back wings (max half-width 1.55 at wing tips)
  - Beveled top surface with raised centre ridge and shoulder crease for angular panel breaks
  - Two mesh surfaces: surface 0 (dark charcoal body), surface 1 (orange accent panels on outer wing bevels)
  - Angular cockpit canopy with three cross-sections (front, peak, rear) built on the centre ridge
  - 8 cross-section ribs define the hull profile from nose (-1.75 Z) to rear (1.60 Z)

- Updated `HoverTank.tscn` to match the new hull:
  - Materials: dark charcoal hull (0.12, 0.12, 0.14), orange accents (0.92, 0.55, 0.08), dark turret, orange thruster glow
  - Collision box widened to 2.4 x 0.40 x 3.4
  - Hover ray grid widened: X = +/-1.1, Z = {-1.4, 0, 1.3}
  - Hover glow lights repositioned and recoloured from blue to warm orange
  - Added port (red) and starboard (green) navigation lights at wing tips
  - Turret made low-profile (0.40 x 0.08 x 0.50), barrel lengthened to 1.3
  - Thruster repositioned to new rear
  - Weapon mount positions adjusted for wider hull

## Architectural decisions

- Hull mesh uses rib-based cross-section system with shoulder split for angular panel detail, rather than a simple polygon extrusion. This makes it straightforward to tweak proportions by adjusting rib arrays.
- Orange accents assigned to outer top bevels (shoulder-to-edge) while inner bevels (centre-to-shoulder) stay dark, creating the two-tone panel-break pattern from the reference.
- Cockpit canopy is part of the hull mesh (surface 0), not a child of the Turret node, so it stays fixed relative to the hull while the turret/barrel rotate independently.
