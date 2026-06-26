# 2026-06-26 — TestDrive dusk art direction (replaces Tron)

**Branch:** main

Pivoted the TestDrive look away from the dark teal/orange "Tron" attempt (user didn't like it) toward a soft, atmospheric reference: pastel sunset sky, distant dunes fading to a teal-blue haze, dark blue terrain, and a clean blue craft. Iterated against rendered screenshots.

## Changes

- **Dusk atmosphere** (`GameSetup.ApplyDuskAtmosphere`, replaces `ApplyTronAtmosphere` + `MakeTealOrangeLut`). Pastel sunset sky (lavender zenith → warm orange horizon), bright sky ambient, gentle bloom, and teal-blue **aerial fog** with low aerial-perspective so the distance fades teal while the sky keeps its orange — the two things the user asked for. Warm low sunset key sun.
- **Dune terrain shader** (`TerrainGenerator.CreateDuneTerrainMaterial`, replaces `CreateTronTerrainMaterial`). Soft dark blue-grey with a subtle world-space tile grid and a faint cool crest rim. **Removed the orange trough emission** (looked odd on the ground; the warmth now lives in the sky) and the neon grid.
- **Blue craft** (`TankMeshBuilder` hull shader). Matte non-metallic saturated blue with strong self-emission so the bright sky doesn't wash it out, plus a thin white-cyan fresnel rim (`pow 8`, energy 0.25) — tuned down repeatedly because the broad rim/under-light kept blooming the small faceted hull to white.
- **Removed the glowing orbs** (`HoverTank.ApplyTestDriveFeel`). Hid the orange thruster glow sphere + its light ("glowing balls" the user wanted gone) and cut the under-craft hover light to a soft white-cyan underglow (energy 0.2, range 2.0).

## Notes

- The craft mesh is a low-poly faceted trapezoid; the reference craft is smooth/rounded. Matching that smoothness would need a remodel (out of scope for a colour/lighting pass).
- Combat/MP keep the Main.tscn baseline look. Tron shader/atmosphere code was replaced, not kept.
- Iterated via the screenshot harness ([[godot-visual-capture-workflow]]).
