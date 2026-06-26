# 2026-06-26 — Visual polish pass (lighting, terrain, hull material)

**Branch:** main

A low-risk, asset-free pass to lift the overall look, prompted by a "how do you make your game look good?" review. Three independent changes, each in its own commit so they can be reverted individually.

## Changes

- **Baseline lighting + colour grade (all modes).** `GameSetup.ApplyBaselineVisuals()` (called unconditionally in `_Ready`) applies on top of `Main.tscn`'s environment: a gentle split-tone grade (teal shadows / warm highlights via the existing `MakeSplitToneLut`), +contrast/+saturation, a slightly warmer + lower key sun for longer raking shadows, and a dim cool **shadowless fill light** from behind-opposite the sun (key/fill separation — the biggest readability win for a one-sun scene).
- **Procedural standard-terrain shader.** `TerrainGenerator.CreateStandardTerrainMaterial()` replaces the flat `StandardMaterial3D` on the combat/MP cratered terrain. Blends colour by **slope** (lighter dust on flats, darker rock on steep crater walls) and **height** (lighter crests), varies roughness with slope, and adds a faint large-scale value-noise mottle so the ground isn't one flat tone. World normal/pos from the vertex stage; asset-free.
- **Restored hull material.** `TankMeshBuilder` now assigns a dark metallic-charcoal `MaterialOverride` in `_Ready`. The scene's format-4 reserialization had dropped the `Mat_hull`/`Mat_accent` overrides and the builder assigned none, so the hull had been rendering with Godot's flat-white default material — a regression. Charcoal kept low-saturation so the orange thruster glow stays the lone warm accent.

## Not done (needs visual iteration)

- **Hover dust** under the craft and **camera shake** juice were deferred — particle look/scale and shake magnitude need in-editor eyeballing to tune, which can't be done from a headless build. To add next with feedback.

## Notes

- The TestDrive "dream" atmosphere remains gated off (`DreamAtmosphereEnabled = false`) from the earlier whiteout fix; the new baseline grade applies there in the meantime.
