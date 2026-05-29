# 2026-05-29 — Auto-steer heading singularity & torque-axis fix

**Branch:** `claude/hover-tank-steering-7Pql9`

## Summary

Fixed two steering bugs that surface when the hull is tilted near vertical
(steep crater walls, jump-jet nose-overs).

- **Bug 1 — heading singularity.** `Atan2(Basis.Z.X, Basis.Z.Z)` reads the hull
  heading in three places. When the forward axis nears world vertical, both
  components collapse to ~0 and `Atan2(0,0)` returns 0, so the heading reads 0
  regardless of true facing. In `HoverTank.ProcessMovement` this fed a garbage
  error into the auto-steer PD and slammed a large spurious yaw torque on an
  already-tilted tank; in `TurretController` and `FollowCamera` (FPS cone) it
  snapped the turret/reticle to an arbitrary direction.
  - Added `MathUtils.TryGetHeading(Basis, out float yaw)` which returns false
    when the Z axis has too little horizontal projection (forward within ~10°
    of vertical, threshold `HeadingHorizThresholdSq = 0.03`).
  - `HoverTank.ProcessMovement`: skips the proportional steer term when the
    heading is undefined; the derivative (damping) term still runs.
  - `TurretController`: holds current turret yaw when heading is undefined.
  - `FollowCamera`: skips the FPS yaw-cone clamp when heading is undefined.

- **Bug 2 — steer torque applied about world up.** `ProcessMovement` applied the
  yaw PD torque about `Vector3.Up`, which leaks into roll/pitch when the hull is
  tilted and fights `ProcessSelfRighting`. Now applied about the hull's own up
  axis (`GlobalBasis.Y`), with the damping term reading the yaw rate about that
  same axis. For an upright tank `GlobalBasis.Y == world up`, so normal driving
  is unchanged.

## Notes

- Bug 3 from the review (thrust/steer active while airborne) was deemed not a
  bug — intended air control — and left as-is.
- `FollowCamera._Ready` still reads the heading directly via `Atan2`; it runs
  once at spawn when the tank is upright, so no guard is needed.
- Could not compile-check: no Godot/dotnet CLI in this environment. Changes are
  C#-syntactically consistent with surrounding code; needs an editor build.

## Architectural decisions

- Centralised the heading read + singularity guard in `MathUtils.TryGetHeading`
  so the shared `Atan2(Basis.Z.X, Basis.Z.Z)` convention (camera / hull /
  turret) has a single source of truth.
