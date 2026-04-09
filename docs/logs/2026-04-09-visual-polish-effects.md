# 2026-04-09 — Visual Polish Effects

**Branch:** `claude/visual-polish-effects-XhVW7`

## Changes

- **Screen shake on damage** (`FollowCamera.cs`, `HoverTank.cs`)
  - `FollowCamera` gains `AddShake(float amplitude)`: stores a peak amplitude that decays linearly at 1.0 m/s until gone, applying a random camera-local X/Y offset each frame
  - `HoverTank.TakeDamage` calls `AddShake` scaled by damage — bullet ≈ 0.012 m, rocket ≈ 0.125 m, shell ≈ 0.25 m; only the player camera shakes (enemy/ally tanks have no camera)

- **Cannon shell recoil** (`WeaponManager.cs`)
  - Shell fire applies `ApplyCentralImpulse(backward × 10)` to the tank RigidBody3D, shoving the tank backward; the camera's spring-follow lag turns this into a visible kick
  - Rocket fire applies a lighter backward impulse (3 units) for lighter weapon feel

- **Muzzle flash at fire points** (`WeaponManager.cs`)
  - Five `OmniLight3D` nodes are created in `_Ready` and parented to the existing `Marker3D` fire points (cannon, rocket L/R, minigun L/R)
  - Flash lights have zero energy until the weapon fires; each shot sets a timer (`FlashDuration = 0.09 s`) and energy decays linearly to zero — cannon peak 12, rocket 5, minigun 2.5
  - Flash ticking runs unconditionally (before the `NetworkGhost` early-return) so remote player tanks also show muzzle flashes

- **Explosion particles on projectile impact** (`Projectile.cs`)
  - `SpawnImpactEffect` spawns a one-shot `GpuParticles3D` burst + short `OmniLight3D` flash at the hit position
  - Particle count, lifetime, velocity, and scale scale up by weapon type: bullet = small sparks, rocket = medium fireball, shell = large debris cloud fading to smoke
  - Only triggered on authoritative hits (`_age < Lifetime && !IsVisualOnly`), matching the existing audio trigger condition

- **Tank destruction effect** (`HoverTank.cs`)
  - `SpawnDestructionEffect` runs when health reaches 0: large one-shot burst (80 particles, orange-to-smoke gradient), lingering smoke column that emits for 6 s then fades, and a 0.15 s `OmniLight3D` flash (energy 20, range 18 m)
  - Physics: `ApplyCentralImpulse` (12 up + small random horizontal) and `ApplyTorqueImpulse` (random axes ±15) throw and spin the tank body as it dies

## Architectural decisions

- Muzzle flash lights are parented to fire-point `Marker3D` nodes so they inherit position automatically — no per-frame transform copy needed
- All particle nodes are added to `GetTree().CurrentScene` (not the projectile/tank) so they survive the parent's `QueueFree`; `SceneTree.CreateTimer` callbacks handle cleanup
- Recoil applied via `ApplyCentralImpulse` on the `RigidBody3D` parent from within `WeaponManager.Fire()` — no new API surface added to `HoverTank`
