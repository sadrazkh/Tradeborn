# Current Status

> Updated at the end of every phase and whenever work stops. Its purpose is that anyone —
> including a future session with no memory of this one — can resume in one read.

**Phase:** 0 complete · Phase 1 **in progress** (domain core done, persistence next)
**Branch:** `main`

## Phase 1 progress

| Step | State |
|---|---|
| 1. Confirm the prototype visually in a real browser | ⛔ blocked — see "Not verified" below |
| 2. `Tradeborn.Domain` project | ✅ |
| 3. `Money` value object, resource types, settlement engine | ✅ 26 unit tests passing |
| 4. `Tradeborn.Application` / `.Infrastructure` projects | ⬜ |
| 5. EF Core + PostgreSQL, initial migration | ⬜ |
| 6. Idempotent seed from `RESOURCE_GRAPH.md` §4 | ⬜ |
| 7. Auth (JWT + rotating refresh cookie) | ⬜ |
| 8. Serilog, OpenTelemetry, health checks, rate limiting | ⬜ |
| 9. Integration + Architecture test projects | ⬜ |
| 10. CI pipeline | ⬜ |
| 11. Replace `/api/prototype/city` with the real Cities endpoint | ⬜ |

**Domain core is complete and green:** `Money` (exact `long` cent arithmetic, throws rather
than going negative or wrapping), the resource/recipe/building model, and the Deterministic
Lazy Settlement engine. Zero external dependencies, warnings-as-errors, 26 passing tests
covering the published rates at every level, both determinism invariants, all three halt
limits, and clock-skew safety.

Three design decisions were changed during implementation because the code proved the
original plan wrong; all three are documented in the commit and the affected docs updated:
cycle-time scaling instead of quantity scaling, a fixed epoch-aligned sub-step grid instead
of a span-derived one, and end-of-step output commit.

---

## What works right now

Run it:

```bash
cd src/Tradeborn.Web/ClientApp && npm ci && npm run build && cd ../../.. && dotnet run --project src/Tradeborn.Web/Tradeborn.Web.csproj -p:SkipSpaBuild=true --urls http://localhost:5084
```

| Capability | State |
|---|---|
| ASP.NET Core 10 host serving the SPA from `wwwroot` | ✅ |
| `GET /health/live` | ✅ 200 |
| `GET /api/prototype/city` — server-supplied world layout | ✅ 200, valid JSON |
| Babylon.js scene, WebGL2 | ✅ |
| WebGPU opt-in via `?webgpu=1` with WebGL2 fallback | ✅ implemented, **fallback path not yet exercised on a WebGPU-capable browser** |
| Isometric camera: orbit / pan / zoom, 45° snap, β locked at 55° | ✅ (β verified as exactly 55.00°) |
| 8×8 plot grid, 3 terrain types, locked/unlocked plots | ✅ |
| 5 procedural buildings with distinct silhouettes | ✅ |
| Building selection + contextual HUD card | ✅ (verified via test bridge) |
| Day/night lighting interpolation | ✅ |
| `window.__tradeborn` debug bridge | ✅ (debug builds only; stripped from production) |
| Loading and error states | ✅ |

## Measurements

| Metric | Measured | Budget | Verdict |
|---|---|---|---|
| Initial JS, gzipped | **472 KB** (babylon 436 + vendor 27 + app 9) | ≤ 1 100 KB | ✅ 57 % headroom |
| Babylon chunk, gzipped | **436 KB** | ≤ 900 KB | ✅ |
| App shell, gzipped | **36 KB** | ≤ 180 KB | ✅ |
| Triangles in scene | **7 068** | ≤ 150 000 | ✅ |
| `dotnet build` | 0 warnings, 0 errors | 0 warnings | ✅ |
| `vue-tsc --noEmit` | 0 errors | 0 errors | ✅ |

**This substantially de-risks [R-02](docs/roadmap/RISKS.md) (Babylon bundle size).** The
tree-shaken engine came in at less than half its budget, so no mitigation is needed beyond
keeping the per-module import discipline.

