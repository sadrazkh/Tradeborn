# Current Status

> Updated at the end of every phase and whenever work stops. Its purpose is that anyone —
> including a future session with no memory of this one — can resume in one read.

**Phase:** 0 complete · Phase 1 **code complete, database verification pending**
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

1. Set the two user secrets above.
2. Run the app, confirm the 3D view no longer jumps, and read FPS/draw calls off the debug
   overlay. This also closes the last open item from Phase 0.
3. Run the integration tests with `TRADEBORN_TEST_POSTGRES` set.
4. Then Phase 2 — the living city (see [IMPLEMENTATION_PLAN.md](docs/roadmap/IMPLEMENTATION_PLAN.md)).

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
| 1 | *uncommitted* | 3D fixes, persistence, auth, endpoints, architecture + integration tests, CI |
