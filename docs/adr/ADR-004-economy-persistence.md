# ADR-004 — Relational state + audit ledger + outbox (not event sourcing)

**Status:** Accepted (Phase 0)

## Context

The brief requires reconstructable state, an audit trail for economic transactions, and no
double-spending — while explicitly warning against full event sourcing unless a documented
need exists. This ADR is that analysis.

## Decision

**Current-state relational tables** as the system of record, plus:

1. **Audit ledger** — append-only, written in the same transaction as every economic mutation
2. **Outbox** — append-only, drained asynchronously to SignalR and analytics
3. **Domain events** — in-process, for cross-module reactions

No event sourcing.

## Rationale

Event sourcing was evaluated against what we actually need:

| Need | Event sourcing | Ledger + current state |
|---|---|---|
| Audit trail | ✅ | ✅ (ledger) |
| Reconstruct a balance | ✅ | ✅ (replay ledger deltas) |
| Investigate fraud | ✅ | ✅ (ledger + correlation id) |
| Temporal queries | ✅ | ⚠️ limited to ledger granularity — which is exactly economic events |
| Read performance | ❌ needs projections | ✅ direct query |
| Query complexity | ❌ high | ✅ ordinary SQL |
| Implementation cost | ❌ high | ✅ low |
| Schema evolution | ❌ event versioning forever | ✅ ordinary migrations |

The ledger delivers every capability we identified a concrete need for, at a fraction of the
cost. Adopting event sourcing here would be choosing a solution before having the problem.

Additionally, [ADR-003](ADR-003-time-model.md)'s determinism already provides much of what
teams usually reach for event sourcing to get: state is a pure function of last-settled
state and time, so it is reproducible by construction.

## Schema

```sql
audit_ledger (
  id, player_id, city_id, occurred_at_utc, kind,
  resource_deltas jsonb, money_delta_cent bigint, balance_after_cent bigint,
  correlation_id, idempotency_key, metadata jsonb
)  -- append-only; app role has no UPDATE/DELETE grant

outbox_messages (id, occurred_at_utc, type, payload jsonb, processed_at_utc, attempts)
idempotency_keys (player_id, key, response_hash, response_body, created_at_utc)
```

`balance_after_cent` is what makes reconciliation cheap: an integration test sums deltas and
asserts they equal the stored balance, catching any mutation that bypassed the ledger.

## Consequences

**Positive:** simple queries, straightforward migrations, low cost, full audit, reconcilable
balances, outbox gives reliable at-least-once delivery of notifications.

**Negative:** no free time-travel of arbitrary state. We can reconstruct *balances* from the
ledger but not, say, the exact camera-visible city at an arbitrary past instant. No use case
requires that.

**Negative:** the ledger grows. Mitigated by 90-day hot retention then archival, and by
partitioning on `occurred_at_utc` when volume justifies it.

## Revisit if

- Regulatory or dispute requirements demand full state reconstruction at arbitrary instants
- Player-to-player trading creates disputes the ledger cannot settle
- The economy grows complex enough that "why is this number wrong?" becomes routinely
  unanswerable from the ledger alone
