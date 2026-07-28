# Architecture

> **Status:** Approved (Phase 0) · Backed by [ADR-002](../adr/ADR-002-modular-monolith.md).

## 1. Shape

**Modular monolith.** One deployable unit, hard internal module boundaries, enforced by
tests rather than by process boundaries.

```
┌──────────────────────── Tradeborn.Web (single deployable) ────────────────────────┐
│                                                                                    │
│   ClientApp/  Vue 3 + TS + Babylon.js  ──build──►  wwwroot/  (served by Kestrel)   │
│                                │                                                   │
│                          HTTPS │ + SignalR                                         │
│                                ▼                                                   │
│   Minimal API endpoints · SignalR hubs · IHostedService workers                    │
│                                │                                                   │
├────────────────────────────────┼───────────────────────────────────────────────────┤
│   Tradeborn.Application        ▼   use cases, validation, orchestration            │
├────────────────────────────────────────────────────────────────────────────────────┤
│   Tradeborn.Domain                 entities, value objects, economy rules          │
│                                    ZERO external dependencies                      │
├────────────────────────────────────────────────────────────────────────────────────┤
│   Tradeborn.Infrastructure         EF Core · PostgreSQL · Redis · outbox · seed    │
└────────────────────────────────────────────────────────────────────────────────────┘
                                     │                    │
                              PostgreSQL              Redis (optional in dev)
```

**Dependency rule:** `Web → Application → Domain`, `Infrastructure → Application, Domain`.
Domain depends on nothing. Enforced by `Tradeborn.ArchitectureTests` — a violation fails CI.

## 2. Why not the structure in the brief

The brief proposed 7 `src` projects. Reduced to 4:

| Dropped | Reason |
|---|---|
| `Tradeborn.Contracts` | DTOs live in `Application/Contracts/`. A separate assembly earns its keep when a *second* consumer exists. Today there is one. |
| `Tradeborn.Workers` | Workers are `IHostedService` in `Web`. Correctness does not depend on them (`REALTIME_AND_TIME_MODEL.md` §6), so they can be extracted later with no redesign. |
| `Tradeborn.GameClient` | Lives at `Web/ClientApp/` so MSBuild builds and publishes it as one artefact — the brief's integration requirement. |

Extraction triggers are recorded in [ADR-002](../adr/ADR-002-modular-monolith.md). This is
deferral, not omission.

## 3. Solution layout

```
Tradeborn.slnx
src/
  Tradeborn.Domain/            Buildings/ Economy/ Cities/ Production/ Market/ Progression/
                               Common/ (Money, ResourceAmount, EntityId)
  Tradeborn.Application/       Abstractions/ Contracts/ Cities/ Construction/ Production/
                               Logistics/ Market/ Quests/ Behaviors/
  Tradeborn.Infrastructure/    Persistence/ (DbContext, Configurations, Migrations)
                               Seed/ Caching/ Outbox/ Time/ Identity/
  Tradeborn.Web/               Endpoints/ Hubs/ Workers/ Middleware/ Program.cs
                               ClientApp/   ← Vue + Babylon
tests/
  Tradeborn.UnitTests/         Domain + Application, no I/O, FakeTimeProvider
  Tradeborn.IntegrationTests/  Real PostgreSQL via Testcontainers, WebApplicationFactory
  Tradeborn.ArchitectureTests/ Dependency + naming + banned-API rules
docs/  tools/
```

## 4. Modules

Modules are **folders with enforced boundaries**, not assemblies. All 19 modules from the
brief are represented, but only those marked ● have code in the vertical slice.

| Module | Slice | Responsibility |
|---|---|---|
| Identity | ● | Registration, login, tokens |
| Players | ● | Player profile, level, XP |
| Cities | ● | City aggregate, plots, settlement entry point |
| Buildings | ● | Definitions, instances, levels |
| Construction | ● | Placement validation, build/upgrade queue |
| Resources | ● | Resource definitions |
| Production | ● | Recipes, orders, DLS engine |
| Inventory | ● | Balances, capacity |
| Logistics | ● | Transport jobs, routes |
| Market | ● | NPC pricing, orders, price history |
| Economy | ● | Money, ledger, audit |
| Quests | ● | Chains, objectives, rewards |
| Progression | ● | XP, levels, unlocks |
| Events | ○ | Economic events (stub + extension point) |
| Notifications | ○ | In-game messages |
| Realtime | ● | SignalR hubs |
| Administration | ○ | Admin panel (Phase 8) |
| Analytics | ○ | Telemetry (Phase 8) |
| World | ○ | Region/world map (post-slice) |

Cross-module rules:
- Modules communicate through `Application` services or domain events — never by reaching
  into another module's entities.
- Only `Cities` may call `Settle`. Every other module receives an already-settled city.
- `ArchitectureTests` assert no `Domain.<ModuleA>` type references `Domain.<ModuleB>`
  internals outside declared shared kernel types (`Money`, `ResourceAmount`, `EntityId`).

## 5. Domain model (slice)

