# Performance Budget

> **Status:** Approved (Phase 0) · Budgets are **derived from vertical-slice scope**, not
> copied from a blog post. §1 shows the derivation. Every budget has an owner phase and an
> automated check.

## 1. Scope the budget is derived from

| Element | Slice count | Tris each | Total tris | Rendering strategy |
|---|---|---|---|---|
| Terrain + plot grid | 1 (8×8) | — | ~6 000 | Single merged mesh |
| Buildings | ≤ 30 | ~800 | 24 000 | Per-type instancing |
| Trees / rocks / props | ~120 | ~200 | 24 000 | **Thin instances** (1 draw call/type) |
| Vehicles | ≤ 10 | ~400 | 4 000 | Instanced + pooled |
| Citizens | ≤ 20 | ~300 | 6 000 | Instanced + pooled |
| Roads | ~40 segments | ~50 | 2 000 | Merged into one mesh |
| **Total** | | | **~66 000** | |

Budget set at **2× measured scope** to leave headroom for polish:

## 2. Rendering budgets

| Metric | Desktop | Mobile | Hard fail |
|---|---|---|---|
| Triangles (visible) | ≤ 150 000 | ≤ 80 000 | 250 000 |
| Draw calls | ≤ 150 | ≤ 80 | 250 |
| Active materials | ≤ 25 | ≤ 15 | 40 |
| Shadow casters | ≤ 20 | **0** (baked/blob) | 30 |
| Lights (real-time) | ≤ 3 | ≤ 2 | 4 |
| Particle systems (live) | ≤ 8 | ≤ 4 | 12 |
| Texture memory | ≤ 128 MB | ≤ 48 MB | 192 MB |

**Mobile has zero real-time shadows.** A single directional shadow map costs more on a
mid-range phone than every other draw call combined. Mobile uses a blob-shadow decal on a
merged mesh instead — visually adequate at isometric distance.

## 3. Frame rate targets

| Device class | Target | p95 floor | Action if missed |
|---|---|---|---|
| Desktop (integrated GPU, 2020+) | 60 fps | 50 fps | Drop quality preset |
| Desktop (discrete GPU) | 60 fps (capped) | 60 fps | — |
| Mobile mid-range (Snapdragon 7xx / A12) | 30 fps | 25 fps | Auto-drop to Low |
| Mobile low-end | 30 fps @ 0.75 res scale | 20 fps | Auto-drop + disable particles |

`QualityManager` samples a 120-frame rolling average and steps the preset down after 3 s
sustained below floor. It steps **up** only on an explicit user action — automatic
oscillation is worse than being one tier too low.

## 4. Loading budgets

| Metric | Target | Hard fail | Measured on |
|---|---|---|---|
| First Contentful Paint (HUD shell) | ≤ 1.2 s | 2.5 s | Desktop broadband |
| **First Meaningful Render** (scene visible + camera responsive) | ≤ 4.0 s | 7.0 s | Desktop broadband |
| First Meaningful Render | ≤ 8.0 s | 14.0 s | Mid mobile, 4G |
| Time to Interactive (can place a building) | ≤ 5.0 s | 9.0 s | Desktop broadband |

### Bundle budgets (gzipped)

| Chunk | Target | Hard fail |
|---|---|---|
| App shell (Vue + router + Pinia + UI) | ≤ 180 KB | 250 KB |
| Babylon core (tree-shaken, lazy chunk) | ≤ 900 KB | 1 200 KB |
| **Total initial JS** | **≤ 1.1 MB** | 1.5 MB |
| Initial asset payload (models + textures) | ≤ 3.5 MB | 6 MB |

Babylon is imported **per-module** (`@babylonjs/core/Meshes/mesh`), never as
`import * from '@babylonjs/core'` — the barrel import defeats tree-shaking and roughly
triples the chunk. A lint rule bans the barrel import.

The HUD shell renders before Babylon loads, so FCP is not blocked by the engine chunk.

## 5. Memory budgets

| Metric | Desktop | Mobile |
|---|---|---|
| JS heap after 10 min play | ≤ 400 MB | ≤ 250 MB |
| Heap growth per 10 min (leak guard) | ≤ 15 MB | ≤ 10 MB |

Leak guard is the important one: a city session runs for hours. Object pooling for
vehicles, citizens and particles is mandatory, and the render loop must not allocate.

## 6. Backend budgets

| Endpoint | p50 | p95 | Hard fail |
|---|---|---|---|
| `GET /api/cities/me` (settle + serialise) | ≤ 40 ms | ≤ 120 ms | 400 ms |
| `POST` economic command | ≤ 50 ms | ≤ 150 ms | 500 ms |
| Settlement CPU, 30-day offline gap | ≤ 15 ms | ≤ 40 ms | 100 ms |
| SignalR fan-out to 1 000 clients | ≤ 200 ms | ≤ 500 ms | 2 s |

Additional invariants:
- **Zero N+1 queries** on the city read path. Verified by asserting query count in an
  integration test — not by inspection.
- City read is **one round trip** (single query with includes, or split query measured to
  be faster).
- Idle players cost **0** CPU (guaranteed by the DLS model).

## 7. Quality presets

| | Low | Medium | High |
|---|---|---|---|
| Resolution scale | 0.75 | 1.0 | 1.0 (up to 2.0 DPR) |
| Shadows | off | blob | 1024 shadow map |
| Particles | off | 4 systems | 8 systems |
| Citizens | 6 | 12 | 20 |
| Prop density | 40 % | 70 % | 100 % |
| Anti-aliasing | off | FXAA | FXAA |
| Post-processing | off | off | bloom (subtle) |
| Texture tier | 512 | 1024 | 1024 |

Default: **Medium on desktop, Low on mobile**, then auto-adjusted on measurement. Users can
override manually and the override is remembered.

> **Revised in Phase 2.** Low originally specified *zero* citizens. Once instancing was in
> place, 20 citizens measured at ~2 draw calls in total — so removing them buys almost no
> frame time while costing the strongest "the city is alive" cue on exactly the devices that
> most need to feel good. Low now keeps 6. Resolution scale is the lever that actually pays
> on mobile.

Downgrades are automatic; **upgrades are not**. A scene that oscillates between presets looks
worse than one sitting a tier too low, and the oscillation is far more noticeable than the
missing detail. Stepping back up is a user action.

## 8. Enforcement

| Budget | How it is checked | Phase |
|---|---|---|
| Bundle size | `vite-plugin-bundle-analyzer` + CI size gate that fails the build | 1 |
| Draw calls / tris | `PerformanceMonitor` asserts in Playwright E2E on a seeded city | 2 |
| FPS floor | Playwright run with a fixed 60 s camera path, p95 asserted | 7 |
| Heap growth | 10 min soak in E2E, `performance.memory` delta asserted | 9 |
| API latency | Integration test timings + OpenTelemetry histograms | 1 |
| N+1 queries | EF Core interceptor counts queries per request in tests | 4 |

A budget without an automated check is a wish. Every row above has an owner phase; if the
check does not exist yet, the phase is not done.

## 9. Explicitly deferred

Occlusion culling, dynamic resolution scaling, GPU instancing of citizens with skinning,
texture streaming, and WebGPU compute paths. Frustum culling is on by default in Babylon
and is sufficient at slice scale. Revisit only when a measurement — not an intuition —
says otherwise.
