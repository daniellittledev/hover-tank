# Server-Side Visibility Culling (Anti-Cheat)

## Problem

`ServerSimulation.BroadcastSnapshot()` currently sends the full `EntityState` of every
tank to every connected client unconditionally. Any modified client can therefore read the
exact world position of all enemies from the incoming snapshot — even enemies far away or
behind solid terrain — and display them as a wallhack or unlimited-range radar.

The radar HUD is currently range-limited on the *client*, but that filter is trivially
bypassed; the data was already transmitted.

## Goal

The server should transmit an enemy's `EntityState` to a client **only when that enemy is
plausibly visible to that client's tank**. Enemies that are not transmitted cannot be
abused by a modified client, regardless of what the client does with its local state.

## Visibility Criteria (in order of cost)

### 1. Transmission range (cheap, always run)

A flat XZ distance threshold — larger than `RadarDisplay.RadarRange` so that enemies
enter the radar before the client stops receiving their state, preventing a visible pop-in
at the radar edge.

Recommended values:
- Radar HUD range: 150 m (configurable `[Export]` on `RadarDisplay`)
- Transmission range: 250 m (enough headroom for the interpolation buffer ~100 ms × speed)

Enemies beyond 250 m are never sent; their state cannot reach the client.

### 2. Line-of-sight check (expensive, amortised)

A single physics `RayCast` on the server from the observer tank's position to the target
tank's position, cast against the terrain collision layer only (not other tanks).

Because this is expensive, LOS checks are **not run every snapshot tick**. Instead:
- Each (observer, target) pair is re-checked every `LosCheckInterval` ticks (e.g. every
  10 ticks = 0.5 s at 20 Hz snapshot rate).
- Between checks the last known result is cached.
- If the cached result says "visible", the entity state is transmitted; if "not visible",
  it is withheld.

LOS adds meaningful protection against enemies hiding behind large terrain features. It is
optional — the system can be shipped with only the distance check first.

### 3. Grace period

When an enemy transitions from visible → not visible (range or LOS), their state
continues to be sent for `GraceTicks` additional snapshot ticks (e.g. 6 ticks = 300 ms).

This prevents:
- Snapping/blinking at the boundary as the range check oscillates
- The client losing the interpolation anchor exactly as the tank rounds a hill

## Architecture

### New class: `VisibilityTracker`

Lives in the server simulation only. Encapsulates all per-pair state so
`ServerSimulation` does not grow complex.

```
class VisibilityTracker
{
    float TransmitRange      = 250f;   // metres
    int   LosCheckInterval   = 10;     // snapshot ticks between LOS rechecks
    int   GraceTicks         = 6;      // snapshot ticks to keep sending after going dark

    // Per (observerPeerId, targetPeerId) state
    Dictionary<(int,int), VisState> _state;

    struct VisState
    {
        bool LastLosResult;   // cached LOS outcome
        int  LosCheckAt;      // snapshot tick of next scheduled recheck
        int  DarkSince;       // snapshot tick when visibility last went false (-1 = visible)
    }

    // Returns true if the server should include targetTank in the snapshot sent to observer.
    bool ShouldSend(int snapshotTick, HoverTank observer, int targetPeerId, HoverTank target);

    // Called when a peer disconnects — prunes state pairs involving that peer.
    void RemovePeer(int peerId);
}
```

### Changes to `ServerSimulation`

`BroadcastSnapshot()` changes from "build one `StateSnapshot`, broadcast to all" to
"build a filtered `EntityState[]` per peer, send individually":

```csharp
private void BroadcastSnapshot(int serverTick)
{
    int snapTick = serverTick / SnapshotInterval; // monotonic snapshot counter for grace logic

    foreach (var (observerPeerId, observerTank) in _tanks)
    {
        var entities = new List<EntityState>();

        foreach (var (targetPeerId, targetTank) in _tanks)
        {
            // Always include the observer's own tank (for reconciliation).
            bool include = (targetPeerId == observerPeerId)
                || _visibility.ShouldSend(snapTick, observerTank, targetPeerId, targetTank);

            if (!include) continue;

            entities.Add(new EntityState { /* ... same as today ... */ });
        }

        var snap = new StateSnapshot
        {
            ServerTick    = serverTick,
            AckedSequence = _ackedSequence.GetValueOrDefault(observerPeerId, 0),
            Entities      = entities.ToArray(),
        };
        _net.SendSnapshot(observerPeerId, snap);
    }
}
```

`NetworkManager` gains a `SendSnapshot(int peerId, StateSnapshot snap)` helper alongside
the existing `BroadcastSnapshot` (which can be removed or kept for single-player shortcut).

### AI enemies (no peer)

Enemy tanks controlled by `EnemyAI` do not have a `peerId` in the peer dictionary today.
They live in the `hover_tanks` group but are not tracked by `ServerSimulation._tanks`.

Options:
- **Assign a sentinel peer ID** (e.g. negative IDs) so they flow through the same
  filtering path — cleanest long-term.
- **Handle them separately** in the snapshot loop — simpler short-term.

Recommendation: handle separately for now (a distinct `_enemyTanks` list in
`ServerSimulation`, always range/LOS filtered but never needing per-peer ack tracking).

## Radar HUD alignment

`RadarDisplay.RadarRange` (currently 150 m) must remain ≤ `VisibilityTracker.TransmitRange`
minus a safety margin, or enemies will disappear from the radar at a shorter distance than
they leave the snapshot stream. Suggested relationship:

```
RadarRange ≤ TransmitRange − (MaxSpeed × InterpolationBuffer)
150        ≤ 250          − (12 m/s × 0.10 s) ≈ 249 m     ✓
```

## Roll-out order

1. Distance-only filtering (no LOS): straightforward loop change in `BroadcastSnapshot`,
   no new helpers needed. Eliminates unlimited-range radar abuse immediately.
2. Add `VisibilityTracker` with LOS + grace period: meaningful protection against
   behind-terrain ESP. Tune `LosCheckInterval` based on server CPU budget.
3. (Future) Frustum-based culling: only send enemies that are inside a generous view
   frustum around the observer camera. Further reduces bandwidth and cheating surface.

## What this does NOT protect

- Cheats that modify physics on the client (speed hack, noclip) — those are addressed by
  `ValidateInput` speed-clamping already in `ServerSimulation`.
- Cheats that reconstruct positions from projectile trajectories or explosion events —
  acceptable residual information leakage.
- The single-player / offline mode — no network channel to filter; irrelevant for anti-cheat.
