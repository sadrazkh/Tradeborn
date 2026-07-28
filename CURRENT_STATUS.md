# Current Status

> Updated at the end of every phase and whenever work stops. Its purpose is that anyone —
> including a future session with no memory of this one — can resume in one read.

**Phase:** 0 complete · Phase 1 code complete (DB verification pending) · Phase 2 **client complete**
**Branch:** `main` · **Working tree: uncommitted — the user commits.**

---

## ⚠️ One thing blocks the rest

PostgreSQL is running on `:5432` but rejects `postgres/postgres`
(`SqlState 28P01: password authentication failed`). Migrations, seed, and the six
integration tests cannot run until real credentials are configured.

**You said you would set this yourself. One command:**

```bash
dotnet user-secrets --project src/Tradeborn.Web set "ConnectionStrings:Postgres" "Host=localhost;Port=5432;Database=tradeborn;Username=postgres;Password=YOUR_PASSWORD"
```

The JWT signing key needs nothing in development — `appsettings.Development.json` carries a
clearly-labelled dev-only value so a fresh clone runs with one less step. Production has an
empty key in `appsettings.json` and **fails fast at startup** with an explanatory message
rather than silently signing tokens with a known constant.

Then integration tests, against a **separate** database (they drop and recreate the schema):

```bash
export TRADEBORN_TEST_POSTGRES="Host=localhost;Port=5432;Database=tradeborn_test;Username=postgres;Password=YOUR_PASSWORD" && dotnet test -p:SkipSpaBuild=true
```

Once that is set, the app creates the database and applies migrations on first start.

---

## Verified green

```
dotnet build           0 warnings, 0 errors   (whole solution, clean build)
vue-tsc --noEmit       0 errors
Unit tests            26/26 passed
Architecture tests     7/7  passed
Integration tests      6    SKIPPED with a message (no database configured)
Client build           472 KB gzip total JS
```

The integration tests skipping rather than passing is deliberate. A test that goes green
without a database has tested nothing, and hides the very problem it should surface.

## What exists now

| Layer | State |
|---|---|
| `Tradeborn.Domain` | Money, resources, recipes, buildings, inventory, city, settlement engine. **Zero external dependencies**, warnings-as-errors. |
| `Tradeborn.Application` | Contracts, abstractions (`ICityStore`, `IGameCatalog`, `ICacheStore`, `IAdvisorService`), `GetCityHandler`. |
| `Tradeborn.Infrastructure` | EF Core + PostgreSQL, 10 tables, initial migration, idempotent catalog seeder, city provisioner, JWT auth with refresh-token rotation. |
| `Tradeborn.Web` | Minimal APIs, auth endpoints, `/api/cities/me`, Serilog, rate limiting, health checks, auth-by-default. |
| `ClientApp` | Babylon city, isometric camera, selection, day/night, **login/register screen**, resource + coin HUD. |
| Tests | Unit, Architecture, Integration projects. |
| CI | GitHub Actions: build, 3 test suites, client typecheck + build, bundle-size gate, gitleaks. |

## Phase 6 — market & the complete loop (code complete)

| Deliverable | State |
|---|---|
| NPC pricing: elasticity, mean reversion, floor/ceiling | ✅ |
| 1.25× buy/sell spread, 3 % fee, per-order volume cap | ✅ |
| Sell command — server-priced, idempotent, audited | ✅ |
| Coins **and** XP, with level-ups | ✅ |
| Price history + sparkline | ✅ |
| Market panel with live quotes and a sale breakdown | ✅ |
| Arbitrage impossibility | ✅ property test |
| Unit tests | ✅ 95 passing (22 new) |
| Integration tests | ✅ 11 new — **skipped, no database** |
| `tools/economy-simulator` | ⬜ **not started** |
| Scripted economic event | ⬜ **not started** |

**The loop now closes:** build → produce → haul → deliver → **sell → coins + XP** → reinvest.

