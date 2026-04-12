# 2026-04-12 — fix-game-bugs

**Branch:** `claude/fix-game-bugs-FKIzL`

## Changes

- **Terrain lighting (Main.tscn):** Sky colours were near-black (`0.015/0.04`), starving
  SDFGI and ambient of any sky radiance. Replaced with a daytime blue sky
  (`sky_top_color ≈ 0.18, 0.35, 0.65`) and brighter horizon. Raised
  `ambient_light/energy` from `0.3` → `0.7` so shadowed terrain is visible.

- **Cannon recoil spin (WeaponManager.cs:246):** `ApplyCentralImpulse` magnitude reduced
  from `10f` → `4f`. On the ~5 kg tank body, `10f` gave ≈2 m/s instant backward
  velocity; near crater edges the 3×3 hover spring grid produced uneven compensating
  torques that spun the tank uncontrollably. `4f` keeps a noticeable kick without
  destabilising hover physics.

- **Audio volumes (AudioManager.cs):** All weapon/impact/explosion sounds reduced by
  ~12 dB across the board:
  - Bullet fire: -6 → -18 dB
  - Rocket fire: -3 → -15 dB
  - Shell fire: -2 → -14 dB
  - Bullet impact: -8 → -20 dB
  - Small explosion: -2 → -14 dB
  - Large explosion: 0 → -12 dB

## Architectural notes

None — all changes are tuning values; no structural modifications.
