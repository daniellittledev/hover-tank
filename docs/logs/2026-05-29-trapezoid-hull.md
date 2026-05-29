# 2026-05-29 — Trapezoid hull mesh

Branch: main

## Changes

- Replaced the procedural stealth-fighter hull in `TankMeshBuilder.cs` with a long trapezoidal prism: narrow + low at the front (-Z), wide + tall at the back (+Z), flat bottom.
- Removed the multi-rib hull loop, the nose tip, the orange accent surface, and the cockpit dome. The hull is now a single dark-charcoal surface (surface 0).

## Notes

- Dimensions: Z from -1.70 (front) to 1.60 (back); half-width 0.55 front → 1.20 back; top 0.10 front → 0.46 back; flat bottom at -0.14.
- `scenes/HoverTank.tscn` still sets `surface_material_override/1` (orange accent). Surface 1 no longer exists, so that override is now inert — left in place rather than touched.
- Turret, barrel, thruster, collision box, and hover raycasts are unchanged.
