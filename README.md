# Tradeborn

> Build a trading city you can **watch work**.

Tradeborn is a persistent online economic strategy game that runs in the browser. You
extract resources, refine them through production chains, haul goods across your city, and
sell into a live market — and every one of those decisions happens visually in a stylised
3D city rather than in a table of numbers.

**Status: Phase 0 complete — design set written, technical prototype running.**

---

## The idea in one rule

> **Every number has a body.**

If a value changes on the server and nothing changes in the 3D city, the feature is
unfinished. That single rule is what separates this from the browser-strategy genre it
comes from, and it is enforced in review.

## Pillars

| | |
|---|---|
| **Visible Economy** | The 3D city is the primary interface. Numbers confirm what you already saw. |
| **Meaningful Scarcity** | You decide because something is *short*, never because a timer is long. |
| **Respectful Persistence** | The world runs while you are away and does not punish you for leaving. |
| **Readable Depth** | Deep systems, shallow interface. No manual required for the first hour. |

## Tech stack

**Backend** — ASP.NET Core 10 · C# · PostgreSQL · EF Core · Redis · SignalR
**Frontend** — Vue 3 · TypeScript · Vite · Pinia · Babylon.js 8 (WebGL2 default, WebGPU opt-in)
**Shape** — Modular monolith, one deployable artefact, server-authoritative economy

The SPA is built by MSBuild into the ASP.NET project's `wwwroot`, so `dotnet publish`
produces a single deployable — there is no separate frontend deployment and no CORS.

## Quick start

```bash
cd src/Tradeborn.Web/ClientApp && npm ci && npm run build
```

```bash
dotnet run --project src/Tradeborn.Web/Tradeborn.Web.csproj -p:SkipSpaBuild=true --urls http://localhost:5084
```

Open <http://localhost:5084>. Full instructions, including the hot-reload dev loop, in
[`docs/operations/LOCAL_DEVELOPMENT.md`](docs/operations/LOCAL_DEVELOPMENT.md).

## What runs today (Phase 0 prototype)

- Isometric camera — orbit, pan, zoom, 45° snap, elevation locked at 55°
- 8×8 plot grid on three terrain types, built from server-supplied data
- Five procedurally generated buildings with distinct silhouettes and animated parts
- Click selection of buildings and plots, with a contextual HUD card
- Interpolated day/night lighting cycle
- WebGL2 with opt-in WebGPU (`?webgpu=1`)
- `window.__tradeborn` debug bridge for end-to-end testing of the canvas

Measured: **472 KB gzip total initial JS** against a 1.1 MB budget; ~7 000 triangles
against a 150 000 budget.

## Repository layout

```
src/
  Tradeborn.Web/            ASP.NET Core host, API, SignalR
    ClientApp/              Vue 3 + TypeScript + Babylon.js
      src/game/             Babylon layer — never imports Vue
      src/ui/               HUD components
tests/                      Unit · Integration · Architecture (Phase 1)
docs/                       Design, architecture, economy, ADRs
tools/                      Economy simulator (Phase 6)
```

`Tradeborn.Domain`, `.Application`, and `.Infrastructure` arrive in Phase 1.

## Documentation

**Start here**
- [Game Vision](docs/vision/GAME_VISION.md) — what this is and is not
- [Vertical Slice](docs/game-design/VERTICAL_SLICE.md) — the scope contract
- [Implementation Plan](docs/roadmap/IMPLEMENTATION_PLAN.md) — phases and acceptance criteria
- [Current Status](CURRENT_STATUS.md) — exactly where the project stands

**Design** — [GDD](docs/game-design/GDD.md) · [Core Loops](docs/game-design/CORE_LOOPS.md) · [Player Journey](docs/game-design/PLAYER_JOURNEY.md)

**Economy** — [Design](docs/economy/ECONOMY_DESIGN.md) · [Resource Graph](docs/economy/RESOURCE_GRAPH.md) · [Balance Assumptions](docs/economy/BALANCE_ASSUMPTIONS.md)

**Architecture** — [Overview](docs/architecture/ARCHITECTURE.md) · [Time Model](docs/architecture/REALTIME_AND_TIME_MODEL.md) · [Security](docs/architecture/SECURITY_MODEL.md) · [Performance Budget](docs/architecture/PERFORMANCE_BUDGET.md)

**Art** — [Art Direction](docs/art-direction/ART_DIRECTION.md) · [Scene Guidelines](docs/art-direction/SCENE_GUIDELINES.md)

**Decisions** — [ADR index](docs/adr/) · [Risks](docs/roadmap/RISKS.md) · [Open decisions](docs/roadmap/DECISIONS_REQUIRED.md)

## Key architectural decisions

| Decision | Why |
|---|---|
| [Babylon.js](docs/adr/ADR-001-babylonjs.md) | TypeScript-native engine, not just a renderer; real WebGPU with WebGL2 fallback |
| [Modular monolith](docs/adr/ADR-002-modular-monolith.md) | Atomic economic transactions; boundaries enforced by tests, not process borders |
| [Deterministic Lazy Settlement](docs/adr/ADR-003-time-model.md) | Offline production with **zero** CPU cost for idle players, and no per-building tick |
| [Ledger, not event sourcing](docs/adr/ADR-004-economy-persistence.md) | Full audit and reconcilable balances at a fraction of the cost |
| [SignalR for notifications only](docs/adr/ADR-005-realtime.md) | A hub outage degrades responsiveness, never correctness |
| [Procedural meshes](docs/adr/ADR-006-asset-pipeline.md) | Zero copyright risk, zero asset budget, instantly re-skinnable |

## Design constraints

**The economy is server-authoritative.** The client sends *intent* (`build X on plot Y`),
never outcomes. Prices, costs, rewards, and timings are computed on the server, and the
client's clock is never trusted.

**No dark patterns.** No energy systems, streaks, decay, loot boxes, pay-to-win, or
manufactured urgency. See [Game Vision §8](docs/vision/GAME_VISION.md).

**Assets are original.** Every mesh is generated procedurally from primitives. No asset
enters the repository without a licence entry in
[the register](docs/art-direction/ART_DIRECTION.md).

## Licence

Not yet determined.
