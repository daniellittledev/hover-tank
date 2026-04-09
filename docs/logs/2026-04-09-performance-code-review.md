# 2026-04-09 — Performance Code Review

**Branch:** `claude/performance-code-review-hw3cd`

## Changes

- **Projectile.cs** — Cache `Godot.Collections.Array<Rid>` exclude list in `_Ready()` instead of allocating a new instance every physics frame per active projectile. Eliminates GC pressure during sustained minigun fire (was allocating ~40 objects/sec at 20 rounds/sec × 2 barrels).

- **FollowCamera.cs** — Cache `PhysicsRayQueryParameters3D` in `_Ready()`, updating only `From`/`To` each frame. Eliminates one heap allocation per rendered frame for the aim raycast.

- **TerrainGenerator.cs (crater carving)** — Pre-compute world-space X/Z coordinate arrays once before the crater loop. Use squared-distance comparison to skip `MathF.Sqrt` for vertices outside the crater radius. Reduces startup cost from ~250K `sqrt` calls to only those vertices that actually fall inside a crater (typically <10% of the grid per crater).

- **TerrainGenerator.cs (normal calculation)** — Cache `z * verts`, `(z+1) * verts`, and `(z-1) * verts` row offsets outside the inner `x` loop, eliminating ~3× redundant multiplications per vertex across 10K vertices.

- **HoverTank.cs** — Expand `(LinearVelocity + AngularVelocity.Cross(r)).Dot(Vector3.Up)` to only compute the Y component: `LinearVelocity.Y + AngularVelocity.Z * r.X - AngularVelocity.X * r.Z`. Avoids constructing the intermediate sum vector and the full dot product (saves ~4 muls + 4 adds), called 4× per physics tick at 60 Hz.

## Architectural decisions

- No behaviour changes; all optimisations are mathematically equivalent to the original code.
- The `_excludeRids` array in `Projectile` is intentionally not `readonly` — it is built in `_Ready()` after `OwnerRid` is set.
- `PhysicsRayQueryParameters3D` mutation (setting `From`/`To` each frame) is safe; the object is not shared across frames.
