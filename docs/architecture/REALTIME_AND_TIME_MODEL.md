# Real-time & Time Model

> **Status:** Approved (Phase 0) · Resolves the central tension in the brief:
> *"production must continue while offline"* vs *"do not run a per-second tick per building"*.
> Backed by [ADR-003](../adr/ADR-003-time-model.md).

## 1. The problem

Naive designs pick one of two bad options:

| Approach | Why it fails |
|---|---|
| Tick every building every second | 10 k players × 10 buildings = 100 k writes/s. Dies at ~500 players. |
| Compute output as `elapsed × rate` on read | Ignores input shortages and storage caps. Produces bread from nothing. |

Tradeborn uses neither.

## 2. The model — Deterministic Lazy Settlement (DLS)

**Nothing advances until someone looks at it.** State is a pure function of
`(lastSettledState, serverTimeNow)`, computed on demand.

Three triggers cause settlement:

1. **Read** — any request that returns city state settles it first.
2. **Command** — any economic command settles before validating.
3. **Scheduled event** — construction completion and similar discrete events, via a
   due-jobs queue (§6). Not a tick.

Between triggers the database is idle. An offline player costs **zero CPU**.

### Core invariant
> Settling a city at time `T` produces byte-identical state regardless of how many times,
> or at what intermediate times, it was settled before `T`.

This is what makes the economy testable, auditable, and reproducible. It is asserted
directly: `IntegrationTests` settle the same city via one 8-hour jump and via 480 one-minute
jumps, then assert the resulting states are equal.

## 3. Why naive `elapsed × rate` is wrong

A bakery consumes 2 flour + 1 plank per cycle. Over 8 offline hours it *could* run 240
cycles — but only if flour and planks were available the whole time, and only if the
warehouse had room for 240 bread. Multiplying elapsed time by rate ignores both.

Correct settlement must respect three simultaneous limits per building:

```
cyclesByTime     = floor(elapsedSeconds / cycleSeconds)
cyclesByInput    = min over inputs  ( available[r] / inputQty[r] )
cyclesByCapacity = min over outputs ( freeSpace[r] / outputQty[r] )

cyclesRun        = min(cyclesByTime, cyclesByInput, cyclesByCapacity)
```

Whichever limit binds is recorded as the building's `HaltReason` — which the client renders
as a warning mote above the building (pillar P1: the player *sees* the bottleneck).

## 4. Fixed-grid sub-stepping

Resolving all buildings once over the whole elapsed span would let a sawmill consume wood
"before" the lumber camp produced it. Instead the span is divided into sub-steps, and three
properties make the result exact.

### 4.1 The grid is absolute, not relative
Sub-step boundaries fall on multiples of **30 000 ms since the Unix epoch** — the shortest
recipe cycle in the slice. The step size does **not** depend on how much time has elapsed.

This is what makes the determinism invariant hold *exactly*. Settling once across eight
hours walks precisely the same grid cells as settling 480 times across one minute each, so
the two produce identical state. A step size derived from the elapsed span (e.g.
`elapsed / 200`) would produce different cell boundaries in the two cases and the results
would silently diverge — which is the trap this design exists to avoid.

### 4.2 Buildings resolve in topological order
Within each step, buildings are resolved by recipe rank (extractors → processors →
assemblers), so producers always run before their consumers.

### 4.3 Outputs are committed at the end of the step
Inputs are consumed immediately (so two buildings competing for the same input in one step
cannot both spend it), but outputs accumulate in a buffer and are added to the inventory
only once every building has been resolved.

Without this, a consumer could use — in the very same step — goods its upstream producer
had not yet made. That is a small but real way for the economy to create value from nothing.

> **Consequence: chains take one cycle to spin up.** A cold Lumber Camp → Sawmill chain
> yields 59 planks in its first hour rather than 60, reaching the full 60/hour from the
> second hour onward. This is realistic pipeline-fill latency, it favours correctness over
> generosity, and it is asserted by a test (`Chains_take_one_cycle_to_spin_up`) so it stays
> a known property rather than resurfacing later as a bug report.

### 4.4 Cost: the fixed-point exit
A 30-day absence is 86 400 grid cells, which would be wasteful to walk. It is not walked:
once a full step produces nothing **and** every producing building is halted, the state is a
fixed point — inventory changes only through production, so no later step can differ.
Settlement stops there.

In practice a returning player costs only the steps needed to fill their warehouse. Measured:
a 30-day absence with a 200-unit cap settles in **under 500 steps**, sub-millisecond.

## 5. Settlement algorithm

```
SettleCity(city, now):
    elapsed = now - city.LastSettledAt
    if elapsed <= 0: return                      # clock skew / already current

    elapsed = min(elapsed, MaxOfflineWindow)     # 30 days — guards absurd gaps
    steps, stepSeconds = ComputeSteps(elapsed)

    for step in 1..steps:
        stepEnd = city.LastSettledAt + stepSeconds
        foreach building in city.Buildings ordered by RecipeTopologicalRank:
            if building.State != Producing: continue
            cycles = min(cyclesByTime, cyclesByInput, cyclesByCapacity)
            if cycles > 0:
                inventory.Remove(recipe.Inputs  × cycles)
                inventory.Add   (recipe.Outputs × cycles)
                building.ProgressSeconds += cycles × recipe.CycleSeconds
                building.HaltReason = None
            else:
                building.HaltReason = BindingConstraint()    # NoInput | NoCapacity
        city.LastSettledAt = stepEnd

    city.LastSettledAt = now
```

Key details:

