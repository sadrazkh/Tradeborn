# Decisions Required

> Non-blocking questions resolved with a stated assumption so work continues. Each entry
> records what was assumed, why, what it costs to reverse, and when it must be confirmed.
> **Blocking** decisions are escalated immediately and never appear here.

## Legend
**A** = assumed and implemented · **Q** = open, needs an answer by the stated phase

---

## Resolved with the user (Phase 0)

| # | Question | Decision |
|---|---|---|
| D-01 | Where does Tradeborn live, given the repo held an unrelated production project? | **New independent repository** at `E:\Cash.Net\source\repos\sadrazkh\Tradeborn`. Charity project untouched. |
| D-02 | Docker is not installed — how do we run PostgreSQL/Redis locally? | **Locally installed services.** PostgreSQL confirmed on :5432. `docker-compose.yml` is kept for CI and for contributors who have Docker. |

---

## A-01 · Redis is optional until Phase 3
**Assumed:** the app boots and behaves correctly without Redis, degrading to in-memory
caching, in-memory rate limiting, and PostgreSQL-only idempotency. A clear warning is
logged at startup.
**Why:** Redis is not running on the dev machine (port 6379 idle), and blocking Phase 1 on
an infrastructure install would stall visible progress for no design benefit.
**Cost to reverse:** none — the abstraction (`ICacheStore`, `IIdempotencyStore`) is in place
from Phase 1; only the registration changes.
**Confirm by:** Phase 3. Redis becomes required in *production* configuration then;
PostgreSQL remains the system of record, so this is latency, not correctness.

---

## A-02 · Name stays "Tradeborn"
**Assumed:** ten alternatives were generated (`Emporia`, `Craftspire`, `Cargonaut`,
`Havenport`, `Guildforge`, `Prosperia`, `Coinforge`, `Wharfborn`, `Ledgerhall`) and
Tradeborn was retained — two syllables, internationally pronounceable, no negative
connotation, and `-born` carries the "built from nothing" fantasy.
**Cost to reverse:** low before Phase 6 (namespaces, package names, folder). High after
public exposure.
**Confirm by:** Phase 6, before anything is shown publicly.

---

## A-03 · Vertical slice cut to 5 resources and 8 buildings
**Assumed:** the brief's 8 resources / 12 buildings reduced. Iron → Foundry → Tools deferred.
**Why:** the cut chain adds tension but no new *kind* of decision. The slice keeps one
convergence point (Bakery) and one divergence point (surplus planks), which is the minimum
for a genuine trade-off — and the maximum a new player can learn in one session.
**Cost to reverse:** low. Adding the third chain is data plus one building mesh.
**Confirm by:** Phase 6, from simulator output and first playtest (see `BALANCE_ASSUMPTIONS.md` A-8).

---

## A-04 · Four `src` projects instead of seven
**Assumed:** `Contracts` and `Workers` folded into `Application` and `Web`. Modules are
folders with test-enforced boundaries.
**Why:** the brief simultaneously demands a modular monolith and forbids premature
over-architecture. Assemblies with a single consumer are ceremony.
**Cost to reverse:** low — extraction is mechanical, and the boundaries are already enforced.
**Extraction triggers** recorded in [ADR-002](../adr/ADR-002-modular-monolith.md).

---

## A-05 · Camera elevation is fixed at 55°
**Assumed:** β is not user-adjustable; α snaps to 45°.
**Why:** guarantees every building silhouette is authored for the angle it is seen at, and
removes a class of "the player found a bad angle" bugs.
**Cost to reverse:** low technically; **high** artistically — freeing β means every mesh
must read well from every elevation.
**Confirm by:** Phase 2 playtest. If players fight the camera, revisit.

---

## A-06 · Money stored as `long` cent, resources as whole `long` units
**Assumed:** no fractional resources; 1 coin = 100 cent.
**Why:** exact integer arithmetic; no floating-point drift; deterministic and diffable.
**Cost to reverse:** **high** after Phase 4 — it is a migration plus a rebalance.
**Confirm by:** Phase 4. Flagged as the highest-cost assumption in this file.

---

## A-07 · No monetisation hooks in the slice
**Assumed:** no IAP, no ads, no premium currency, and no data model anticipating them.
**Why:** pillar P3 and §15 of the brief. Building hooks "just in case" shapes systems toward
monetisation even when unused.
**Cost to reverse:** medium — a premium currency added later needs its own ledger, but the
audit ledger design already accommodates a second currency type.
**Confirm by:** post-slice.

---

## A-08 · Tutorial rewards carry early pacing (1 200 coins over 7 quests)
**Assumed:** roughly 5× passive income during the first 15 minutes.
**Why:** lets build times stay short and the demo stay tight without inflating steady-state
economy.
**Cost to reverse:** none — pure pacing data.
**Confirm by:** Phase 7 playtest.

---

## A-09 · SignalR carries notifications only, never authoritative state
**Assumed:** every push is a hint to refresh; polling every 15 s is a complete fallback.
**Why:** makes real-time an enhancement rather than a dependency, and keeps a hub outage
from being an outage.
**Cost to reverse:** low.
**Confirm by:** Phase 6 (verified by an integration test that runs with the hub disabled).

---

## Open questions

### Q-01 · Target launch region and localisation — *needed by Phase 7*
Affects currency formatting, number formatting, RTL support, and font subsetting.
**Working assumption:** English UI, `en-US` formatting, i18n scaffolding present from Phase 2
so adding Persian (including RTL) later is data, not refactoring.

### Q-02 · Production hosting target — *needed by Phase 8*
Affects deployment docs, secret management, backup strategy, and TLS termination.
**Working assumption:** a single Linux container host with managed PostgreSQL. Nothing in
the architecture depends on this.

### Q-03 · Account model: email/password only, or social login? — *needed by Phase 7*
**Working assumption:** email + password with rotating refresh tokens
([ADR-007](../adr/ADR-007-authentication.md)). The provider abstraction leaves room for OAuth
without redesign.

### Q-04 · Is a guest/anonymous first session desirable? — *needed by Phase 7*
Strongly improves funnel conversion, but adds account-linking complexity.
**Working assumption:** no guest mode in the slice; revisit with real funnel data from the
`tutorial_*` telemetry in `PLAYER_JOURNEY.md`.
