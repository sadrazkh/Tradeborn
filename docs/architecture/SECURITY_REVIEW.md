# Security Review — Phase 9

> A control-by-control audit of the **actual code** against `SECURITY_MODEL.md` T1–T12,
> performed by reading the implementation rather than trusting the design document.
>
> **Evidence levels are stated honestly**, because most of this project's security tests have
> been written and never executed:
>
> | Level | Meaning |
> |---|---|
> | **Structural** | Cannot be violated without changing the design. The strongest kind. |
> | **Tested (run)** | A test asserts it and that test has actually passed. |
> | **Tested (unrun)** | A test asserts it, but it needs PostgreSQL and has never executed. |
> | **Reviewed** | Verified by reading the code. No automated check. |

## Findings

Three gaps were found. All three were introduced by me in earlier phases, all three are fixed.

### 🔴 F-1 — Admin endpoints had no rate limiting *(fixed)*
Every other endpoint group carries `RequireRateLimiting`; `/api/admin` carried only
`RequireAuthorization`. An authenticated Support account could hammer the audit and player-list
queries — the widest, most expensive reads in the system — without limit.

**Cause:** oversight when the admin surface was added in Phase 8, not a decision.
**Fix:** an `admin` policy (120/min per operator) applied to both groups.

### 🟠 F-2 — Economic commands only had the general limit *(fixed)*
`SECURITY_MODEL.md` §5 specifies 60 req/min for economic commands specifically. Only the
general `game` limit (240/min) existed, so commands got four times their intended allowance —
which is exactly the budget an attacker probing for a race condition wants.

**Fix:** a `command` policy (60/min per player), stacked on top of `game` on every write
endpoint. Reading your city 240 times a minute is merely wasteful; attempting 240 sales is
someone looking for a race.

### 🟠 F-3 — Registration shared the login limit *(fixed)*
Both sat under `auth` at 10 per 5 minutes per IP. That is right for login (a stuffing attack
retries *one* account) and far too generous for registration (mass registration creates
*many*) — it permitted 120 accounts per hour per IP against a specified 5.

**Fix:** a `register` policy at 5/hour per IP, applied to the registration endpoint on top of
the group's limit.

---

## T1 — Forged resource or coin amounts · **Structural**
Request DTOs have no amount fields at all. `StartConstructionRequest` is
`(DefinitionId, Col, Row)`; `SellRequest` is `(Resource, Quantity)`. There is no cost, price or
total to tamper with because none exists in the shape.

A field that does not exist cannot be forged. Integration test
`A_forged_body_cannot_change_what_the_build_costs` sends `costCoins: 0` and `level: 3` and
asserts the player is charged the real 150 and gets level 1. **Tested (unrun).**

## T2 — Market price manipulation · **Structural**
`SellRequest` carries no price. `MarketHandler` re-reads the price inside the transaction,
under a row lock, and uses that. Two players selling simultaneously each get the price as it
stood when their transaction reached that point — the second gets the lower price the first
caused. **Reviewed**, plus unit tests on the pricing maths. **Run.**

## T3 — Replay / double-charge · **Tested (unrun)**
`Idempotency-Key` is required — a missing header is a 400, not a silently-unprotected request.
The key is recorded in the *same transaction* as the command, so a crash between "apply" and
"record" is impossible. Recording uses a primary-key insert; a concurrent duplicate collides
rather than being checked-then-inserted.

## T4 — Concurrent double-spend · **Tested (unrun)**
Every command takes `SELECT … FOR UPDATE` on the city row before reading anything else. Defence
in depth: `xmin` as an EF concurrency token, and a unique index on `(CityId, Col, Row)` that
makes two buildings on one plot impossible even if both requests somehow passed the lock.

Two tests cover it: 20 parallel builds on one plot → exactly one succeeds; 20 parallel builds
across different plots → still exactly one, because the queue allows one at a time.

## T5 — Duplicate quest reward · **Structural**
The claim is an insert into `player_quests` guarded by a primary key on `(PlayerId, QuestId)`.
Checking "already claimed?" then inserting would leave a window; the insert *is* the check.

## T6 — Clock manipulation · **Tested (run)**
All time comes from an injected `TimeProvider`. An architecture test fails the build if
`DateTime.Now`, `DateTime.UtcNow`, `DateTimeOffset.Now` or `DateTimeOffset.UtcNow` appears in
`Domain` or `Application`. Client timestamps are used only for animation interpolation.

## T7 — Cross-tenant access · **Structural for players, policy-guarded for admin**
Player endpoints resolve the city **from the token**. There is no player id in any player-facing
route or body, so accessing someone else's city is not a check that could be forgotten — it is
unexpressible.

Admin endpoints *do* take a player id in the route. That is the point of them, and it is guarded
by the `admin.read` / `admin.write` policies. Worth stating explicitly so the asymmetry is a
recorded decision rather than something a future reader mistakes for an inconsistency.

Test: `Production_cannot_be_switched_on_for_another_players_building` — another player's
building id simply does not exist in the caller's city. **Tested (unrun).**

## T8 — Bypassing unlock requirements · **Tested (run)**
`ConstructionRules` is the single authority: plot exists, unlocked, unoccupied, city level
sufficient, affordable, queue free. Ten distinct refusals, each unit-tested.

## T9 — Request spam · **Fixed this phase** — see F-1, F-2, F-3
| Scope | Limit | Partition |
|---|---|---|
| `auth` | 10 / 5 min | IP |
| `register` | 5 / hour | IP |
| `game` | 240 / min | player |
| `command` | 60 / min | player |
| `admin` | 120 / min | operator |

Still per-instance. Redis-backed limiting is required before running more than one instance,
and is listed in the hardening checklist in `DEPLOYMENT.md`.

## T10 — NPC market arbitrage · **Structural, tested (run)**
Buy price is 1.25× sell price, so a round trip loses 20 % before the 3 % fee. This is
arithmetic, not a rate limit — there is no waiting period that makes it work.

A property test runs ten buy/sell rounds at four volumes with recovery time between them and
asserts the terminal balance is always below the starting balance.

## T11 — Data exfiltration · **Reviewed**
Player endpoints return only that player's projection. Admin endpoints are the deliberate
exception and are policy-guarded, paged, and clamped to 100 rows regardless of what is
requested — an admin panel is exactly where an unbounded query gets written.

The admin player list deliberately **omits email**. Support needs to find and understand an
account, not read personal data.

## T12 — Token theft · **Reviewed**
Access token: JWT, 15 minutes, held in a JS variable — never `localStorage`, so an XSS cannot
steal a durable credential. Refresh token: opaque, `HttpOnly` + `Secure` + `SameSite=Strict`,
rotated on every use with reuse detection that revokes the whole family. Stored hashed, so a
database leak yields no usable sessions.

---

## Residual risks

| Risk | Status |
|---|---|
| Rate limits are per-instance | Correct for one instance; Redis needed before scaling out |
| `/health/ready` is anonymous | Leaks dependency detail; restrict at the proxy (`DEPLOYMENT.md`) |
| No IP allow-list on `/api/admin` | Deployment configuration, on the hardening checklist |
| **31 integration tests have never run** | The largest residual risk in this document |
| Admin actions are not idempotent | Deliberate — an operator retrying a grant *should* grant again, and the audit records both |

## The honest summary

The structural controls — T1, T2, T5, T7, T10 — are the strong ones, and they are strong
because the design makes violations unexpressible rather than caught. Those hold regardless of
test coverage.

The controls that depend on runtime behaviour — T3, T4 — have tests that have **never
executed**. Until they do, "no double-spend" is a well-argued claim, not a verified fact.
