# Next Priorities

Battlezone-style game — things to work on next, roughly in order of impact.

## 1. Enemy AI Tanks (highest priority)
The biggest missing piece. Without enemies there's no gameplay tension.
- Patrol/hunt behaviour: wander the terrain, detect player within range, engage
- Shooting: fire at player using existing weapon/projectile system
- Health & death: take damage from player projectiles, spawn explosion on death
- Spawning: place enemies on terrain at game start or via wave system

## 2. Wave / Spawn System
Turns the sandbox into a game loop.
- Spawn waves of enemies that escalate in count and aggressiveness
- Score counter (kills) and game-over condition (player health reaches 0)
- Display wave number and score on HUD (HUD system already extensible)

## 3. Radar / Scanner HUD
Classic Battlezone staple — should feel natural alongside the existing HUD.
- Circular radar in a HUD corner showing enemy blips relative to player heading
- Range-limited: only show enemies within a configurable radius
- Extend the existing HUD (HUD.cs) which already has a SetTank() hook

## 4. Audio
No sound at all currently — audio would dramatically improve game feel.
- Engine hum tied to thrust input
- Weapon fire sounds per weapon type (minigun rattle, rocket whoosh, shell boom)
- Explosion on projectile impact / enemy death
- Background ambient (wind, distant rumble)

## 5. Power-ups / Pickups
Health, ammo, or shield crates on the terrain.
- Spawn at fixed or random terrain positions
- Proximity pickup (overlap trigger on the tank RigidBody)
- Types: health restore, ammo resupply per weapon, temporary speed boost

## 6. Destructible Structures
Bunkers, pylons, or supply depots as cover and objectives.
- Static meshes placed on terrain in Main.tscn or spawned procedurally
- Health component: take damage from projectiles, play destruction effect on death
- Fits Battlezone aesthetic; gives the terrain more tactical interest

## 7. Visual Polish / Juice
- Screen shake on taking damage or firing the tank shell
- Muzzle flash (brief light pulse at fire points — fire points already exist in HoverTank.tscn)
- Explosion particles on projectile impact (extend existing particle trail system)
- Tank destruction effect (debris, smoke) when health reaches 0

---

## Notes
- Projectile/collision infrastructure is already in place — enemies and pickups can reuse it directly.
- HUD is procedurally built and has a SetTank() API — radar can slot in alongside existing elements.
- NetworkMessages.cs will need new message types if enemies are to be server-authoritative in multiplayer.
- All input actions live in project.godot — add any new actions there, not in code.