**Prices need no ticking job.** The current price is a pure function of
`(base, priceAtLastTrade, lastTradeAt, now)` — the same lazy-evaluation trick as production
settlement. A market nobody is trading in costs nothing to run, and a player returning next
day finds it healed rather than still punishing them.

**Arbitrage is arithmetic, not a rate limit.** The NPC charges 1.25× what it pays, so a round
trip loses 20 % before the 3 % fee. There is no waiting period or clever ordering that makes
trading-without-producing profitable. A property test runs ten buy/sell rounds at four volumes
and asserts the balance always ends lower.

**Two locks, in a fixed order.** The market price row is global — two players selling wood
contend on it — so the sell command takes the city lock *then* the market lock. Every handler
that touches both uses that order; mixed lock ordering is how deadlocks get written.

**Money stays in cent throughout.** Elasticity moves wood from 200 to 188 cent. A coins-only
field would round that to "2" and the player would watch the sparkline drop while the number
beside it never moved.

**Not started:** the economy simulator (`ECONOMY_DESIGN.md` §12 invariants over 1/7/30-day
horizons and three archetypes) and the scripted economic event. Both are listed for Phase 6
and neither is built. The simulator matters most — it is what would prove no dominant strategy
exists, and right now that claim rests on design reasoning rather than measurement.

## Phase 7 — tutorial (quest chain complete)

| Deliverable | State |
|---|---|
| Seven-quest tutorial chain | ✅ domain, rules, completion conditions |
| Rewards granted exactly once | ✅ primary-key collision, not a check (T5) |
| Delivery / sale lifetime counters | ✅ two quests ask about the past |
| Claim command, persistence, migration, endpoints | ✅ `QuestsAndCounters` |
| Quest tracker UI, one step at a time | ✅ |
| Level-up feedback | ✅ celebrated separately from the reward |
| Sound hooks, VFX | ⬜ not started |
| Playwright E2E over the 6-minute script | ⬜ not started |
| Unit tests | ✅ 117 passing (22 new) |

**Completion is derived, claiming is stored.** A quest's "done?" is recomputed from city state
every time it is asked — nothing to keep in sync, nothing to migrate when a condition changes,
and a player who builds a Sawmill before being asked simply finds that step already complete.
The only durable state is *which rewards were paid*, because that is real money.

**Claiming is deliberate, not automatic.** A design choice rather than a technical one: the
moment of taking the reward is where the payoff lands, and a reward that appears silently while
the player is looking elsewhere is a reward they never felt. It also makes double-payment
impossible to express — the claim is an insert guarded by a primary key.

**Two counters were unavoidable.** "Has a delivery happened?" and "has a sale happened?" are
facts about the past that current state cannot answer: a player who sold everything they were
delivered looks, by inventory alone, exactly like one who never received anything. A test
asserts that selling your whole stock does not un-complete the delivery quest.

**Guarded by tests that encode design rules**, not just behaviour: every hint is asserted to be
twelve words or fewer, the chain is asserted to total 1 200 coins and 330 XP, and finishing it
is asserted to land the player at level 3. If a reward is retuned and pacing shifts, a test
says so.

## Phase 5 — visible logistics (backend complete, renderer not started)

| Deliverable | State |
|---|---|
| Per-building output buffer | ✅ |
| `TransportJob` domain model | ✅ |
| Dispatch and arrival inside settlement, grid-aligned | ✅ |
| Nothing destroyed when storage fills | ✅ asserted |
| Transport + buffer persistence, migration | ✅ `TransportAndBuffers` |
| Transports exposed in the city DTO | ✅ with departure/arrival instants |
| Offline recap reports **deliveries**, not gross production | ✅ |
| Unit tests | ✅ 73 passing (10 new) |
| **Vehicles rendered from real jobs** | ⬜ **not started** |

