# 2026-04-12 — Unit selection controls

Branch: `claude/unit-selection-controls-5AqGT`

## Summary

Reworked the ally-unit selection and order controls around a single
context-sensitive key (SPACE), moved deselect to Escape, and fixed a bug where
Follow-mode allies froze in place after being deselected.

## Changes

- **`project.godot`** — Replaced `unit_order_move` (V) and `unit_order_attack`
  (B) actions with a single `unit_select` action bound to Space. Rebound
  `unit_deselect_all` from X to Escape.
- **`scripts/UnitCommander.cs`**
  - `_Input` no longer handles LMB selection. It now only intercepts Escape
    and consumes it (via `SetInputAsHandled`) when there's an active selection
    so the pause menu and mouse-capture toggle don't also trigger.
  - New `ProcessSelectKey` polls the `unit_select` action. If the crosshair is
    over an ally, SPACE selects it (Shift = add, Ctrl = toggle, none =
    replace). Otherwise, with units selected, it issues Attack if aiming at an
    enemy or Move if aiming at terrain.
  - `ProcessOrderKeys` kept Follow (G), Hold (H), and formation cycle (F); the
    V/B handlers were removed.
  - HUD action list updated: row 0 is `[SPACE] …` with live text/colour that
    tracks the crosshair (green = move, red = attack, cyan = select); rows for
    G/H/F remain; row 4 shows `[ESC] Deselect all`.
  - Removed the now-dead `_weapons` field and the
    `WeaponManager.SuppressFireThisFrame` call.
  - **Follow bug fix.** `UpdateFormationSlots` previously only wrote slots to
    *selected* allies, so any ally in Follow order that the player deselected
    kept its last world-space slot and stopped tracking the player. It now
    scans `hover_tanks` and assigns slots to every friendly with
    `CurrentOrder == Follow`, selected or not.
- **`scripts/GameSetup.cs`** — Moved the Escape/pause handler from `_Input` to
  `_UnhandledInput` so UnitCommander can consume Escape.
- **`scripts/FollowCamera.cs`** — Moved the Escape → mouse-capture toggle from
  `_Input` to `_UnhandledInput` for the same reason.
- **`scripts/WeaponManager.cs`** — Removed the now-unused
  `SuppressFireThisFrame` property and its short-circuit in the fire loop.
  Space and LMB no longer share a purpose, so the suppression flag is dead.

## Architectural notes

- **Escape is now priority-based.** `UnitCommander._Input` runs first; if any
  unit is selected, it deselects and marks the event handled, which stops
  propagation to `_UnhandledInput`. With nothing selected, both
  `FollowCamera._UnhandledInput` (mouse toggle) and
  `GameSetup._UnhandledInput` (pause) fire as before.
- **Follow slots are order-driven, not selection-driven.** Any future ally
  spawning code that sets `CurrentOrder = Follow` will automatically receive
  a formation slot from `UnitCommander` without needing to be selected.
