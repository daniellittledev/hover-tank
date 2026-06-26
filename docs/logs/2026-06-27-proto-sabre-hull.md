# 2026-06-27 — Proto-Sabre hull: procedural rebuild, shiny material, raised camera

**Branch:** main

Replaced the tank hull with the **Proto-Sabre base platform**, built procedurally from a supplied build spec. Earlier same-day attempts (a smooth superellipse loft, a faceted triangular dart) were discarded in favour of the spec's layered-silhouette method. Also reworked the hull material to a sky-reflecting metal and raised the TestDrive chase camera. Iterated throughout against the `godot-screenshots` capture workflow.

## Hull construction (`TankMeshBuilder.cs`)

Rebuilt as a **layered-silhouette loft** (per the Proto-Sabre spec): one symmetric 2D silhouette polygon (`Base`, a 10-gon = core hex + rear "fan" hex, with concave side notches) stamped as four horizontal rings at increasing height (`bottom 0.88`, `base 1.0`, `rim 1.0`, `platform 0.42 @ centerZ 0.230`), bridged into walls and capped. Engine glow is an unlit cyan quad on the rear of the rim band.

Key decisions:
- **Symmetry is structural**: the silhouette carries both halves, so the hull can't come out lopsided — far more reliable than hand-placing vertices.
- **Faceted hard-surface read**: non-indexed triangles with flat per-face normals.
- **Forward axis**: the spec uses +Z = nose, but the project moves along **-Z** (barrel/turret fire -Z). `ToWorld` flips forward with a **180° rotation about Y** (negate X and Z), a *proper* rotation that preserves winding — a plain Z-reflection would invert every face. X-symmetry makes the X negation invisible.
- **Winding**: Godot's front face is **clockwise as seen from outside** (winding's right-hand normal points inward). `AddTri` emits the order whose RH normal is inward and stores the outward normal for lighting, so single-sided culling shows the correct outer faces. (Symptom when this was wrong: only back-faces visible / inside-out hull.)
- **Outward orientation**: each ring band/cap passes a `vBias`/`radialW` hint (belly = down+out, rim band = radial, deck ramp = up, caps = ±Y) so every face is oriented outward reliably — a single global-centroid test was unreliable for the flat, open-deck shape.
- **Single-sided**: dropped the spec's suggested double-sided material once winding was correct — it was only insurance against inconsistent winding.
- `Scale = 2.6` fits the tank's physics footprint; all spec values are constants at the top of the file. The dorsal cannon is omitted (mounts on the platform top later).

The hull is an **open recessed shell** by design (no top cap over the core; the cannon platform sits in the recess) — looking straight down shows the interior, but no gameplay angle does.

## Material

Light, **shiny metal that reflects the sky**: `StandardMaterial3D`, `albedo (0.78,0.82,0.90)`, `metallic 0.5`, `roughness 0.30`. Reflections come from the scene sky automatically (upward facets catch the dusk sky, lower ones the terrain) for a blurred satin look. Replaced the earlier near-black charcoal, which read as a dark blob in the dusk scene.

## Camera (`FollowCamera.cs`, `HoverTank.ApplyTestDriveFeel`)

Added `FollowCamera.SetOrbitPitch(float)`. TestDrive now starts from a **raised, looking-down vantage**: orbit pitch ~0.72 rad (~41° down, was ~23°), `PitchMax` lifted to 1.0 to allow it, orbit centre raised to 1.4. The player can still free-look; this just sets the default. Turret/thruster orb stay hidden in TestDrive for the clean Proto-Sabre silhouette.

## Tooling

- New project skill **`.claude/skills/godot-screenshots`** documenting the build → render → view loop (committed separately, `591ac7a`).
- New debug harness **`tools/HullPreview.tscn`**: renders the hull alone from TOP/FRONT/LEFT/ANGLE in a 2×2 grid — the key tool for diagnosing shape, winding, and normals in isolation.

## Notes / follow-ups

- Mount the dorsal cannon on the platform top.
- Material reads cool because the dusk scene is dim; lower `metallic` toward diffuse or `roughness` for a sharper mirror if a brighter craft is wanted.
- Combat/MP modes still use the baseline look; this art pass is TestDrive-focused.