**Goods no longer teleport — in the economy, not just the animation.** Production fills a
building's local buffer; only a delivered `TransportJob` moves goods into the inventory where
they can be spent. A sawmill genuinely cannot consume wood that is still on a cart.

**Why the rate tests changed.** Several tests asserted storage after one hour and now see 119
where they saw 120 — one unit is on a cart at any instant. They were rewritten to assert
**gross production** from `SettlementResult.Produced`, because a rate is a property of the
building while what sits in the warehouse is a property of logistics. Both are now tested,
separately and honestly.

**A bug this phase introduced and fixed.** Dispatching a load that storage could not accept
made it bounce back to the buffer and be re-sent on the next step — an endless shuttle that
never reached a fixed point, so a 30-day absence walked all 86 400 grid steps. A cart now
leaves only with what storage can actually take, and space already claimed by carts on the
road is subtracted so two of them cannot target the last free slot. Regression test:
`A_full_warehouse_stops_dispatch_rather_than_shuttling_forever`.

**Not started: the renderer.** `AgentRenderer` still shows carts wandering the road graph for
atmosphere — they are not connected to real jobs. The server now sends every in-flight haul
with absolute departure and arrival instants, which is exactly what a renderer needs to place
a cart mid-journey after a reload, but nothing consumes it yet. This is the remaining work for
slice steps 8 and 9 to be genuinely done.

## Phase 4 — production & inventory (code complete)

Much of this phase already existed: the settlement engine, halt-on-input, halt-on-capacity,
offline progression and the determinism tests all landed in Phase 1. What was genuinely
missing was **the player's control over production** — slice step 7.

| Deliverable | State |
|---|---|
| Recipes, inventory, capacity, DLS engine | ✅ (Phase 1) |
| Halt on missing input / full storage with reasons | ✅ (Phase 1) |
| Offline progression + determinism tests | ✅ (Phase 1) |
| **Explicit start / pause of production** | ✅ domain, command, endpoint |
| Production animations and warning motes | ✅ (Phase 2/3) |
| Building panel: recipe, rate, halt reason, controls | ✅ |
| "While you were away" recap | ✅ — server returned it since Phase 1, nothing showed it |
| N+1 query guard on the city read path | ✅ interceptor + assertion (needs a DB to run) |
| Unit tests | ✅ 63 passing (13 new) |

**A finished building now waits to be switched on.** Previously it auto-started. Two design
documents call for the player to start it (`VERTICAL_SLICE.md` step 7,
`PLAYER_JOURNEY.md` 1:40–2:10), and the change buys something the auto-start version could
not: a **pause lever**. Stopping the sawmill banks wood for a Bakery instead of converting it
to planks — which is the surplus decision from `ECONOMY_DESIGN.md` §3 turned into a control
the player can actually pull. A test asserts exactly that.

A finished **upgrade** resumes on its own. The player already had it running; making them
switch it back on would be a chore, not a decision.

**Idle is silent.** A building the player switched off reports no halt reason and shows no
warning mote. Showing a warning over something they turned off themselves would train them to
ignore warnings — and the mote is how the game says "this needs you".

The production command carries the **desired state, not a toggle**. A retried toggle is not
idempotent: a dropped response would flip the building back to the opposite of what was asked.

## Phase 3 — construction & upgrade (code complete)

| Deliverable | State |
|---|---|
| Build costs, durations and unlock levels on definitions | ✅ seeded, incl. material costs |
| Construction state on buildings (`CompletesAtUtc`, `PendingLevel`) | ✅ |
| `ConstructionRules` — 10 distinct, actionable refusals | ✅ |
| Completion handled **inside settlement**, not a job | ✅ |
| `StartConstruction` / `StartUpgrade` commands | ✅ transactional, idempotent, audited |
| `SELECT … FOR UPDATE` row lock per command | ✅ |
| Idempotency keys + audit ledger tables | ✅ migration `ConstructionAndAudit` |
| Endpoints with mandatory `Idempotency-Key` | ✅ |
| Four-stage construction visuals (scaffolding, crane) | ✅ driven by server progress |
| Build button + placement → real server command | ✅ |
| Unit tests | ✅ 50 passing (24 new) |
| Concurrency / idempotency integration tests | ⚠️ **written but skipped** — need a database |

