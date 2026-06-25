# 2026-06-25 — TestDrive "dream-skim" movement & terrain feel

**Branch:** `claude/game-movement-terrain-f4pxlv`

Goal: reproduce the look + movement feel of a reference video (rolling swell
terrain, pastel atmosphere, glowing crests, banking craft, ember sparks, jumps)
in the **TestDrive sandbox only** — combat / multiplayer modes are untouched.
Scope was confirmed with the user: build a playable prototype, matching the
reference's dreamy pastel aesthetic.

## Changes

- **Bigger ocean-swell terrain** (`TerrainGenerator.cs`)
  - Infinite (TestDrive) noise retuned for big, long-wavelength dunes instead of
    gentle ripples: `Frequency 0.0105 → 0.006`, `InfiniteHillScale 22 → 40`,
    `FractalOctaves 3 → 4`, `FractalGain 0.35 → 0.42`.
  - `ChunkLoadRadius 3 → 4` to keep a wide vista of swells visible before fog.
  - Collision is derived from the same `SampleHeight`, so it tracks the new
    amplitude automatically (no separate bake).

- **Dream-terrain shader** (`TerrainGenerator.cs`)
  - `CreatePanelGridMaterial` (StandardMaterial3D) replaced by
    `CreateDreamTerrainMaterial` (ShaderMaterial). Matte dark-slate surface with
    faint per-cell panel seams, plus **teal emissive crests**: emission ramps by
    world height (`CrestGlowLow`/`CrestGlowHigh` exports → shader uniforms) and a
    fresnel rim lights ridge edges, so peaks read as glowing wireframe under the
    scene bloom + fog. `_infiniteMaterial` field widened `StandardMaterial3D → Material`.

- **Dream atmosphere** (`GameSetup.cs`)
  - New `ApplyDreamAtmosphere()`, called only in the SinglePlayer → TestDrive
    branch. Swaps the shared `WorldEnvironment` + `Sun` for: peach→lavender
    procedural sky, warm low golden-hour sun, filmic tonemap, stronger **additive
    bloom** (so the crests glow), and thick **aerial-perspective fog** that fades
    distant swells to teal-grey and hides the streamed-chunk horizon seam.

- **TestDrive movement feel** (`HoverTank.cs`)
  - `_feelMode` gate mirrors `TerrainGenerator._infiniteMode`
    (SinglePlayer + TestDrive + human-driven). `ApplyTestDriveFeel()` raises
    `ThrustForce 200 → 440`, `MaxSpeed 12 → 28`, `JumpImpulse 3 → 4.5`,
    `JumpSustainForce 20 → 26` for fast floaty skimming, and lowers/widens the
    chase camera (`OrbitRadius 8 → 6.5`, `OrbitCenterHeight 1.5 → 1.1`,
    `Fov 70 → 82`) for a sense of speed.
  - **Cosmetic banking**: in `_Process`, after the Visual interpolation, the
    craft rolls about its forward axis into turns — driven by hull-frame lateral
    velocity + commanded yaw rate, eased by `BankResponse`, clamped to `MaxBank`.
    Composed on top of the interpolated pose, never as physics, so it stays
    purely visual.
  - **Ember spark trail**: a `GpuParticles3D` (additive billboard quads,
    yellow→orange fade, `LocalCoords = false`) parented at the rear underside,
    toggled on in `_PhysicsProcess` while grounded (≥3 hover-ray contacts) and
    moving >6 m/s — the carved-surface sparks from the reference.

