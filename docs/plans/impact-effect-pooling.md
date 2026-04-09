# Plan: Impact Effect Pooling

## Problem

`Projectile.SpawnImpactEffect` allocates a new `GpuParticles3D` + `OmniLight3D` + 2 `SceneTree` timers on every hit. The minigun fires at 20 rounds/sec × 2 barrels = up to 40 impacts/sec in a busy skirmish, producing steady allocation churn and GC pressure.

## Approach

Pre-warm a small static pool of reusable effect nodes. On impact, pull a node from the pool, reconfigure it, and play it. When the burst finishes, return it to the pool instead of freeing it.

### Pool structure

```csharp
// EffectPool.cs (new autoload or static helper)
private static readonly Queue<ImpactEffect> _pool = new();
private const int WarmCount = 12; // enough for a sustained minigun burst
```

Each pooled unit contains:
- `GpuParticles3D` (OneShot, pre-configured per kind or reconfigured on checkout)
- `OmniLight3D` (energy set to 0 when idle)
- A `Tween` or manual timer to return itself to pool after `lifetime + buffer`

### Checkout / return

```csharp
static ImpactEffect Checkout(Node scene)
{
    var e = _pool.Count > 0 ? _pool.Dequeue() : CreateEffect(scene);
    e.Play();
    return e;
}

void OnFinished() => _pool.Enqueue(this); // called via Tween or timer
```

### Per-kind reconfiguration

Two options:
1. **Separate pools per kind** (3 queues) — no reconfiguration, simplest playback
2. **Single pool, reconfigure on checkout** — update `ProcessMaterial` properties; acceptable because `GpuParticles3D` re-reads material on next emit

Option 1 is simpler; option 2 saves memory (fewer total nodes). Given only 3 kinds and small pool sizes, **option 1** is recommended.

## Scope

- New file `scripts/EffectPool.cs` (or inner class on `Projectile`)
- Change `Projectile.SpawnImpactEffect` to call `EffectPool.Checkout`
- `HoverTank.SpawnDestructionEffect` can optionally share the Shell pool (or keep its own larger burst separate — destruction fires once, pooling is less valuable there)

## Why deferred

At current game scale (1 player + wave enemies), the allocation rate is low enough that GC pauses are not noticeable. Revisit if profiling shows frame spikes during heavy minigun use.