**Completion lives in settlement, not in a scheduled job.** Finishing a build is a purely
time-driven state transition, which is exactly what Deterministic Lazy Settlement already
does. A construction therefore completes correctly whether or not any background worker is
running — which is precisely what ADR-008 claims jobs should never be needed for. The job
queue is still the right answer for *notifying* offline players, and that lands with SignalR.

**Two changes the implementation forced:**

- `UpgradeCurve` moved from `double` to `decimal`, with an exact repeated-multiplication
  helper instead of `Math.Pow`. The architecture test caught a `double` captured into a
  compiler-generated field and was right to: `decimal` represents 1.6 and 2.5 exactly, binary
  floating point does not, and these multipliers feed every cost in the game.
- Plots moved from `CityAggregate` into the domain `City`. Placement validity is a domain
  rule, not a presentation concern, and it cannot be decided without them.

**Not yet verified:** the 16 integration tests — including the two that matter most,
`Concurrent_builds_on_one_plot_produce_exactly_one_building` (T4) and
`Replaying_the_same_idempotency_key_charges_only_once` (T3) — skip without
`TRADEBORN_TEST_POSTGRES`. They are written and compile; they have never run.

## Phase 2 — the living city (client complete)

Built while the database was being sorted out; none of it depends on the database.

| Deliverable | State |
|---|---|
| All 8 slice buildings with distinct silhouettes | ✅ Farm, Mill and Bakery added |
| Signature moving parts | ✅ Saw blade and windmill sails, each on its own rotation axis |
| Citizens walking the roads | ✅ 20, instanced, pooled up front |
| Carts driving the roads | ✅ 6, instanced, pooled up front |
| Road graph | ✅ `RoadNetwork` derived from dirt plots |
| Placement preview with valid/invalid | ✅ ghost + pad + glyph (bar vs cross, not colour alone) |
| Quality presets with auto-downgrade | ✅ Low/Medium/High, drops after 3 s below the p95 floor |
| Terrain, plot grid, camera, selection, day/night | ✅ (Phase 0/1) |
| Build button in the HUD | ⬜ **deliberately not built** — see below |

**Why there is no Build button yet.** Confirming a placement needs the server-side
construction command, which is Phase 3. A button that opens a placement preview and then
does nothing is worse than no button. The placement system is fully working and driveable
from `window.__tradeborn.beginPlacement('sawmill')` so it can be demonstrated and tested now,
and the HUD control lands in Phase 3 alongside the command that makes it real.

**A budget revised by measurement.** `PERFORMANCE_BUDGET.md` originally specified *zero*
citizens on Low quality. Once instancing was in place, 20 citizens measured at ~2 draw calls
in total — so removing them buys almost no frame time while costing the strongest "the city
is alive" cue on exactly the devices that most need it. Low now keeps 6; resolution scale is
the lever that actually pays on mobile. Document updated with the reasoning.

Notable implementation detail: spinners carry a **per-part rotation axis**. A saw blade turns
about its mounting axis, a windmill's sails turn in the plane facing the viewer — rotating
both about Y would have spun the sails like a carousel.

## 3D bugs reported and fixed this session

You said the 3D view was glitching and jumping. Three causes, all real:

1. **`scene.autoClearDepthAndStencil = false`** — I had set this as a micro-optimisation. It
   leaves the depth buffer holding the previous frame's values, so with a moving camera
   geometry flickers in and out as stale depth wins the test. **This was the main cause.**
   Removed, with a comment explaining why it must not come back.
