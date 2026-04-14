# Hover Tank

Battlezone-style hover tank game built with Godot 4.3 (C# / ForwardPlus).

## Requirements

- [Godot 4.3+](https://godotengine.org/download) with .NET support (the `mono`/dotnet build)
- [.NET 8 SDK](https://dotnet.microsoft.com/download)

## Setup

```bash
git clone https://github.com/daniellittledev/hover-tank
```

Open the cloned folder in Godot 4.3.

## Run

1. **Build** — `Project → Build` or `Ctrl+B`
2. **Play** — `F5` (runs `scenes/MainMenu.tscn`)

## Game Modes

- **Single Player** — wave-based survival against escalating enemy AI
- **Network Multiplayer** — host or join via IP; server-authoritative with client-side prediction
- **Split Screen** — two players on one machine (keyboard + keyboard)
- **Settings** — configurable from the main menu

## Controls

### Movement

| Key | Action |
|-----|--------|
| W / S | Forward / Back |
| A / D | Turn left / right |
| E (tap) | Jump jet burst |
| E (hold) | Sustained thrust |

### Aiming & Weapons

| Key / Input | Action |
|-------------|--------|
| Mouse move / Right stick | Orbit camera + aim turret |
| Left Mouse / RShift | Fire weapon |
| Tab / RCtrl | Cycle weapon (minigun → rocket → shell) |

The turret tracks the camera yaw (clamped ±90° from hull). A/D keys add a steering bias on top of the PD auto-steer that aligns the tank body to the aim direction — Halo Ghost style.

### Unit Command (Single Player)

Two allied tanks spawn with you at wave start and can be directed:

| Key | Order |
|-----|-------|
| G | Follow player |
| H | Hold position |
| V | Move to crosshair |
| B | Attack crosshair target |
| F | Cycle formation (Wedge / Line / Column) |
| X | Deselect all |

Click allies with the crosshair to select them (Shift+click to add, Ctrl+click to toggle). A terrain ring marker shows the current move target; selected units show a hull glow and a HUD card with health and order state.

### Pause

| Key | Action |
|-----|--------|
| Esc | Pause / resume |

## Weapons

| Weapon | Fire key | Behaviour |
|--------|----------|-----------|
| Minigun | Primary | Fixed forward, high rate of fire, two simultaneous rounds |
| Rocket | Primary | Turret-aimed, guided to snapshot target (Halo-style, not homing) |
| Shell (cannon) | Primary | Turret-aimed, high damage, kicks tank backward on fire |

## Single-Player Wave System

Enemies spawn in even radial rings at 65 m and close in. Difficulty escalates each wave:

| Wave | Enemies | Accuracy | Weapon | Lead targeting |
|------|---------|----------|--------|----------------|
| 1 | 2 | 50% error | Minigun | No |
| 2 | 3 | 35% error | Minigun | No |
| 3 | 4 | 22% error | Rocket | No |
| 4 | 5 | 15% error | Rocket | Yes |
| 5+ | wave+2 | 10% error | Shell | Yes |

Destroying enemies drops health and ammo pickups between waves. Up to 10 pickups can be on the field at once; each new wave spawns 4 more.

### Pickups

| Pickup | Restores |
|--------|---------|
| Health | 40 HP |
| Minigun ammo | 200 rounds |
| Rocket ammo | 8 rockets |
| Shell ammo | 5 shells |

Pickups bob and spin; drive through to collect (player only).

## HUD

- **Crosshair** — gapped reticle with centre dot
- **Health & ammo** — bottom panel
- **Radar** — top-right 130×130 px scanner (150 m range); red blips for enemies, heading-relative so radar-up = tank forward
- **Wave banner & enemy counter** — top-centre during single-player waves
- **Unit command cards** — top-left; one card per selected ally with health bar and order indicator

## Visual Effects

- **Screen shake** on damage — scales with weapon type (bullet < rocket < shell)
- **Cannon recoil** — `ApplyCentralImpulse` shoves the tank backward; spring-lag camera turns this into a visible kick
- **Muzzle flash** — `OmniLight3D` burst at each fire point, energy decays over 90 ms
- **Explosion particles** — `GpuParticles3D` burst + light flash on projectile impact, scaled by weapon type
- **Tank destruction** — 80-particle burst, 6 s smoke column, physics tumble on death

## Audio

All audio is procedurally synthesised at startup — no audio asset files required.

| Sound | Technique |
|-------|-----------|
| Engine hum (looped) | Additive harmonics at 75 / 150 / 225 / 1100 Hz; pitch and volume track throttle |
| Minigun | Noise + 1800 Hz tone, fast-attack decay |
| Rocket fire | 600→150 Hz frequency sweep + noise |
| Shell fire | 90→40 Hz bass sweep + crack burst |
| Bullet impact | Pure noise, very fast decay |
| Explosion | 50→25 Hz thump + blast noise + rumble tail (larger variant for tank death) |
| Ambient wind | Lowpass-filtered white noise with slow amplitude modulation |

Sounds are 3D-attenuated; each tank carries its own engine `AudioStreamPlayer3D`. A 16-slot round-robin pool handles concurrent one-shot sounds.

## Customising Terrain

Terrain is fully procedural — tweak `NoiseSeed`, `CraterCount`, `CraterDepth`, `HeightScale`, etc. on the `Terrain` node in the Godot editor.

For hand-authored campaign maps, point `CustomMapPath` at a packed float32 heightmap: exactly `(GridSize+1)²` little-endian float values, row-major (x fastest), heights in world metres. Leave `CustomMapPath` empty to use noise.
