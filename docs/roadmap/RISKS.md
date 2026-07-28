# Risk Register

> Reviewed at the end of every phase. Probability × Impact → Severity.
> **P** = probability (L/M/H), **I** = impact (L/M/H).

## Active risks

### R-01 · Art quality falls short without an art team — **P:H I:H → CRITICAL**
The whole premise is "visually alive". Procedural low-poly can look cheap.

**Mitigation:** quality comes from lighting rig, tight palette, silhouette discipline, and
animation — not polygons (`../art-direction/ART_DIRECTION.md` §2). Phase 2 is front-loaded
so this is tested in week 2, not week 8.
**Early warning:** if the phase 2 city does not read as pleasant to a fresh viewer, stop and
reassess before building economy on top of it.
**Fallback:** budget for a commissioned modular building kit; the `ModelRegistry` interface
already allows swapping procedural meshes for glTF without touching gameplay code.

---

### R-02 · Babylon bundle size blows the loading budget — **P:M I:H → HIGH**
Full Babylon is several MB. Barrel imports defeat tree-shaking.

**Mitigation:** per-module imports only; lint rule bans `import … from '@babylonjs/core'`;
CI bundle-size gate from phase 1; engine in a lazy chunk so the HUD shell paints first.
**Early warning:** prototype measurement in phase 0 — if the tree-shaken core exceeds
~900 KB gzip, revisit before phase 2.
**Fallback:** raise the budget and add a designed loading experience, or drop to a leaner
feature subset of the engine.

---

### R-03 · Economy is boring or has a dominant strategy — **P:M I:H → HIGH**
A slice that computes correctly but plays flat.

**Mitigation:** the surplus-planks decision is designed in from the start
(`../economy/ECONOMY_DESIGN.md` §3); the simulator asserts no strategy dominates by > 25 %;
all constants are data-driven and retunable without a deploy.
**Early warning:** simulator output at phase 6; first playtest at phase 7.
**Fallback:** promote the iron/tools chain into scope — but only after the core is proven
fun. Adding content to fix a boring core never works.

---

### R-04 · Mobile performance misses 30 fps — **P:M I:M → MEDIUM**
Mid-range phones are the constraint.

**Mitigation:** budgets defined before code (`../architecture/PERFORMANCE_BUDGET.md`);
zero real-time shadows on mobile; quality presets with auto-downgrade; instancing mandatory
from phase 2.
**Early warning:** measure on a real mid-range device at the end of phase 2. Not an emulator.
**Fallback:** reduce prop density and citizen count on mobile; 0.75 resolution scale.

---

### R-05 · Scope creep back toward the full brief — **P:H I:M → HIGH**
Nineteen modules, ten roles, and nine phases invite over-building.

**Mitigation:** `VERTICAL_SLICE.md` §4 is an explicit exclusion list; modules are folders,
not assemblies; only 4 `src` projects; phase gates require a *running* app.
**Early warning:** any PR adding an abstraction with one implementation and no second
consumer in sight.
**Fallback:** cut back to the slice list. The exclusion list is the contract.

---

### R-06 · Offline settlement has a subtle economic bug — **P:M I:H → HIGH**
Sub-stepping and topological ordering are easy to get subtly wrong; a bug here mints or
destroys goods silently.

**Mitigation:** the determinism invariant is tested directly (one 8 h jump == 480 × 1 min);
audit ledger allows reconciling every balance by replay; `Money` throws on negative.
**Early warning:** determinism test failures in phase 4; ledger reconciliation mismatch.
**Fallback:** shrink the step size (costs CPU, buys accuracy) — the mechanism is already
parameterised.

---

### R-07 · WebGPU instability on mobile Safari — **P:M I:L → LOW**
Support is uneven and changing.

**Mitigation:** WebGL2 is the **default** path; WebGPU is feature-detected and opt-in. Both
are tested every phase; the prototype validates fallback from day one.
**Fallback:** ship WebGL2-only. Nothing in the design requires WebGPU.

---

### R-08 · Redis absent in local development — **P:H I:L → LOW**
Already true on the current machine (port 6379 not listening).

**Mitigation:** Redis is optional through phase 2; the app boots with an in-memory cache and
in-memory rate limiter when Redis is unreachable, and logs a clear warning.
**Becomes blocking:** phase 3, when idempotency needs a durable fast path — though
PostgreSQL remains the system of record, so this is a latency concern, not correctness.

---

### R-09 · Docker unavailable on the development machine — **P:H I:M → MEDIUM**
`docker --version` fails; Testcontainers-based integration tests cannot run locally.

**Mitigation:** local PostgreSQL (confirmed listening on 5432) is the documented dev path;
integration tests detect Docker and fall back to a local connection string via
`TRADEBORN_TEST_POSTGRES`; CI always has Docker and runs the full suite.
**Consequence:** some integration tests are CI-only locally. Documented in
`../testing/TEST_STRATEGY.md`, not hidden.

---

### R-10 · Vue reactivity wrapping Babylon objects destroys performance — **P:M I:H → HIGH**
A single `ref(mesh)` makes Vue deep-proxy the scene graph. Symptom is catastrophic and the
cause is non-obvious.

**Mitigation:** `game/` may not import `vue` (ESLint rule); engine held in `shallowRef` +
`markRaw`; the boundary is one file (`GameBridge`); it is on the review checklist.
**Early warning:** sudden unexplained frame drop after a UI change.

---

### R-11 · Single-developer bandwidth vs. nine phases — **P:H I:M → HIGH**
The plan is large for the available capacity.

**Mitigation:** phases are independently valuable and shippable; the slice is the milestone
that matters; `CURRENT_STATUS.md` keeps state resumable across sessions.
**Fallback:** phases 8 and 9 are deferrable indefinitely without harming the slice.

---

## Closed

*(none yet — Phase 0 in progress)*

## Review log

| Date | Phase | Change |
|---|---|---|
| Phase 0 | 0 | Register created. R-08 and R-09 confirmed from environment probe. |
