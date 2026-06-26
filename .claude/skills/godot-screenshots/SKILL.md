---
name: godot-screenshots
description: Render the game (or just the tank hull) to a PNG and view it, to verify visuals/art/lighting changes. Use whenever you change meshes, shaders, materials, lighting, camera, or terrain and want to SEE the result instead of guessing. Godot has no CLI build; this drives the editor binary headlessly-ish to capture real rendered frames.
---

# Godot visual capture

Godot delegates C# compilation to the .NET SDK, so building and rendering are two
separate steps. Build with `dotnet`, then run a throwaway capture scene that
instances the world, lets it settle, saves a viewport PNG, and quits. Read the PNG
to actually look at it — this is the only reliable way to verify visuals.

## 1. Build (required after any C# change)

```bash
dotnet build hover-tank.sln -c Debug
```

This produces the same `HoverTank.dll` the editor's F5/Build uses
(`.godot/mono/temp/bin/Debug/`). There is no separate "Godot build" — your
`dotnet build` IS the live artifact.

## 2. Render a capture scene

The Godot binary is at:

```
D:\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe
```

(The `_console` variant prints stdout. If the path differs on this machine, find it
with the installed Godot mono build — the version must match `project.godot`.)

Run a capture scene with `--quit-after` as a safety timeout. User args go after `++`.

### In-game view (`tools/Screenshot.tscn` → `tools/shot-<mode>.png`)

Instances `Main.tscn` in SinglePlayer, captures at frame ~240 (~4 s, after terrain
build + tank settle). Mode arg selects the game mode:

```bash
"D:\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe" \
  --path "D:\dev\hover-tank" --quit-after 600 "res://tools/Screenshot.tscn" ++ testdrive
```

- Modes: `testdrive` (default) or `waves`. Output: `tools/shot-testdrive.png` / `tools/shot-waves.png`.
- Default to `testdrive` for visual checks — it's the showcase/art mode.

### Hull-only debug grid (`tools/HullPreview.tscn` → `tools/hull-preview.png`)

Renders ONLY the tank hull (`TankMeshBuilder`) from four fixed viewpoints — TOP,
FRONT, LEFT, and a behind-the-ship 3/4 ANGLE — in a 2×2 editor-style grid with a
neutral background. Best for inspecting mesh shape, winding, and normals in
isolation (no terrain/sky). No mode arg:

```bash
"D:\Godot_v4.6.2-stable_mono_win64\Godot_v4.6.2-stable_mono_win64_console.exe" \
  --path "D:\dev\hover-tank" --quit-after 600 "res://tools/HullPreview.tscn"
```

The preview uses a flat bright ambient, so metallic/reflective materials look
uniform there — judge reflections in the in-game scene, not the grid.

## 3. View it

Read the PNG with the Read tool — e.g. `D:\dev\hover-tank\tools\shot-testdrive.png`.
Iterate: edit → build → render → Read.

## Tips

- **One command per iteration:** chain build + render, e.g.
  `dotnet build hover-tank.sln -c Debug && "<godot>" --path "D:\dev\hover-tank" --quit-after 600 "res://tools/Screenshot.tscn" ++ testdrive`.
- **Close-ups:** to inspect the tank up close in-scene, temporarily lower
  `AimCamera.OrbitRadius` in `HoverTank.ApplyTestDriveFeel` (e.g. 3–4.5), render,
  then revert. The gameplay default is 6.5.
- **Headless caveat:** running Godot with `--headless` uses the dummy renderer (no
  shaders/visuals). These capture scenes need the real (windowed) renderer, which
  the command above uses — don't add `--headless`.
- **Diagnosing normals/winding:** if faces look inside-out or unlit, temporarily set
  the hull material to unshaded flat colour (geometry check) or a world-normal
  visualiser shader (`ALBEDO = normalize(worldNormal) * 0.5 + 0.5`): up = green,
  down = magenta.
- Both capture scenes (`tools/Screenshot.cs`, `tools/HullPreview.cs`) are dev-only
  harnesses, not shipped. Output PNGs are gitignored.
```
