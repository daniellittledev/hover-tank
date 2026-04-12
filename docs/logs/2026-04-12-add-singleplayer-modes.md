# 2026-04-12 — add-singleplayer-modes

**Branch:** `claude/add-singleplayer-modes-8P5u5`

## Changes

- **GameState.cs:** Added `SinglePlayerMode` enum (`TestDrive`, `StandardWaves`)
  and a `SinglePlayerMode` property, so the menu can carry sub-mode intent
  alongside the existing `GameMode`.

- **MainMenu.cs:** Inserted a new Single Player sub-panel between the main
  panel and the game scene. "SINGLE PLAYER" on the root panel now navigates
  to the submenu (parity with Multiplayer) instead of starting the game
  directly. Submenu offers:
  - `STANDARD WAVES` – classic wave-based survival.
  - `TEST DRIVE` – empty sandbox.
  Escape-to-back handling extended to cover the new panel.

- **GameSetup.cs:** In `SinglePlayer` mode, `WaveManager` is now only spawned
  when `SinglePlayerMode == StandardWaves`. Since `WaveManager` also owns
  ally spawning and enemy waves, skipping it leaves the player alone on the
  map for Test Drive — no extra code needed.

## Architectural notes

None — the change piggy-backs on the existing `WaveManager`/`GameSetup` split.
`WaveManager` remained the single owner of all NPC tanks (allies + enemies),
which kept the Test Drive path to a one-line conditional.
