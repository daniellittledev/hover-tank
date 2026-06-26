# 2026-06-26 — Visual polish pass (lighting, terrain, hull material)

**Branch:** main

A low-risk, asset-free pass to lift the overall look, prompted by a "how do you make your game look good?" review. Three independent changes, each in its own commit so they can be reverted individually.

## Changes

- **Baseline lighting + colour grade (all modes).** `GameSetup.ApplyBaselineVisuals()` (called unconditionally in `_Ready`) applies on top of `Main.tscn`'s environment: a gentle split-tone grade (teal shadows / warm highlights via the existing `MakeSplitToneLut`), +contrast/+saturation, a slightly warmer + lower key sun for longer raking shadows, and a dim cool **shadowless fill light** from behind-opposite the sun (key/fill separation — the biggest readability win for a one-sun scene).
- **Procedural standard-terrain shader.** `TerrainGenerator.CreateStandardTerrainMaterial()` replaces the flat `StandardMaterial3D` on the combat/MP cratered terrain. Blends colour by **slope** (lighter dust on flats, darker rock on steep crater walls) and **height** (lighter crests), varies roughness with slope, and adds a faint large-scale value-noise mottle so the ground isn't one flat tone. World normal/pos from the vertex stage; asset-free.
- **Restored hull material.** `TankMeshBuilder` now assigns a dark metallic-charcoal `MaterialOverride` in `_Ready`. The scene's format-4 reserialization had dropped the `Mat_hull`/`Mat_accent` overrides and the builder assigned none, so the hull had been rendering with Godot's flat-white default material — a regression. Charcoal kept low-saturation so the orange thruster glow stays the lone warm accent.

## Follow-up: shadows + atmosphere + AA

Prompted by the Reddit comment's emphasis on shadows and fog:

- **Soft, higher-res shadows.** `project.godot`: 4x MSAA (`msaa_3d=2`), directional shadow `size=4096`, and `soft_shadow_filter_quality=3` for both directional and positional shadows. The sun gets `LightAngularDistance = 0.6` for a contact-hardening soft penumbra instead of a hard uniform edge.
- **Subtle fog for depth.** In `ApplyBaselineVisuals`: distance fog density `0.003 → 0.005`, aerial perspective `0.6 → 0.7`, plus a shallow ground mist (`FogHeight = 2`, `FogHeightDensity = 0.04`) pooling in craters. Kept subtle — overdone fog is what hazed the dream atmosphere out to white.

## Root-caused the whiteout (via rendered screenshots)

Ran Godot 4.6.2 (mono, at `D:\Godot_v4.6.2-stable_mono_win64`) with a throwaway capture harness (`tools/Screenshot.*`) and confirmed the actual cause of the long-standing full-screen whiteout — it reproduced in **StandardWaves too**, so it was never the dream environment:

- **`MakeSplitToneLut()` was the culprit.** Its gradient ran `(0.86,0.97,1.02)`→`(1.04,0.98,0.88)`, so as an `AdjustmentColorCorrection` curve it mapped **black up to ~0.9**, lifting the whole image to near-white. Fixed to span true black→white (`(0.02,0.03,0.05)`→`(1.00,0.97,0.90)`) with only subtle tints. This LUT was reused from the dream code, so it was almost certainly the original whiteout in both modes — the earlier "disable the dream env" bisection masked it rather than fixing it.
- **Spawn-ordering bug.** `NetworkManager` set `tank.GlobalPosition` *before* `AddChild`, which errors (`!is_inside_tree()`) and drops the value, so tanks spawned at the origin (and could end up under terrain). Now set after `AddChild` at all three spawn sites — the `HeightAt` spawn fix actually takes effect.

Verified by screenshot: both modes render correctly, tank spawns on the surface, TestDrive shows subtle teal crest glow only on the highest distant ridges.

## Not done (needs visual iteration)

- **Hover dust** under the craft and **camera shake** juice were deferred — particle look/scale and shake magnitude need in-editor eyeballing to tune, which can't be done from a headless build. To add next with feedback.

## Notes

- The TestDrive "dream" atmosphere remains gated off (`DreamAtmosphereEnabled = false`) from the earlier whiteout fix; the new baseline grade applies there in the meantime.
