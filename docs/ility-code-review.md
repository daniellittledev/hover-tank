# -Ility Code Review

A structured pass over changed code asking one question per quality attribute.
The goal is to find issues that functional testing won't catch — latent bugs,
maintenance traps, and hidden coupling.

---

## The attributes

| Attribute | The question to ask |
|-----------|---------------------|
| **Correctness** | Can this produce a wrong result or silently do nothing? |
| **Reliability** | Does it handle edge cases, resource exhaustion, and timing? |
| **Maintainability** | If I change one thing, how many other things break or drift? |
| **Coupling** | What hidden assumptions tie this to other parts of the system? |
| **Consistency** | Does it follow the conventions already established in this codebase? |

These five cover most findings. Testability, extensibility, and performance are
worth adding when the context calls for them — don't force them into every pass.

---

## How to run the review

### 1. Read top-down, take notes

Read the diff once without judging. Write down anything that makes you pause —
a pattern that feels off, a guard that seems missing, a value that appears
more than once. Don't categorise yet; just collect.

### 2. Apply each attribute as a lens

Go back through your notes and ask the attribute question for each one.

**Correctness lens**
- Can two events fire before the first one completes? (race / re-entrancy)
- Are numeric operations bounded? (clamp, overflow, NaN)
- Are null / empty cases handled, or just assumed away?
- Does early-return leave shared state half-updated?

**Reliability lens**
- What happens on the first call, before any state is initialised?
- What happens on the last call, after things start tearing down?
- Does a resource live forever if the happy path doesn't complete?
  (uncollected pickups, leaked timers, undisposed players)
- Is initialisation order implicit? If a caller skips a setup step,
  does the code fail loudly or silently misbehave?

**Maintainability lens**
- Is the same value or logic expressed in more than one place?
  Changing it means finding every copy. (`GetTypeColor` duplicate case.)
- Are magic numbers named? An unnamed `1` in a collision mask is opaque;
  `TankCollisionLayer = 1` documents the intent and is grep-able.
- Does adding a new variant (weapon type, pickup type, wave tier) require
  edits in multiple files? If yes, document the list of touch-points.

**Coupling lens**
- Does this code depend on the order in which other nodes run `_Ready`
  or receive `CallDeferred`? Order-dependent initialisation is fragile.
- Does it reach into another class's fields directly when a method would
  be safer? Direct field writes bypass future validation.
- Does it assume a physics layer, group name, or scene path that lives
  in a different file? Name or document the contract.

**Consistency lens**
- Compare with the nearest equivalent in the codebase — how is a similar
  problem solved there? Diverging without reason adds cognitive load.
- Are signals used where the project already uses signals, and direct calls
  used where the project uses direct calls?

### 3. Triage by severity

| Severity | Criteria | Action |
|----------|----------|--------|
| **Bug** | Produces wrong output or crashes on a reachable path | Fix now |
| **Reliability** | Works today but breaks under timing, scale, or a future change | Fix now |
| **Maintainability** | Creates a maintenance hazard; low immediate risk | Fix before merge |
| **Low** | Style, naming, missing comment | Fix if cheap, otherwise log |

### 4. Report findings

Group by severity, not by file. Lead with the issue and its location, then
state the fix in one sentence. A table works well as a summary:

```
| Severity        | Issue                                                  |
|-----------------|--------------------------------------------------------|
| Bug             | Double-collection: no consumed guard in OnBodyEntered  |
| Maintainability | Color defined twice across BuildVisual / BuildLight    |
| Reliability     | Pickups never expire if uncollected                    |
| Reliability     | _baseYCaptured depends on undocumented call order      |
| Low             | Collision layer magic number                           |
| Low             | FindPlayerPosition fallback is silent                  |
```

---

## Common findings in this codebase

These patterns have appeared before and are worth checking explicitly:

- **Re-entrancy on `QueueFree`** — Godot defers the actual free, so a signal
  handler can fire again before the node is gone. Guard with a `_collected` /
  `_died` bool set *before* `QueueFree`.

- **`CallDeferred` ordering** — `_Ready` fires during `AddChild`; position and
  other properties set *after* `AddChild` are not visible to `_Ready`. Either
  set them before `AddChild`, or expose an explicit init property the caller
  must set first (and document it).

- **Group membership as an API** — `"hover_tanks"`, `"pickups"`, `"ally_tanks"`
  are stringly-typed contracts. When code searches a group, check that every
  relevant node actually calls `AddToGroup` with the exact same string.

- **Physics layer assumptions** — Collision layers and masks are set per-node
  in the scene or in code. If two systems must agree on a layer number, name
  it as a constant in one place and reference it from both.

- **Duplicate switch/match arms** — A switch over an enum that appears in two
  methods (e.g. visuals and audio) will drift when a new variant is added.
  Extract a helper or a data struct so the enum is switched once.