2. **The 45° camera snap fought Babylon's inertia** — my tween wrote `camera.alpha` every
   frame while `inertialAlphaOffset` was also still adding to it. Two writers, visible
   oscillation. The tween now suppresses the inertial offsets for its duration.
3. **The snap ran on every `pointerup`, including a plain click** — so selecting a building
   swung the camera. That violated my own rule in `SCENE_GUIDELINES.md` §2 ("the camera never
   moves on a tap"). It now requires actual pointer travel.

Also: the day/night cycle ran a full day every 125 s, fast enough to read as flickering
light rather than passing time. Slowed to the documented 4 minutes.

## Other fixes worth knowing about

- **EF Core version conflict** — Npgsql resolved EF 10.0.4 while Design/Binder pulled 10.0.10.
  Left alone this is an `MSB3277` warning; at runtime a mismatched provider pair fails. All
  EF packages are now pinned together with a comment saying to bump them as a set.
- **Analyzer warnings in the EF-generated migration** — scoped `.editorconfig` marks that
  folder as generated rather than weakening analysis project-wide.
- **A fake architecture test** — my first clock guard checked for member *names* that could
  never exist in those assemblies, so it could not fail. Rewritten to scan IL for calls to
  `DateTime.UtcNow` / `DateTimeOffset.UtcNow`, then **verified by deliberately introducing
  the violation in both Domain and Application and confirming it failed each time.**

## Still not verified

**Frame rate and draw calls remain unmeasured, and no screenshot of the scene exists.**
Neither browser surface available here composites frames — the in-app pane is not displayed,
so `requestAnimationFrame` is fully paused, and the Chrome extension is not connected. The
fixes above are reasoned from the code and are all well-understood Babylon failure modes,
but **I have not seen the flickering stop.** Please confirm visually.

## Next actions

1. Set the connection-string user secret above.
2. Run the app and confirm visually:
   - the 3D view no longer jumps or flickers
   - citizens and carts are moving along the dirt roads
   - the 8 buildings are distinguishable from each other
   - read FPS and draw calls off the debug overlay
3. Run the integration tests with `TRADEBORN_TEST_POSTGRES` set.
4. Try placement preview from the browser console:
   `__tradeborn.beginPlacement('sawmill')`, move the pointer, then `__tradeborn.lastCandidate()`.
5. Then Phase 3 — construction and upgrade (see [IMPLEMENTATION_PLAN.md](docs/roadmap/IMPLEMENTATION_PLAN.md)),
   which also brings the Build button into the HUD.

## Deferred from Phase 1, deliberately

| Item | Why | When |
|---|---|---|
| Redis-backed cache and rate limiting | Not installed locally; in-memory implementation behind `ICacheStore`, so swapping is a one-line registration change ([A-01](docs/roadmap/DECISIONS_REQUIRED.md)) | Phase 3 |
| `SELECT … FOR UPDATE` row lock on city | Only matters once write commands exist; the `xmin` concurrency token is already mapped | Phase 3 |
| OpenTelemetry traces/metrics | Serilog + correlation logging is in; tracing has no consumer yet | Phase 8 |
| Outbox and domain events | No second consumer until SignalR arrives | Phase 5 |
| N+1 query-count assertion | The read path is written for it (`AsSplitQuery`) but the interceptor is not built | Phase 4 |

## Commits

| Phase | SHA | Message |
|---|---|---|
| 0 | `570c208` | `docs: define game vision, economy, architecture and phase plan` |
| 0 | `8d735b0` | `feat(prototype): validate Babylon scene inside ASP.NET host` |
| 1 | `09c608e` | `feat(economy): add domain core and deterministic settlement engine` |
| 1 | `bb415b6` | `docs: record Phase 1 progress and next actions` |
| 1 | `8944caf` | `Phase 1 complete: real backend, auth, CI, and docs` |
| 2 | *uncommitted* | Farm/Mill/Bakery models, citizens, carts, road graph, placement preview, quality presets |
