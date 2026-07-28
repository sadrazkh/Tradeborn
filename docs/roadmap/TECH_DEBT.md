# Tech Debt

> An honest inventory at the end of Phase 9. Ordered by what would hurt most if left.
> Items are debt only if they are *known shortcuts*; deliberate scope cuts live in
> `VERTICAL_SLICE.md` §4 instead.

## 🔴 Blocking — the slice is not verified until these are done

### D-1 · 31 integration tests have never executed
Every test that needs PostgreSQL skips. That includes the two that matter most:
`Concurrent_builds_on_one_plot_produce_exactly_one_building` (T4) and
`Replaying_the_same_idempotency_key_charges_only_once` (T3).

**Why it happened:** Docker is not installed on the development machine, so Testcontainers
cannot run, and `TRADEBORN_TEST_POSTGRES` was never set.

**Cost of leaving it:** every claim about concurrency, idempotency and persistence is
reasoning, not evidence. Eight phases of work rest on it.

**Fix:** one environment variable and one command. Minutes, not days.

### D-2 · The app has never been run end to end
Health, auth and the Phase 0 prototype endpoint were verified in Phase 0. Since then the
database, auth flow, six command handlers and the whole client have been built and **never
started together**. The client has never rendered real server data.

**Fix:** set the connection string, run it, play the six-minute demo script.

## 🟠 Significant

### D-3 · Vehicles are not rendered from real transport jobs
`AgentRenderer` wanders carts around the road graph for atmosphere. The server sends every
in-flight haul with absolute departure and arrival instants — exactly what a renderer needs to
place a cart mid-journey after a reload — and nothing consumes it.

Slice steps 8 and 9 are economically complete and visually fake. This is the largest remaining
gap between what the game *does* and what the player *sees*, which for a project whose premise
is "every number has a body" is the wrong gap to have.

### D-4 · No admin panel UI
The admin API is complete, authorised and rate limited. There is no front end. Everything is
reachable with curl, which is fine for one operator and not fine for a support team.

### D-5 · Economy simulator not built
`ECONOMY_DESIGN.md` §12 defines six invariants the simulator must prove — no dominant strategy,
per-building return rising with chain depth, inflation under 15 %/day. Some are unit-tested
statically; none are tested *over time* across player archetypes.

**Consequence:** the balance numbers are defensible by argument and by spot-checks, but nobody
has watched a simulated 30 days.

### D-6 · No E2E tests
`window.__tradeborn` exists precisely so Playwright can assert against the canvas, and no
Playwright suite was written. FPS and draw calls have never been measured either — the pane in
this environment does not composite frames.

## 🟡 Worth doing before scale

### D-7 · Rate limits are per-instance
Correct for a single instance. A second instance doubles every effective limit. Redis-backed
limiting is a configuration change, not a redesign.

### D-8 · Transport job ids change identity across a reload
Settlement generates deterministic string ids; persistence maps them to a stable GUID; a job
loaded back carries the GUID string instead. Harmless today — ids are not economically
meaningful and `HasTransportFrom` keys on the building — but it means "settle twice, get
identical ids" holds only within one process lifetime.

### D-9 · `players.DisplayName` search cannot use an index
The admin search uses `ILIKE '%term%'`, which no btree index can serve. Fine at current scale;
needs a `pg_trgm` GIN index eventually. Deferred because it requires a database extension,
which is a deployment concern rather than application code.

### D-10 · No sound
`ART_DIRECTION.md` §8 specifies an `AudioManager` with a no-op sink so licensed audio can be
added without touching gameplay code. Neither the manager nor the sink exists.

## 🟢 Accepted, not debt

Recorded here so nobody "fixes" them later without reading why:

| Thing | Why it is fine |
|---|---|
| No job queue | Construction and delivery complete inside settlement (ADR-003). A queue would be optimisation, not correctness. |
| Chains take one cycle to spin up | Realistic pipeline fill; errs toward correctness over generosity. Asserted by a test. |
| Sub-step approximation | Bounded at one 30 s step, always in the player's favour. Documented in ADR-003. |
| Admin actions not idempotent | An operator retrying a grant *should* grant again; the audit records both. |
| `Contracts` and `Workers` not separate assemblies | Extraction triggers recorded in ADR-002. Deferral, not omission. |

## Recommended order

1. **D-1** — one command, unblocks every other claim
2. **D-2** — run it; expect to find things
3. **D-3** — closes the biggest gap between the economy and what the player sees
4. **D-5** — before any balance decisions are made on real players
5. **D-6**, then the rest