- **Speed/landing polish** (`HoverTank.cs`, second pass)
  - **Hard-landing burst**: a one-shot `GpuParticles3D` ember fan, repositioned
    to the centre hover-ray's ground point and `Restart()`ed the frame ground
    contact returns with >4 m/s downward speed (`_prevVertVel` captures the
    pre-spring impact velocity). Shared ember-quad billboard factory
    (`CreateEmberQuad`) used by both the trail and the burst.
  - **Stronger hover glow**: the existing `Visual/HoverGlow/GlowLight` is
    boosted to a brighter teal (energy 4, range 6) and pulses up to +2.5 energy
    with speed, so the hull reads like the reference's glowing craft.
  - **Speed FOV kick**: camera FOV eases from the base 82° up to +`MaxFovKick`
    (12°) at `MaxSpeed`, scaled by an eased speed fraction.
  - **Speed-line overlay**: a faint full-screen radial-streak `canvas_item`
    shader on its own `CanvasLayer` (parented to the tank), edge-biased and
    driven by the same speed fraction via an `intensity` uniform.

## Architectural decisions

- **Sandbox-scoped, zero combat impact.** Every change is gated: infinite
  terrain is already TestDrive-only; the atmosphere swap lives behind the
  TestDrive branch in `GameSetup`; the tank feel/banking/sparks sit behind
  `_feelMode`. Combat and multiplayer render and handle exactly as before.
- **Banking lives in `_Process`, not physics.** The hull's real transform stays
  authoritative for collision, hover rays, weapons and networking; the bank is a
  render-only roll layered onto the interpolated `Visual` each frame, so it can't
  destabilise the spring/auto-steer controllers or desync in multiplayer.
- **Runtime-generated shader/material**, consistent with the project's
  "no baked assets" convention — the dream material is built in C# at `_Ready`.

- **Atmosphere / post pass** (`GameSetup.cs`, `project.godot`, `HoverTank.cs`)
  Direction-neutral lighting + rendering upgrades (work for either a future
  cel/edge or realistic-PBR direction), all still TestDrive-scoped:
  - **ACES tonemap** in place of Filmic for a cleaner cinematic highlight
    rolloff. (AgX was the first choice but isn't exposed in GodotSharp 4.3.0's
    `ToneMapper` enum.)
  - **Height fog** (`FogHeight` / `FogHeightDensity`) layers mist in the swell
    troughs on top of the existing aerial-perspective depth fog, plus a little
    `FogSunScatter` for glow toward the low sun.
  - **Volumetric fog** (Forward+) so the sun rakes through the haze and the
    emissive crests inject a faint teal glow into the air (`VolumetricFogGIInject`,
    `VolumetricFogEmission`).
  - **Colour grade**: `AdjustmentEnabled` with a touch more contrast/saturation
    and a code-generated split-tone 1D LUT (teal shadows, warm highlights) via
    `MakeSplitToneLut()` — no baked asset.
  - **Softer key + cool fill**: the sun gets a wider penumbra
    (`LightAngularDistance`) and lifted `ShadowOpacity`; a dim shadowless
    `SkyFill` DirectionalLight3D adds cool skylight bounce for warm/cool contrast.
  - **Debanding** enabled in `project.godot` (`[rendering]`) to kill banding in
    the pastel sky + soft fog.
  - **Vignette** folded into the existing speed-line `canvas_item` shader (one
    pass): corners darken toward black, and a speed line overrides the vignette
    wherever it's brighter.

## Build / verification

- Installed the .NET 8 SDK via the Ubuntu archive (`apt-get install dotnet-sdk-8.0`);
  the official `dotnet-install.sh` CDN (`builds.dotnet.microsoft.com`) is blocked
  by this session's egress policy. `dotnet build hover-tank.csproj` compiles
  clean against the Godot 4.3 C# bindings (0 warnings, 0 errors), validating all
  the `Godot.Environment` / particle / shader-material API usage.
- The `.gdshader` strings (terrain spatial shader, speed-line canvas_item shader)
  are only validated by Godot at scene load — the C# compiler doesn't parse them.

## Not done / follow-ups

- Still needs a run in the editor (F5 → Test Drive) to confirm the look and to
  tune to taste: crest glow thresholds/energy, fog density, swell amplitude,
  bank strength, speed-line density, and the landing-impact gate.
</content>