## Not verified — and why

**Frame rate and draw calls have NOT been measured.** Neither browser surface available in
this environment composites frames: the in-app pane is not displayed, so
`requestAnimationFrame` is fully paused, and the Chrome extension is not connected. Readings
taken in that state (`fps: 60`, `drawCalls: 0`) are stale initial values, not measurements,
and are treated as unknown.

**No screenshot of the rendered scene exists.** The visual result is therefore unconfirmed.
What *is* confirmed is that the scene graph is correctly constructed from server data:
renderer backend, camera angles, triangle count, mesh hierarchy, building states and levels,
and programmatic selection all return correct values.

**Action for the next session:** open <http://localhost:5084> in a real browser, confirm the
city looks right, and record FPS and draw calls from the debug overlay. This is the first
Phase 1 task, and it gates whether [R-01](docs/roadmap/RISKS.md) (art quality) is on track.

## Bugs found and fixed in Phase 0

| Bug | Fix |
|---|---|
| Canvas backing buffer stuck at 300×150 when the canvas was first measured while hidden — scene would render at wrong resolution, stretched | Added a `ResizeObserver` on the canvas in `GameBridge`; a `window.resize` listener alone does not catch element-level size changes. Verified: backing buffer now tracks CSS size (1280×720) |
| Draw-call metric counted active meshes, which double-counts instances that collapse into one draw call — would have made the performance budget meaningless | Replaced with Babylon's `SceneInstrumentation.drawCallsCounter` |
| Debug bridge stripped from *all* built artefacts, leaving E2E unable to inspect the canvas | Added a `debug` Vite mode (`npm run build:debug`); production builds still strip it |
| `vue-tsc` errors: `location` unresolved in template, missing Node types | Extracted a `reload()` method; added `@types/node` |

## Phase 0 deliverables

- [x] 20 design documents (vision, GDD, economy, art, architecture, roadmap, testing, ops)
- [x] 8 ADRs
- [x] Technical prototype: scene, camera, selection, FPS overlay, WebGPU/WebGL2, ASP.NET integration
- [x] Build verified: `dotnet build` clean, `vue-tsc` clean, SPA builds
- [x] App verified running: health, API, and SPA all serve
- [ ] Visual confirmation — **blocked on a browser that composites**

## Next actions

1. **Open <http://localhost:5084> in a real browser.** Confirm the city looks right and
   record FPS and draw calls from the debug overlay. This gates whether
   [R-01](docs/roadmap/RISKS.md) (art quality) is on track and is the one thing that cannot
   be done from here.
2. Add `Tradeborn.Application` and `Tradeborn.Infrastructure` projects.
3. EF Core + PostgreSQL, initial migration, idempotent seed from
   [`RESOURCE_GRAPH.md`](docs/economy/RESOURCE_GRAPH.md) §4 — replacing the in-code
   `SliceEconomy` fixture the unit tests currently build from. The tests should keep passing
   unchanged; that is the point of keeping the numbers in one documented place.
4. Auth ([ADR-007](docs/adr/ADR-007-authentication.md)), observability, rate limiting.
5. Integration + Architecture test projects.
6. CI: build, test, bundle-size gate, gitleaks.
7. Replace `/api/prototype/city` with the real Cities endpoint.

**Environment notes carried forward:** PostgreSQL is available on `:5432`. Redis is **not**
installed — Phase 1 must boot without it ([A-01](docs/roadmap/DECISIONS_REQUIRED.md)). Docker
is **not** installed — integration tests must fall back to a local connection string via
`TRADEBORN_TEST_POSTGRES` ([R-09](docs/roadmap/RISKS.md)).

## Commits

| Phase | SHA | Message |
|---|---|---|
| 0 | `570c208` | `docs: define game vision, economy, architecture and phase plan` |
| 0 | `8d735b0` | `feat(prototype): validate Babylon scene inside ASP.NET host` |
| 1 | `09c608e` | `feat(economy): add domain core and deterministic settlement engine` |
