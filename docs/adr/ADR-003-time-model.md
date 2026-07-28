# ADR-003 — Deterministic Lazy Settlement for game time

**Status:** Accepted (Phase 0) · Full specification:
[`../architecture/REALTIME_AND_TIME_MODEL.md`](../architecture/REALTIME_AND_TIME_MODEL.md)

## Context

Production must continue while players are offline, but the brief explicitly forbids a
per-building per-second tick. These are only contradictory if state must be *stored*
continuously rather than *derived* on demand.

## Options

| Option | Verdict |
|---|---|
| Per-building tick (1 Hz) | Rejected — 10 k players × 10 buildings = 100 k writes/s. Fails around 500 players. |
| Naive `elapsed × rate` on read | Rejected — ignores input shortages and storage caps. Produces bread with no flour. |
| Full event sourcing with replay | Rejected — correct but disproportionately expensive. See [ADR-004](ADR-004-economy-persistence.md). |
| **Deterministic Lazy Settlement** | ✅ Chosen |

## Decision

State advances only when read, when a command arrives, or when a scheduled discrete event
fires. Settlement walks a **fixed 30 s grid aligned to the Unix epoch**, resolves buildings
in **topological order** of the recipe graph, limits each building by
`min(time, available input, free capacity)`, and **commits outputs at the end of each step**.
It exits early once the state reaches a fixed point.

## Rationale

- **Offline players cost zero CPU.** Nothing runs until someone looks.
- **Correct under scarcity.** The three-way limit is what naive elapsed-time maths misses.
- **Exactly deterministic.** Because the grid is absolute rather than derived from the
  elapsed span, settling once over eight hours walks the same cells as settling 480 times
  over one minute. A relative step size would have made these diverge silently.
- **No value from nothing.** Committing outputs at step end stops a consumer using goods its
  producer has not yet made.
- **Cheap.** The fixed-point exit means a returning player costs only the steps needed to
  fill their warehouse — a 30-day absence settles in under 500 steps, sub-millisecond.

## Consequences

**Positive:** scales with *active* players, not registered ones; testable with
`FakeTimeProvider`; jobs become an optimisation rather than a correctness requirement, so
the worker is freely restartable and horizontally scalable.

**Negative — chains take one cycle to spin up.** A cold Lumber Camp → Sawmill chain yields
59 planks in its first hour instead of 60, reaching full rate from hour two. This is the
price of end-of-step commit, it is realistic pipeline-fill behaviour, and it errs toward
correctness rather than generosity. Asserted by a test so it stays a known property.

**Negative:** settlement must run before *every* read and command. Mitigated by it being
cheap and by having exactly one entry point (`Cities` module) that nothing may bypass.

**Constraint created:** the recipe graph must remain **acyclic**, or settlement does not
terminate. Enforced by a unit test over the seeded graph.

## Validation

- One 8-hour settlement produces byte-identical state to 480 one-minute settlements
- 30-day offline settlement completes within 40 ms p95
- A building starved of input reports `HaltReason.NoInput` and produces nothing
- A building at storage capacity reports `HaltReason.NoCapacity` and destroys nothing
