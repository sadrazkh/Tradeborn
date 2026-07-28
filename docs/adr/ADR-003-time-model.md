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
fires. Settlement divides the elapsed span into **bounded sub-steps** (max 200) and resolves
buildings in **topological order** of the recipe graph, limiting each building by
`min(time, available input, free capacity)`.

## Rationale

- **Offline players cost zero CPU.** Nothing runs until someone looks.
- **Correct under scarcity.** The three-way limit is what naive elapsed-time maths misses.
- **Deterministic.** State is a pure function of `(lastSettledState, now)`, which makes the
  economy testable, auditable, and reproducible.
- **Cheap.** 200 steps × ~10 buildings ≈ 2 000 in-memory operations, no I/O, sub-millisecond.
- **Bounded error.** Sub-stepping caps the "goods used slightly early" artefact at one step,
  always in the player's favour, never destroying goods.

## Consequences

**Positive:** scales with *active* players, not registered ones; testable with
`FakeTimeProvider`; jobs become an optimisation rather than a correctness requirement, so
the worker is freely restartable and horizontally scalable.

**Negative — known approximation:** within a single step a consumer may use output produced
by an upstream building in that same step. Bounded, player-favourable, and removing it costs
~100× the CPU for an effect no player can perceive. Accepted deliberately and recorded here
so it is never discovered as a surprise.

**Negative:** settlement must run before *every* read and command. Mitigated by it being
cheap and by having exactly one entry point (`Cities` module) that nothing may bypass.

**Constraint created:** the recipe graph must remain **acyclic**, or settlement does not
terminate. Enforced by a unit test over the seeded graph.

## Validation

- One 8-hour settlement produces byte-identical state to 480 one-minute settlements
- 30-day offline settlement completes within 40 ms p95
- A building starved of input reports `HaltReason.NoInput` and produces nothing
- A building at storage capacity reports `HaltReason.NoCapacity` and destroys nothing
