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
2. **Play** — `F5` (runs `scenes/Main.tscn`)

## Controls

| Key | Action |
|-----|--------|
| W / S | Forward / Back |
| A / D | Turn left / right |
| E (tap) | Jump jet burst |
| E (hold) | Sustained thrust |

## Customising terrain

Replace `terrain/heightmap.png` with any 8-bit grayscale PNG. Brighter pixels = higher ground. The terrain generator carves craters on top at runtime — tweak counts and depth via the `Terrain` node's exported properties in the Godot editor.