```
Player ──1:1── City ──1:N── Plot ──0:1── Building ──0:1── ProductionOrder
   │              │                           │
   │              ├──1:N── InventoryItem      └──0:1── ConstructionJob
   │              ├──1:N── TransportJob
   │              └──1:1── CityProgress (level, xp)
   └──1:N── QuestProgress

MarketState ──1:N── MarketPrice ──1:N── PriceHistoryPoint      (global, not per-player)
AuditLedgerEntry                                               (append-only)
OutboxMessage                                                  (append-only, drained)
IdempotencyKey
```

**Aggregate roots:** `City` (transactional boundary), `Player`, `MarketState`.

**Shared kernel value objects:**
- `Money` — wraps `long` cent. Arithmetic is checked; no implicit conversion from
  `double`/`decimal`. Negative balances throw.
- `ResourceAmount` — `(ResourceId, long Quantity)`.
- Strongly-typed ids (`CityId`, `BuildingId`, …) as `readonly record struct`, preventing
  the classic "passed the wrong Guid" bug.

## 6. Request pipeline

```
HTTP → Rate limiter → Auth → Correlation ID → Idempotency filter
     → Endpoint → Validator (FluentValidation)
     → Handler:  BEGIN TX
                   lock city (FOR UPDATE)
                   Settle(city, TimeProvider.Now)
                   validate against settled state
                   apply domain operation
                   append audit ledger entry
                   enqueue outbox message
                 COMMIT
     → Outbox drainer → SignalR push
     → Response (+ serverTimeUtc)
```

Every economic write follows this exact path. There is no second way to change a balance.

**Error contract:** RFC 9457 `application/problem+json` everywhere, with a stable
machine-readable `code` (`INSUFFICIENT_FUNDS`, `PLOT_OCCUPIED`, `PRICE_MOVED`,
`CAPACITY_EXCEEDED`, …) that the client maps to localised text and a specific visual.

## 7. Frontend architecture

Vue owns the HUD. Babylon owns the world. They meet at one narrow seam.

```
ClientApp/src/
  game/                        ← Babylon. NO Vue imports allowed.
    engine/      EngineBootstrap, SceneManager, QualityManager, PerformanceMonitor
    camera/      IsometricCameraController, InputController (mouse + touch)
    world/       TerrainRenderer, PlotGrid, RoadRenderer, PropRenderer
    entities/    BuildingRenderer, VehicleRenderer, CitizenRenderer
    systems/     SelectionSystem, PlacementSystem, AnimationCoordinator,
                 EffectsManager, AudioManager
    assets/      AssetManager, ModelRegistry, MaterialLibrary
    bridge/      GameBridge  ← the ONLY seam between Vue and Babylon
    debug/       DebugOverlay, TestBridge (window.__tradeborn)
  ui/            Vue components, HUD, panels, design system
  stores/        Pinia — server state mirror
  api/           Typed HTTP client, SignalR client
```

**Hard rules (enforced in review and by lint):**
1. Nothing in `game/` imports from `vue`.
2. No Babylon object ever enters Vue reactivity. `GameBridge` holds the engine in
   `shallowRef` + `markRaw`. A `Mesh` inside a `ref` makes Vue deep-proxy the entire scene
   graph — this is the single most likely cause of catastrophic frame drops.
3. Vue → Babylon communication is by **command**; Babylon → Vue is by **typed event**.
   No shared mutable object.
4. The render loop never allocates. Object pooling for vehicles, particles, and citizens.

## 8. Technology decisions

| Choice | Decision | ADR |
|---|---|---|
| 3D engine | Babylon.js 8.x | [ADR-001](../adr/ADR-001-babylonjs.md) |
| Architecture | Modular monolith | [ADR-002](../adr/ADR-002-modular-monolith.md) |
| Time model | Deterministic Lazy Settlement | [ADR-003](../adr/ADR-003-time-model.md) |
| Persistence | Relational + audit ledger + outbox (no event sourcing) | [ADR-004](../adr/ADR-004-economy-persistence.md) |
| Real-time | SignalR, notifications only, polling fallback | [ADR-005](../adr/ADR-005-realtime.md) |
| Assets | glTF/GLB + Draco + KTX2, progressive | [ADR-006](../adr/ADR-006-asset-pipeline.md) |
| Auth | JWT access + rotating refresh cookie | [ADR-007](../adr/ADR-007-authentication.md) |
| Background work | IHostedService + DB queue, `SKIP LOCKED` | [ADR-008](../adr/ADR-008-background-processing.md) |

## 9. Deployment

One container. `dotnet publish` runs `npm ci && npm run build`, emits the SPA into
`wwwroot/`, and packages everything into a single image. No separate frontend deployment,
no CORS, no CDN required to run.

External dependencies: **PostgreSQL** (required), **Redis** (optional in dev — the app boots
and runs correctly without it, degrading to in-memory caching; required from Phase 3 in
production).

## 10. AI extension point (not built in the slice)

`IAdvisorService` is defined in `Application/Abstractions/` with a no-op implementation.
Nothing in the critical path calls it. When a real advisor arrives it consumes the audit
ledger and market history — both of which already exist for other reasons. No paid AI call
will ever sit in a gameplay request path.
