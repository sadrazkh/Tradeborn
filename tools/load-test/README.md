# Load test harness

> **No results exist.** This harness has never been run against anything — Tradeborn has never
> been deployed, and the integration tests have never executed either. Every number in
> `docs/architecture/PERFORMANCE_BUDGET.md` §6 remains a *target*, not a measurement.
>
> The harness is here so that the first person with an environment can produce real numbers in
> minutes instead of writing a tool first.

## What it measures

The two things the architecture makes non-obvious claims about:

1. **Idle players cost nothing.** Deterministic Lazy Settlement claims a player who is not
   looking consumes zero CPU (ADR-003). The way to test that is to register many accounts, let
   them sit, and watch that throughput for *active* players does not move.
2. **Settlement cost scales with absence, not with population.** A player returning after 30
   days should settle in roughly the same time as one returning after an hour, because the
   fixed-point exit stops early once storage fills.

## Running it

Requires [k6](https://k6.io). The target must be a **deployed instance with its own database** —
never a shared one, because the script registers thousands of accounts and grants itself
resources.

```bash
k6 run --vus 200 --duration 5m -e BASE_URL=https://your-instance tools/load-test/slice.js
```

| Env var | Meaning |
|---|---|
| `BASE_URL` | Target instance |
| `VUS` | Concurrent virtual players |
| `IDLE_ACCOUNTS` | Accounts to create and then leave alone |

## What to record

Fill these in and move them into `PERFORMANCE_BUDGET.md` §6, replacing the targets with
measurements and marking them as measured:

| Metric | Target | Measured |
|---|---|---|
| `GET /api/cities/me` p50 / p95 | 40 / 120 ms | — |
| Economic command p50 / p95 | 50 / 150 ms | — |
| Settlement CPU, 30-day gap | ≤ 40 ms p95 | — |
| Throughput at 1 000 concurrent | — | — |
| Throughput with 10 000 idle accounts | unchanged | — |
| Database connections at peak | — | — |
| Error rate | < 0.1 % | — |

## Known limits before it is even run

Two things will bind before anything else, and they are worth predicting so the results are
read correctly rather than blamed on the wrong layer:

- **One row lock per city per command.** Contention is near zero (one player per city), so this
  should not bind. If it does, the cause is a client retrying faster than the command completes.
- **The shared market price row.** Every sale of the same resource contends on one row
  (`REALTIME_AND_TIME_MODEL.md` §8). This is the first thing that will serialise under load,
  and it is the number worth watching most closely. Mitigation, if needed, is sharding the
  price row by resource — which it already is — and then batching price updates.
