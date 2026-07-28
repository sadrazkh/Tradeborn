# ADR-001 — Babylon.js as the 3D engine

**Status:** Accepted (Phase 0) · **Deciders:** Technical Director, Babylon/WebGPU Developer

## Context

The core premise requires a 3D city rendered in-browser on desktop and mobile, integrated
with a Vue application, with no plugin install. We need instancing, glTF loading, a mature
asset pipeline, and a credible WebGPU path.

## Options

| Option | Verdict |
|---|---|
| **Babylon.js 8.x** | ✅ Chosen |
| Three.js | Rejected — we would have to build scene management, asset management, input, and picking ourselves. Excellent library, but it is a renderer, not an engine. |
| PlayCanvas | Rejected — strongest tooling, but editor-centric; the engine-only path is less well travelled and the workflow fits poorly with a code-first, MSBuild-integrated repo. |
| Unity WebGL | Rejected — the brief forbids requiring a plugin-like runtime; build sizes and mobile browser performance are poor; the toolchain does not fit the ASP.NET integration requirement. |

## Decision

**Babylon.js 8.x**, imported per-module, integrated into the Vue app behind a single
`GameBridge` seam.

## Rationale

- **TypeScript-native.** Written in TS, so types are authoritative rather than community stubs.
- **Engine, not just a renderer.** Scene graph, asset manager, input, picking, animation,
  particles, GUI, and inspector are first-party. This is months of work we do not do.
- **Instancing built in.** `ThinInstance` and `createInstance()` map directly onto the
  performance budget — repeated props and buildings are the bulk of our draw calls.
- **Asset pipeline.** First-party glTF/GLB, Draco, and KTX2/Basis support, which
  [ADR-006](ADR-006-asset-pipeline.md) depends on.
- **WebGPU is real.** A genuine WebGPU engine (`WebGPUEngine`) with a WebGL2 fallback that
  shares the same scene API — we get [R-07](../roadmap/RISKS.md) mitigation for free.
- **Licence.** Apache-2.0. No cost, no attribution burden, no commercial ceiling.
- **Debuggability.** The Inspector shortens the diagnosis of draw-call and material problems
  from hours to minutes.

## Consequences

**Positive:** less engine code to write and maintain; performance tools available from day
one; swapping procedural meshes for glTF later requires no gameplay changes.

**Negative:** larger bundle than Three.js ([R-02](../roadmap/RISKS.md)). Mitigated by
per-module imports, a lint rule banning the barrel import, a lazy engine chunk, and a CI
size gate.

**Neutral:** the team must learn Babylon's conventions. Phase 0's prototype exists partly
to surface that cost early.

## Validation

The Phase 0 prototype must demonstrate: a rendered scene, a working isometric camera, mesh
picking, an FPS counter, WebGPU with WebGL2 fallback, and a measured bundle size — served
from ASP.NET as a single build. If the tree-shaken core exceeds ~900 KB gzip, this ADR is
revisited before Phase 2.
