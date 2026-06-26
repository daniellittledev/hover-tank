# 2026-06-26 — TestDrive feel fixes (whiteout, spawn, banking, speed lines)

**Branch:** main

Follow-up tuning/bugfixes on the just-pulled "test-drive feel" work, which shipped a TestDrive sandbox that rendered as a near-white screen and had over-strong visual effects.

## Changes

- **Fixed full-screen whiteout in TestDrive.** Bisection (disabling the environment swap while leaving the terrain shader running) confirmed the **dream-atmosphere `WorldEnvironment`** in `GameSetup.ApplyDreamAtmosphere` was the cause, not the terrain shader. Gated it behind a new `[Export] bool DreamAtmosphereEnabled` (default **false**), so TestDrive now uses `Main.tscn`'s plain environment. The dream-env code is preserved for later retuning — the likely culprit is the volumetric fog or the low-`GlowHdrThreshold` additive glow.
- **Spawn-below-terrain fix.** Spawn Y was hardcoded to `5`, but seeded `FastNoiseLite` is non-zero at the origin, so the TestDrive dunes (`InfiniteHillScale = 40`) buried the tank. Added `TerrainGenerator.HeightAt(x, z)` (analytic in infinite mode, grid-sampled in finite mode — no physics tick required), registered the terrain in a `"terrain"` group, and added `NetworkManager.SpawnPoint()` which drops the tank 5 m above actual ground at all spawn sites.
- **Banking retuned.** The cosmetic turn-bank now triggers only when *fast and turning*: keyed on yaw rate alone (dropped the strafe/lateral term) and gated by a **squared** high-speed factor, so it stays near zero until the craft is really moving. (`HoverTank._Process`.)
- **Removed screen-space speed lines.** Deleted `CreateSpeedLineOverlay`, its field, `_Ready` call, and per-frame update — the radial streak/vignette overlay didn't look good.
- **Minor:** reduced the under-craft hover glow (energy 4.0→1.5, range 6.0→4.0, speed pulse 2.5→1.0) and the dream-env bloom — both kept.

## Notes

- `DreamAtmosphereEnabled` is the switch for resuming work on the pastel atmosphere; it must be made non-washing-out before re-enabling.
