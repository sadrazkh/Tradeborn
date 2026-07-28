# ADR-008 — IHostedService + PostgreSQL job queue with `SKIP LOCKED`

**Status:** Accepted (Phase 0)

## Context

Some transitions are discrete and cannot be derived lazily: a construction completing while
the player is offline should still fire its notification and be reflected in leaderboards.
[ADR-003](ADR-003-time-model.md) handles *continuous* production; this ADR handles the
discrete events.

## Options

| Option | Verdict |
|---|---|
| Hangfire / Quartz | Rejected — capable, but brings a dashboard, its own schema, and a scheduling model we do not need for four job types. |
| Redis-backed queue | Rejected as system of record — Redis is optional through Phase 2 ([A-01](../roadmap/DECISIONS_REQUIRED.md)), and job loss must not be possible. |
| External worker service | Rejected — premature ([ADR-002](ADR-002-modular-monolith.md)). |
| **IHostedService + PostgreSQL queue** | ✅ Chosen |

## Decision

A `scheduled_jobs` table drained by an `IHostedService` polling every 5 seconds:

```sql
scheduled_jobs (
  id, kind, due_at_utc, payload jsonb, status,
  attempts, locked_until_utc, last_error, created_at_utc
)
CREATE INDEX ix_scheduled_jobs_due ON scheduled_jobs (due_at_utc) WHERE status = 'Pending';
```

```sql
SELECT * FROM scheduled_jobs
 WHERE status = 'Pending' AND due_at_utc <= now()
 ORDER BY due_at_utc
 LIMIT 50
 FOR UPDATE SKIP LOCKED;
```

Job kinds: construction complete, upgrade complete, transport arrival, market price
snapshot.

## Rationale

**Why PostgreSQL:** jobs are enqueued **in the same transaction** as the command that
creates them. A construction can never be started without its completion job being durably
scheduled — that atomicity is unavailable with an external queue and is worth more here than
throughput.

**Why `SKIP LOCKED`:** multiple instances can drain the same queue concurrently with no
coordination, no leader election, and no distributed lock. It is the standard PostgreSQL
pattern and it scales horizontally for free.

**Why 5-second polling:** the game is asynchronous; a 5-second granularity is imperceptible.
Polling one indexed partial index is negligible load, and it avoids `LISTEN/NOTIFY`'s
connection-affinity complications.

**Why not Hangfire:** four job types do not justify a second scheduling framework, a second
schema, and a second dashboard to secure.

## The property that makes this safe

**Jobs are an optimisation, not a correctness requirement.** If the worker is down for an
hour, the next player read settles everything correctly anyway ([ADR-003](ADR-003-time-model.md)).
The queue exists so that *offline* players' notifications and derived data stay fresh.

This is why the worker can live in `Web`, be restarted freely, and be extracted later
without redesign.

## Reliability

- At-least-once delivery → **all handlers must be idempotent**
- Exponential backoff on failure; `attempts` capped, then moved to `Failed` for inspection
- `locked_until_utc` guards against a crashed worker holding a job forever
- Failed jobs are visible in the admin panel (Phase 8), never silently dropped

## Consequences

**Positive:** transactional enqueue, no extra infrastructure, horizontally scalable, simple
to reason about and to test.

**Negative:** polling adds a small constant query load. Negligible against a partial index.

**Negative:** not suitable for high-throughput streaming work. Not our workload; revisit if
job volume exceeds ~1 000/s.