- **Progress is never discarded.** `ProgressSeconds` accumulates the *consumed* time, so a
  building 29 s into a 30 s cycle keeps those 29 s across settlements.
- **Topological rank is precomputed** at seed time and stored on the building definition.
  The recipe graph is a DAG by invariant (`RESOURCE_GRAPH.md` §2); a unit test rejects cycles.
- **`MaxOfflineWindow = 30 days`** bounds worst-case work for a returning lapsed player.
  Capacity caps mean nothing is actually lost — storage filled long before day 30.

## 6. Scheduled events (the only "jobs")

Discrete, non-continuous transitions do not fit DLS and use a due-jobs queue instead:

| Event | Trigger |
|---|---|
| Construction complete | `CompletesAt <= now` |
| Upgrade complete | `CompletesAt <= now` |
| Transport arrives | `ArrivesAt <= now` |
| Market price snapshot | every 5 min (analytics only) |

Implementation: a `ScheduledJobs` table with a `DueAt` index; an `IHostedService` polls
every 5 s with `FOR UPDATE SKIP LOCKED` and processes the batch.

Crucially, **these jobs are an optimisation, not a correctness requirement.** If the worker
is down for an hour, the next player read settles everything correctly anyway — the job
queue only exists so that *offline* players' SignalR notifications and leaderboards stay
fresh. This makes the worker independently restartable and horizontally scalable without
coordination.

## 7. Server time is the only clock

- All timestamps are `DateTimeOffset` in **UTC**, from an injected `TimeProvider`
  (.NET 8+ built-in). Tests inject `FakeTimeProvider` — no `DateTime.UtcNow` anywhere.
- The client **never** sends a timestamp that affects economy. Client time is used only for
  animation interpolation.
- Every state response carries `serverTimeUtc`. The client computes a clock offset once and
  renders all countdowns against `serverTime`, so a wrong device clock changes nothing.
- A banned pattern list is enforced by `ArchitectureTests`: `DateTime.Now`,
  `DateTime.UtcNow`, `DateTimeOffset.Now`, `DateTimeOffset.UtcNow` must not appear in
  `Domain` or `Application`.

## 8. Concurrency & transactional boundary

**The City aggregate is the unit of consistency.** One player's city is never touched by
another player's command (the NPC market is the only shared state, handled separately).

Every economic command:

```
BEGIN;
  SELECT * FROM cities WHERE id = @id FOR UPDATE;   -- serialises this city
  Settle(city, now);
  Validate(command, city);
  Apply(command, city);
  INSERT INTO audit_ledger (...);
  INSERT INTO outbox (...);
COMMIT;
```

- Row-level `FOR UPDATE` on the city gives correctness without `SERIALIZABLE` isolation,
  and contention is naturally near-zero (one player per city).
- PostgreSQL `xmin` is mapped as an EF Core concurrency token for defence in depth.
- Default isolation stays `READ COMMITTED`.

**NPC market prices** are shared. Price updates use an atomic conditional update on the
market row and re-read the price inside the transaction — the client's displayed price is
advisory only, and the sale executes at the server price at commit time. If that differs
from what the player saw by more than a tolerance, the command returns
`PriceMoved` with the new price rather than silently filling.

## 9. Idempotency

Every state-changing command requires an `Idempotency-Key` header (client-generated UUID v4).

```
idempotency_keys(player_id, key) PRIMARY KEY
    → response_hash, response_body, created_at
```

On replay the stored response is returned without re-executing. Insert happens inside the
same transaction as the command, so a crash between "apply" and "record key" is impossible.

Keys expire after 24 h. Redis fronts this table from Phase 3 as a read-through cache; it is
**not** the system of record, so a Redis outage degrades latency, never correctness.

## 10. Real-time push (SignalR)

SignalR carries **notifications, not state**:

| Message | Payload | Purpose |
|---|---|---|
| `ConstructionCompleted` | buildingId, newLevel | Trigger completion animation |
| `TransportArrived` | jobId, resources | Trigger unload animation |
| `MarketPriceChanged` | resourceId, price | Update chart |
| `BuildingHalted` | buildingId, reason | Show warning mote |

Rules:
- A notification is a **hint to refresh**, never authoritative data. The client re-fetches
  or applies a delta; it never trusts a push to mutate its economy view.
- Full state is never broadcast. Deltas only.
- If SignalR is unavailable the client falls back to polling every 15 s and **nothing
  breaks** — this is verified by an integration test that disables the hub.
- Scale-out uses the Redis backplane (Phase 9, not before).

## 11. Client-side prediction

The client predicts *visuals only*:

```
Player clicks "Build"
  → immediately: ghost mesh, dust puff, sound          (prediction, 0 ms)
  → POST /api/cities/{id}/buildings  (Idempotency-Key)
  → on 200: reconcile to server truth, start real construction animation
  → on 4xx: rewind visual, show inline reason
```

Coins and resources shown in the HUD are **also** predicted, but flagged `pending` and
rendered slightly dimmed until confirmed. Reconciliation is authoritative and silent when
it agrees, animated when it does not.

The client never predicts: prices, XP awards, quest completion, or anything another player
can influence.

## 12. What we deliberately did not build

| Rejected | Why |
|---|---|
| Full event sourcing | Enormous cost; DLS + audit ledger gives replay and audit already. See [ADR-004](../adr/ADR-004-economy-persistence.md). |
| Per-building background tick | O(players × buildings) writes/s. The thing this document exists to avoid. |
| Actor model (Orleans) | Real answer at ~50 k concurrent. Premature at slice scale; DLS is a compatible stepping stone. |
| Client-side simulation of truth | Trivially cheatable. |
