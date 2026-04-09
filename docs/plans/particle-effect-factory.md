# Plan: Particle & Light Effect Factory

## Problem

`GpuParticles3D` + `OmniLight3D` construction is repeated across three files with near-identical boilerplate:

| File | Method | Nodes created |
|------|--------|---------------|
| `Projectile.cs` | `SpawnImpactEffect` | burst + flash light |
| `HoverTank.cs` | `SpawnDestructionEffect` | burst + smoke + flash light |
| `WeaponManager.cs` | `AttachFlashLight` | muzzle light (already extracted) |

Each site manually constructs `ParticleProcessMaterial`, sets `Gradient` + `GradientTexture1D`, configures `OneShot`/`Explosiveness`/`LocalCoords`, calls `scene.AddChild`, and schedules a `CreateTimer` cleanup. That is ~25–35 lines of boilerplate per site.

## Proposed API

```csharp
// scripts/EffectFactory.cs
static class EffectFactory
{
    // One-shot particle burst, auto-cleaned up after lifetime + buffer.
    public static GpuParticles3D SpawnBurst(Node scene, Vector3 pos, BurstConfig cfg);

    // Continuous emitter; caller controls Emitting and QueueFree.
    public static GpuParticles3D SpawnEmitter(Node scene, Vector3 pos, BurstConfig cfg);

    // Timed OmniLight3D that frees itself after duration seconds.
    public static void SpawnFlash(Node scene, Vector3 pos, Color color,
                                  float energy, float range, float duration);
}

struct BurstConfig
{
    public Color   BaseColor;
    public Color   FadeColor;   // default: dark smoke (0.15, 0.15, 0.15, 0)
    public int     Amount;
    public float   Lifetime;
    public float   VelocityMin, VelocityMax;
    public float   ScaleMin,    ScaleMax;
    public float   Spread;      // default 180 (omnidirectional)
    public float   Explosiveness; // default 0.85
}
```

## Usage after refactor

```csharp
// Projectile.SpawnImpactEffect — shell case:
EffectFactory.SpawnBurst(scene, pos, new BurstConfig {
    BaseColor = new Color(1f, 0.40f, 0.05f), Amount = 60, Lifetime = 1.20f,
    VelocityMin = 5f, VelocityMax = 20f, ScaleMin = 0.12f, ScaleMax = 0.40f,
});
EffectFactory.SpawnFlash(scene, pos, new Color(1f, 0.65f, 0.20f), energy: 8f, range: 12f, duration: 0.08f);
```

This reduces each call site from ~25 lines to ~5.

## Scope

- New file `scripts/EffectFactory.cs`
- Refactor `Projectile.SpawnImpactEffect` and `HoverTank.SpawnDestructionEffect` to use it
- `WeaponManager.AttachFlashLight` is already well-encapsulated; leave it or migrate to `EffectFactory.SpawnFlash` (which is stateless vs the parented-light approach — different enough to keep separate)

## Why deferred

Only 2 call sites currently. The extraction pays off at 3+, or when a new effect type (shield hit, terrain decal, jump jet blast) needs to reuse the same boilerplate. Add then.
