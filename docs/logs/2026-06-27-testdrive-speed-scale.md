# 2026-06-27 — Scale TestDrive movement speed to the track arena

**Branch:** worktree-bridge-cse

## Summary

- The track arena (commit `07c57db`) is much smaller than the old infinite TestDrive terrain, so the inherited `ApplyTestDriveFeel` tuning felt ~4× too fast.
- `HoverTank.ApplyTestDriveFeel`: quartered horizontal movement — `MaxSpeed 28 → 7`, `ThrustForce 440 → 110` (keeps the same accel-to-top-speed ratio). Jump-jet values left unchanged.
- Replaced the hardcoded 8 m/s "fast" knee in the bank-lean and FOV-kick speed effects with a knee proportional to `MaxSpeed` (`MaxSpeed * 0.3f`). The old constant would have sat above the new 7 m/s cap, disabling both effects.

## Notes

- Combat-default exports (`MaxSpeed = 12`, `ThrustForce = 200`) are unchanged; only the TestDrive override path was retuned.
