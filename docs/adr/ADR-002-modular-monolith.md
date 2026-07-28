# ADR-002 — Modular monolith, four `src` projects

**Status:** Accepted (Phase 0)

## Context

The brief demands a modular monolith, forbids microservices, *and* forbids premature
over-architecture — while also listing 19 modules and 7 projects. Those pull in opposite
directions and must be reconciled explicitly.

## Decision

A modular monolith with **four** `src` projects:

```
Tradeborn.Domain          entities, value objects, economy rules — zero dependencies
Tradeborn.Application     use cases, validation, orchestration, DTOs
Tradeborn.Infrastructure  EF Core, PostgreSQL, Redis, outbox, seed
Tradeborn.Web             endpoints, hubs, hosted workers, ClientApp/
```

All 19 modules exist as **folders with test-enforced boundaries**, not assemblies.

## Rationale

**Why a monolith:** one team, one deployable, one database, unknown load. Microservices
would add distributed transactions to an economy whose correctness depends on atomicity —
paying a large cost to solve a problem we do not have.

**Why folders instead of assemblies:** an assembly boundary is only worth its cost when it
prevents something a test cannot. `ArchitectureTests` enforce the same rules with faster
builds and no `InternalsVisibleTo` gymnastics.

**Why `Contracts` was dropped:** a shared contracts assembly earns its keep when a *second*
consumer exists. Today there is one. DTOs live in `Application/Contracts/`.

**Why `Workers` was dropped:** workers are `IHostedService` in `Web`. Crucially, correctness
does not depend on them — the DLS model means a dead worker delays notifications but never
corrupts state ([ADR-003](ADR-003-time-model.md)). That property is what makes extraction a
later, safe, mechanical operation.

**Why `GameClient` was dropped:** the brief requires a single integrated build. Placing the
SPA at `Web/ClientApp/` and building it from MSBuild delivers exactly that.

## Extraction triggers

Split only when one of these is *observed*, not anticipated:

| Extract | When |
|---|---|
| `Tradeborn.Workers` | Background work needs independent scaling or a separate deploy cadence |
| `Tradeborn.Contracts` | A second consumer exists (mobile client, public API, partner integration) |
| A module into a service | It has a genuinely different scaling profile *and* an owning team |

## Consequences

**Positive:** fast builds, simple debugging, atomic transactions across modules, trivial
local setup, one deployment artefact.

**Negative:** boundaries can erode. Mitigated by `ArchitectureTests` running in CI —
violations fail the build rather than accumulating in review comments.

**Negative:** the whole app scales as one unit. Acceptable and correct at slice scale; the
first bottleneck will be PostgreSQL, and the DLS model already means idle players cost zero.
