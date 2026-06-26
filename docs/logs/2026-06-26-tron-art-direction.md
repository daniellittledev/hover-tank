# 2026-06-26 — TestDrive Tron art direction (teal / orange)

**Branch:** main

Reworked the TestDrive look toward a vibrant teal/orange "Tron" art style (per a reference wallpaper + "modern Disney Tron" brief), now that the whiteout (the colour-correction LUT) is fixed and visuals can be verified by rendered screenshots.

## Changes

- **Tron terrain shader** (`TerrainGenerator.CreateTronTerrainMaterial`, replaces `CreateDreamTerrainMaterial`). Near-black teal base lit only by emissives: a world-space neon cyan grid (Tron panel lines that stay fixed as the craft moves), teal crest rims (height ramp + fresnel), and orange "energy" pooling in the deep troughs. The dark base + HDR-thresholded bloom yields the high-contrast teal/orange neon look.
- **Tron atmosphere** (`GameSetup.ApplyTronAtmosphere`, replaces the disabled `ApplyDreamAtmosphere` + its export flag). Near-black teal sky/ambient, ACES exposure 1.0, additive bloom with `GlowHdrThreshold = 1.0` so only emissives glow, thin teal depth fog, punchy saturation, and a teal-shadow/orange-highlight grade (`MakeTealOrangeLut`, endpoints spanning true black→white so it grades without lifting). Dim cool key sun just for dune form.
- **Glowing hull** (`TankMeshBuilder.CreateHullMaterial` → ShaderMaterial). Near-black metallic body with a thin cyan fresnel rim (`pow(...,6)`, energy 1.3) so the craft reads as a Tron vehicle outlined in neon, without blooming to a white blob. Tuned down from an initial broad/energetic rim that washed the small hull out.

## Notes

- Iterated with the screenshot harness ([[godot-visual-capture-workflow]]): grid/orange intensities and the rim sharpness were dialled in against rendered frames.
- Combat/MP modes keep the Main.tscn baseline look — Tron applies to TestDrive only so far. Could extend if wanted.
- Possible follow-ups: faction-coloured hull rims (cyan player / orange enemy) for readability; hover dust + camera shake (still deferred per user).
