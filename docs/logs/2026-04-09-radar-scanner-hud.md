# 2026-04-09 — Radar / Scanner HUD

**Branch:** `claude/add-radar-scanner-hud-8Gkgg`

## Changes

- Added `scripts/RadarDisplay.cs` — new `Control` subclass that draws the radar via `_Draw()`.
  - `[Export] RadarRange = 150f` (metres) controls the visible radius; tunable in Inspector.
  - `UpdateData(playerPos, playerBasis, enemyPositions)` feeds fresh data each frame and calls `QueueRedraw()`.
  - Draws: dark semi-transparent background circle, two concentric distance rings at 1/3 and 2/3 range, faint cardinal crosshairs, outer green border, forward-notch tick at top, green player dot at centre, red blips for live enemies.
- Modified `scripts/HUD.cs`:
  - Added `BuildRadarPanel(root)` — 130 × 130 px radar panel anchored top-right, matching existing panel style.
  - Added `UpdateRadar()` called from `_Process` — iterates `hover_tanks` group, filters to live enemy tanks (`IsEnemy && Health > 0`), feeds positions to `RadarDisplay`.

## Architecture decisions

- **Heading projection**: enemy positions are projected onto the tank's local X/Z axes via `Basis.X` and `Basis.Z`, so radar "up" always equals tank forward with no trig per blip — one dot-product pair per enemy.
- **Separate drawing component**: `RadarDisplay` owns all drawing logic; `HUD` owns scene-graph queries. Consistent with the `Crosshair`/`CrosshairHUD` split already in the project.
- **No new groups**: enemy detection reuses the existing `hover_tanks` group filtered by `IsEnemy`.
