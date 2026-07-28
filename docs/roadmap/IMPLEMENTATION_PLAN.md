# Implementation Plan

> **Status:** Approved (Phase 0) · Live document — updated at the end of every phase.
> Current state always in [`../../CURRENT_STATUS.md`](../../CURRENT_STATUS.md).

## Rules

1. A phase is done when its acceptance criteria pass **and the app actually runs**.
2. No phase is closed with empty files, stubs, or TODOs standing in for behaviour.
3. Every phase ends with: build ✅, tests ✅, app run ✅, docs updated, phase report, commit SHA.
4. Later phases may be re-scoped; earlier ones may not be skipped.

---

## Phase 0 — Discovery, design & technical validation

**Goal:** decide everything expensive to change later, and prove Babylon.js works inside
ASP.NET before committing to it.

**Deliverables**
- [x] Vision, pillars, audience
- [x] GDD, core loops, vertical slice, player journey
- [x] Economy design, resource graph, balance assumptions
- [x] Art direction, scene guidelines
- [x] Architecture, time model, security model, performance budget
- [x] Roadmap, risks, decisions required
- [x] Test strategy, local development, README
- [x] 8 ADRs
- [ ] **Technical prototype**: ASP.NET host + Vue + Babylon scene, isometric camera,
      placeholder building, click selection, FPS/backend overlay, WebGPU→WebGL2 fallback,
      served from ASP.NET as one build

**Acceptance:** `dotnet run` serves a page showing a 3D scene at 60 fps with working camera
and selection, reporting its renderer backend.
**Risk:** Babylon bundle size; WebGPU instability. Mitigated by measuring both in the prototype.

---

## Phase 1 — Foundation

**Goal:** clone → one command → running stack.

**Deliverables**
- Solution: `Domain`, `Application`, `Infrastructure`, `Web` + 3 test projects
- PostgreSQL via EF Core; initial migration; **idempotent** seed of resources/buildings/recipes
- Redis wired as optional (in-memory fallback when absent)
- Auth: register/login, JWT + rotating refresh cookie
- Serilog structured logging + correlation id; OpenTelemetry; health checks; rate limiting
- Vite + Vue + TS + Pinia + router integrated into MSBuild publish
- CI: build, test, lint, bundle-size gate, gitleaks
- Smoke tests: health endpoint, login round-trip, SPA served

**Acceptance:** clean clone → documented command → app on `https://localhost:xxxx`, health
green, register/login works, SPA served by Kestrel. All tests green.

---

## Phase 2 — The living city

**Goal:** the city looks alive before it *is* an economy. Front-loaded deliberately — this
is where the project's biggest risk lives.

**Deliverables**
- Terrain + 8×8 plot grid (merged mesh)
- `IsometricCameraController`: orbit/pan/zoom, 45° snap, bounds clamp, mouse + touch
- `SelectionSystem` and `PlacementSystem` with ghost + valid/invalid indicator
- 3–4 procedural buildings from the modular kit
- Thin-instanced props; simple citizens; 1–2 moving vehicles (cosmetic)
- Light day/night cycle; ambient animation
- HUD shell: resources, player, build button, contextual card
- `QualityManager`, `PerformanceMonitor`, debug overlay, `window.__tradeborn` bridge

**Acceptance:** the city reads as alive and pleasant with **no economy behind it**;
≥ 50 fps p95 desktop, ≥ 25 fps mobile; within draw-call and triangle budgets.

---

## Phase 3 — Construction & upgrade

- `BuildingDefinition` / `BuildingInstance` domain model
- Server-side placement validation (plot free, unlocked, affordable, prerequisites)
- `StartConstruction` / `StartUpgrade` commands: idempotent, transactional, audited
- Server-time completion; scheduled-jobs table + hosted worker
- 4-stage construction visuals driven by server progress
- Per-level meshes; upgrade changes the model visibly
- Build queue (1 slot at city level 1)
- **Concurrency tests**: 20 parallel builds on one plot → exactly 1 succeeds

**Acceptance:** slice steps 5, 6, 12, 13 pass; no double-spend; refresh mid-construction
resumes at the correct stage.

---

## Phase 4 — Production & inventory

- Recipes, production orders, inventory, capacity
- **Deterministic Lazy Settlement** engine with bounded sub-stepping
- Halt on missing input / full storage, with `HaltReason` surfaced visually
- Offline progression
- Production animations; warning motes
- Deterministic economy tests: one 8 h jump == 480 × 1 min jumps
- N+1 query guard on the city read path

**Acceptance:** slice steps 7 and 17 pass; determinism test green; idle players cost 0 CPU.

---

## Phase 5 — Visible logistics

- `TransportJob` domain model; road graph; A* or waypoint routing
- Server-side dispatch and delivery (arrival is a scheduled event)
- Pooled vehicle rendering; load/unload animations
- Client reconciliation: in-flight transports resume at correct interpolated position
- Recovery after reconnect; killed animation never affects economy

**Acceptance:** slice steps 8, 9 pass; closing the tab mid-haul still delivers.

---

## Phase 6 — Market & the complete loop

- NPC market: server-side pricing, elasticity, mean reversion, floor/ceiling
- Buy/sell with idempotency; 1.25× spread; 3 % fee; volume caps
- Transaction log, price history, sparkline
- Coins + XP; level-ups; one scripted economic event
- `tools/economy-simulator`: 1/7/30-day runs, 3 archetypes, invariant report
- Exploit tests: arbitrage property test, price tampering, replay

**Acceptance:** **the vertical slice is playable end to end.** Simulator reports all
invariants from `ECONOMY_DESIGN.md` §12 satisfied.

---

## Phase 7 — Tutorial & polish

- 7-quest contextual tutorial chain with in-world highlights
- Session recap / offline summary
- Sound hooks, VFX, feedback animations, level-up moment
- Loading screen, error states, empty states
- Mobile polish; accessibility pass; `prefers-reduced-motion`
- Playwright E2E over the full 6-minute demo script; FPS p95 assertion

**Acceptance:** an unbriefed player reaches first sale in < 6 min; all slice acceptance
criteria in `VERTICAL_SLICE.md` §5 pass.

---

## Phase 8 — Admin & operations

- Role-based admin: players, city inspection, economy tuning, recipes, prices
- Audit log viewer, feature flags, job monitoring, health, online players
- Limited resource grants and test-city reset (audited)
- Run the economy simulator from the panel
- Metrics dashboards, backup and deployment documentation

**Acceptance:** economy retunable without a deploy; every admin action audited.

---

## Phase 9 — Load, hardening & next roadmap

- Load test (target 1 000 concurrent, 10 k registered)
- Hotspot analysis; DB indexes; Redis strategy; SignalR scale-out plan
- Asset optimisation (Draco, KTX2, bundles)
- Security review against T1–T12; tech-debt report
- Roadmap for alliances, player market, world map

**Acceptance:** documented capacity limits and a costed plan for the next milestone.

---

## Sequencing rationale

Phase 2 (visuals) comes **before** phase 3 (mechanics) deliberately. The brief's priority
order puts visual appeal first, and the largest risk in this project is that the 3D city
fails to feel good — not that the economy fails to compute. Finding that out in week 2 is
recoverable; finding out in week 8, after building six systems on top of it, is not.

## Extension points reserved (no code)

`IAdvisorService` (AI) · `IWorldRegionProvider` (world map) · `ITradePartner` (P2P market) ·
`IAllianceContext` · `IEventScheduler` (systemic events). Interfaces defined where they cost
nothing; implementations deferred.
