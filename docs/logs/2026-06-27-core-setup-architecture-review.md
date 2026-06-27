# Core setup / architecture / config review

- **Date:** 2026-06-27
- **Branch:** `claude/core-setup-architecture-review-17famn`

## Summary

Reviewed the core setup, architecture, and config. The project has grown well
beyond what the docs described (now ~7.8k lines: netcode, AI, waves, audio,
split-screen, menus). Acted on the agreed follow-ups from that review.

## Changes

- **Config — reconciled Godot version.** `project.godot` was saved by Godot 4.6
  while the build SDK and docs still said 4.3. Bumped `Godot.NET.Sdk` 4.3.0 → 4.6.0
  (`hover-tank.csproj`) and updated `README.md` to 4.6 so all four references agree.
  (Render cap `run/max_fps=60` left as-is per decision.)
- **Docs — refreshed `CLAUDE.md` (kept high-level).** Fixed stale/incorrect claims:
  version 4.3 → 4.6; "4 corner raycasts" → the actual 9-ray 3×3 grid; the
  "turret has no script / does not rotate" note (a `TurretController` exists and
  yaws the turret within a clamped cone); and "No autoloads" (there are three:
  `GameState`, `NetworkManager`, `AudioManager`). Added a brief "Major systems" map
  and a few key-file entries (networking, `GameSetup`, `WaveManager`, `UiTheme`).
- **Netcode — hardened `ServerSimulation`.** The per-peer jitter queue was
  unbounded (memory-growth / flood vector). Added `MaxJitterQueue` (drop oldest
  beyond the cap; doubles as a rate limit) and `MaxExtrapolationTicks` so a peer
  that goes silent coasts to a stop instead of driving on its last command forever.
  Healthy connections are unaffected (they queue only 1–3 packets).
- **Refactor — extracted `scripts/UiTheme.cs`.** The green-on-dark palette and the
  panel / transparent-box / hover-box / separator builders were copy-pasted across
  `MainMenu`, `PauseMenu`, `GameSetup`, and `WaveManager`. Centralised the canonical
  colours and box construction in `UiTheme`; each screen now delegates to it while
  keeping its own local builder names, so call sites and per-screen specifics
  (font sizes, panel alpha/margins) are unchanged.

## Decisions / notes

- Delegation (local methods forward to `UiTheme`) chosen over rewriting every call
  site — lowest-risk migration since this environment has no CLI build to verify C#.
- **Not build-verified here** (no Godot CLI build step); changes should be opened in
  the editor (Ctrl+B) to confirm compilation.
- **Deferred:** carving responsibilities out of the ~885-line `HoverTank.cs` god
  object (TestDrive feel, destruction FX, jump-jet fuel are the natural extractions).
  Held back as a larger structural change pending editor verification.
