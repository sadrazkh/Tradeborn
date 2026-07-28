# Local Development

> Every command here has been executed on the development machine. Where something has not
> been verified, it says so.

## Prerequisites

| Tool | Version | Verified |
|---|---|---|
| .NET SDK | 10.0.302 | ✅ |
| Node.js | 22.13.0 | ✅ |
| npm | 10.9.2 | ✅ |
| PostgreSQL | listening on `:5432` | ✅ (required from Phase 1) |
| Redis | `:6379` | ❌ not installed — optional until Phase 3 ([A-01](../roadmap/DECISIONS_REQUIRED.md)) |
| Docker | — | ❌ not installed — only needed for Testcontainers and CI ([R-09](../roadmap/RISKS.md)) |

## First run

```bash
git clone <repo> && cd Tradeborn
```

```bash
cd src/Tradeborn.Web/ClientApp && npm ci && cd ../../..
```

Build the SPA into `wwwroot`, then start the server:

```bash
cd src/Tradeborn.Web/ClientApp && npm run build && cd ../../.. && dotnet run --project src/Tradeborn.Web/Tradeborn.Web.csproj -p:SkipSpaBuild=true --urls http://localhost:5084
```

Then open <http://localhost:5084>.

## Day-to-day: two-process dev loop (recommended)

Vite's dev server gives hot-module reload and proxies `/api` and `/health` to Kestrel, so
the SPA and API behave as one origin exactly as they do in production.

Terminal 1 — API:

```bash
dotnet run --project src/Tradeborn.Web/Tradeborn.Web.csproj -p:SkipSpaBuild=true --urls http://localhost:5084
```

Terminal 2 — client with HMR:

```bash
cd src/Tradeborn.Web/ClientApp && npm run dev
```

Open <http://localhost:5173>. Edits to `.vue` / `.ts` reload instantly.

> `-p:SkipSpaBuild=true` stops MSBuild rebuilding the SPA on every backend build. Omit it
> only when you want the published artefact.

## Build modes

| Command | Mode | `window.__tradeborn` | Debug overlay |
|---|---|---|---|
| `npm run dev` | development | present | shown |
| `npm run build:debug` | debug | present | shown |
| `npm run build` | production | **stripped** | hidden |

`build:debug` exists so end-to-end tests can inspect the Babylon scene from a *built*
artefact (TEST_STRATEGY.md §6). The production build must never expose the bridge.

## Verifying it works

```bash
curl -s -o /dev/null -w "health %{http_code}\n" http://localhost:5084/health/live && curl -s http://localhost:5084/api/prototype/city | head -c 200
```

Expected: `health 200`, then JSON beginning `{"name":"Riverbend","gridSize":8,...`.

In the browser console:

```js
__tradeborn.renderer()   // "webgl2"  (or "webgpu" with ?webgpu=1)
__tradeborn.buildings()  // 5 buildings supplied by the server
```

## Renderer selection

WebGL2 is the default path ([R-07](../roadmap/RISKS.md)). WebGPU is opt-in:

| URL | Renderer |
|---|---|
| `http://localhost:5084` | WebGL2 |
| `http://localhost:5084/?webgpu=1` | WebGPU, falling back to WebGL2 if unsupported |

Testing the fallback deliberately is the point — it must never be exercised only by accident.

## Useful commands

```bash
dotnet build -p:SkipSpaBuild=true
```

```bash
cd src/Tradeborn.Web/ClientApp && npm run typecheck
```

```bash
dotnet publish src/Tradeborn.Web/Tradeborn.Web.csproj -c Release
```

Publish runs `npm ci && npm run build` automatically and emits one self-contained artefact —
there is no separate frontend deployment ([ADR-002](../adr/ADR-002-modular-monolith.md)).

## Ports

| Port | Service |
|---|---|
| 5084 | Kestrel (API + SPA in production mode) |
| 5173 | Vite dev server (development only) |
| 5432 | PostgreSQL |
| 6379 | Redis (optional until Phase 3) |

## Troubleshooting

**Blank page, console shows a 404 for `/assets/...`** — `wwwroot` is empty or stale. Run
`npm run build` in `ClientApp`. `wwwroot` is generated and gitignored, so a fresh clone
always needs one client build.

**Scene renders stretched or at low resolution** — the canvas backing buffer is out of sync
with its CSS size. `GameBridge` installs a `ResizeObserver` for exactly this; if it recurs,
check that the observer is still attached after hot reload.

**`npm ci` fails** — delete `node_modules` and `package-lock.json`, then `npm install`.

**Port 5084 in use** — pass a different `--urls`, and update `server.proxy` in
`vite.config.ts` to match.

## Not yet applicable

Database setup, migrations, and seeding arrive in **Phase 1**; this document is updated
then. There is currently no database dependency — the Phase 0 prototype serves a
hand-authored city from memory.
