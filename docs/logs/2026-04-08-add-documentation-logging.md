# 2026-04-08 — Add documentation and session logging

**Branch:** `claude/add-documentation-logging-V31Eb`

## Changes

- Added `## Architecture notes` section to `CLAUDE.md` capturing key design decisions:
  - Hover physics spring-damper per-raycast approach
  - Terrain mesh and collision built in a single pass from the same height array
  - Input actions defined in `project.godot`, not in code
  - Scene ownership structure (Main.tscn → HoverTank.tscn, camera as child)
  - No singletons/autoloads — state is script-local
- Added `## Session logs` section to `CLAUDE.md` defining the convention for per-session log files in `docs/logs/`
- Created `docs/logs/` directory
- Created this initial session log file

## Notes

Architecture section is intentionally minimal — only information that future changes are very likely to need, not a full design doc.
