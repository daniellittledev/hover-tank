# 2026-04-09 — Procedural Audio Effects

**Branch:** `claude/add-game-audio-effects-64vvA`

## Changes

- **`scripts/AudioManager.cs`** *(new)* — Autoload singleton that synthesises all game audio as `AudioStreamWAV` PCM buffers at startup (no audio asset files required). Generates 8 distinct sounds from scratch using additive synthesis, frequency sweeps, filtered noise, and envelope shaping. Manages a pool of 16 `AudioStreamPlayer3D` nodes for spatial one-shot sounds, plus a 2D ambient loop player.

- **`scripts/HoverTank.cs`** — In `_Ready()`, creates an `AudioStreamPlayer3D` child node (via `AudioManager.CreateEnginePlayer()`) so every tank—player and enemy—has a spatially attenuated engine hum. Added `UpdateEngineAudio()` called each physics tick: smoothly lerps pitch (0.75–1.30×, 1.55× jump) and volume (−14 to −6 dB, −4 dB jump) to match the current throttle and jump-jet state. Added `AudioManager.Instance?.PlayExplosion()` call in `TakeDamage()` when health reaches zero.

- **`scripts/WeaponManager.cs`** — In `Fire()`, after spawning projectiles, calls `AudioManager.Instance?.PlayWeaponFire(kind, position)` with the tank's global position as the 3D sound origin. One call per trigger pull regardless of projectile count (e.g. minigun spawns two bullets but makes one sound).

- **`scripts/Projectile.cs`** — In `Die()`, calls `AudioManager.Instance?.PlayImpact(kind, position)` when `_age < Lifetime` (real collision, not range timeout). Skips sound for `IsVisualOnly` ghost projectiles so client prediction doesn't double-play with the server's authoritative projectile.

- **`project.godot`** — Added `AudioManager="*res://scripts/AudioManager.cs"` to the `[autoload]` section.

## Sound Design

| Sound | Duration | Technique |
|-------|----------|-----------|
| Engine hum | 1 s loop | Additive harmonics: 75 + 150 + 225 Hz + 1100 Hz whine |
| Minigun | 70 ms | Noise + 1800 Hz tone, fast-attack exponential decay |
| Rocket fire | 500 ms | 600→150 Hz frequency sweep + noise, shaped envelope |
| Shell fire | 600 ms | 90→40 Hz bass sweep + initial crack burst |
| Bullet impact | 120 ms | Pure noise with very fast (35×) exponential decay |
| Explosion (small) | 1.0 s | 50→25 Hz thump + blast noise + long rumble tail |
| Explosion (large) | 1.8 s | Same layers, longer decay for tank-death drama |
| Ambient wind | 4 s loop | White noise → 3-pass box-blur lowpass + 0.25 Hz modulation |

## Architectural decisions

- **Procedural PCM, no files**: All audio is generated in C# using `AudioStreamWAV` with hand-coded sample buffers. This keeps the repo asset-free and makes every sound trivially replaceable with a real .ogg file later — just swap `EngineHumStream` (etc.) for a loaded asset.
- **Per-tank `AudioStreamPlayer3D`**: Engine hum is a child of the HoverTank node rather than managed by the pool. This gives automatic 3D attenuation for enemy tanks with zero extra code, and Godot's physics interpolation keeps the source position accurate.
- **Pool round-robin fallback**: When all 16 pool slots are busy (heavy firefight), the oldest slot is stolen. This prevents unbounded node creation while ensuring no sound is silently dropped without a substitute.
- **`IsVisualOnly` guard in Projectile**: Client-predicted ghost projectiles skip impact sounds; only the server's authoritative projectile fires them. This prevents double-sound artifacts in online play.
- **Fixed RNG seed (1337)**: PCM generation uses `new Random(1337)` for deterministic output — the sounds are identical every launch, simplifying testing and comparisons.
