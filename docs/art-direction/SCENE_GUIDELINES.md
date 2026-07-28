# Scene Guidelines

> Implementation rules for anything that touches the Babylon scene. Reviewed on every PR
> under `ClientApp/src/game/`.

## 1. World units & grid

```
1 world unit = 1 metre
1 plot       = 4 × 4 units, centre at (col*4 + 2, 0, row*4 + 2)
City grid    = 8 × 8 plots = 32 × 32 units
Building height: L1 ≤ 4u, L2 ≤ 6u, L3 ≤ 9u
Ground plane at y = 0. Nothing renders below y = -0.5.
```

Plot coordinates are integers `(col, row)`; the conversion to world space lives in exactly
one function (`PlotGrid.toWorld`). No other file does this arithmetic.

## 2. Camera

Isometric-style, orthographic-feeling but implemented as a constrained perspective camera
(perspective keeps a subtle sense of depth that pure ortho loses).

| Property | Value |
|---|---|
| Type | `ArcRotateCamera` |
| β (elevation) | fixed **55°**, not user-adjustable |
| α (rotation) | free, but **snaps to 45° increments** on release |
| Radius (zoom) | 18 – 60 units, default 34 |
| FOV | 0.5 rad (narrow → flattens perspective, reads as isometric) |
| Target | clamped to city bounds + 6 unit margin |
| Pan | right-drag / two-finger drag |
| Inertia | 0.85 — movement must feel weighted, never floaty |

**Locked β is a design decision, not a limitation.** A fixed elevation guarantees every
building silhouette was authored for the angle it is seen at, and removes an entire class
of "the player found a bad camera angle" bugs.

Touch: one finger = orbit, two fingers = pan + pinch zoom, tap = select, long-press =
context. Camera never moves on tap — a tap that moves the camera feels broken.

## 3. Mandatory rendering practices

| Practice | Rule |
|---|---|
| Repeated props (trees, rocks, fences) | **Thin instances**, one master mesh per type |
| Same-type buildings | `createInstance()` from a master mesh |
| Static geometry (terrain, roads) | Merged into one mesh at load |
| Materials | Shared from `MaterialLibrary`. **Never** `new StandardMaterial` per object |
| Meshes created per frame | **Zero**. Everything pooled |
| `scene.registerBeforeRender` allocations | **Zero**. No closures, no arrays, no vectors created in the loop |
| Off-screen animation | Paused via `mesh.isInFrustum()` check every 500 ms, not per frame |
| Disposal | Every renderer owns a `dispose()` that releases meshes, materials, observers |

### The allocation rule
The render loop runs 60×/second. A single `new Vector3()` per building per frame is
~1 800 allocations/second → GC pauses → visible stutter. Pre-allocate scratch vectors as
module-level constants and mutate them in place (`Vector3.TransformCoordinatesToRef`, etc.).

## 4. Vue ↔ Babylon boundary

This is the rule most likely to be violated and most expensive to violate.

```ts
// ✅  GameBridge.ts
const engine = shallowRef<Engine | null>(null)
const scene  = markRaw(new Scene(engine))

// ❌  NEVER
const meshes = ref<Mesh[]>([])        // Vue deep-proxies the entire scene graph
const camera = reactive(arcCamera)    // catastrophic frame drops
```

`game/` must not import from `vue`. Enforced by an ESLint `no-restricted-imports` rule.

Communication:
- Vue → Babylon: `gameBridge.execute({ type: 'PLACE_BUILDING', plot, definitionId })`
- Babylon → Vue: `gameBridge.on('buildingSelected', handler)` — typed events carrying
  **plain data only**, never mesh references.

## 5. Selection & placement

**Selection:** `scene.pick` on pointer-up only (never on move — picking every frame is
expensive). Selected mesh gets a `HighlightLayer` outline in gold `#F0B429`. Exactly one
selection at a time; clicking empty ground clears it.

**Placement mode:**
1. Ghost mesh follows the pointer, snapped to plot centres.
2. Ghost is tinted `#4CAF7D` at 60 % alpha when valid, `#D9534F` when not.
3. Invalid reasons render as a glyph above the ghost (not colour alone — §9 of Art Direction).
4. Validity is checked **client-side for feedback and server-side for truth**. The client
   check is a UX affordance; the server rejects independently.
5. Confirm on tap/click; `Esc` or right-click cancels.

## 6. Construction stages

Construction renders as discrete stages driven by server progress, never by a client timer:

```
0–25 %   Ground cleared, foundation outline, dust particles, crane appears
25–60 %  Frame/scaffolding, partial walls, worker figures
60–90 %  Walls complete, roof going on, scaffolding thinning
90–100 % Scaffolding removed, final materials, completion burst + sound
```

`progress = (serverNow - startedAt) / (completesAt - startedAt)`, clamped 0–1, where
`serverNow` is derived from the synchronised clock offset — not `Date.now()`.

If the client reconnects mid-construction it jumps straight to the correct stage without
replaying earlier ones.

## 7. Vehicles

- Pooled. Pool size = `maxConcurrentTransports` (10 in the slice). Never instantiated
  during play.
- Move along a `Path3D` built from the road graph, constant speed, orientation from path
  tangent.
- **Purely cosmetic.** Arrival is a server event. If the animation is interrupted, killed,
  or the tab is backgrounded, the goods still arrive — the client simply snaps to the
  post-arrival state on the next update.
- If a transport is already in flight when the client loads, the vehicle spawns at the
  correct interpolated position along the path, not at the origin.

## 8. Level-of-detail & quality

| Distance from camera | Detail |
|---|---|
| < 20 u | Full mesh, animations, particles |
| 20–40 u | Full mesh, animations, no particles |
| > 40 u | Simplified mesh (L0 kit form), no animation |

Quality presets (see `PERFORMANCE_BUDGET.md` §7) are applied by `QualityManager` through a
single `applyPreset(preset)` call. No renderer reads quality settings directly — they
receive them. This keeps quality switching atomic and testable.

## 9. Debug & test bridge

`window.__tradeborn` is exposed in development and E2E builds **only** (stripped in
production by a Vite `define` flag). It is the reason Playwright can test a `<canvas>`:

```ts
window.__tradeborn = {
  ready:      () => boolean,
  renderer:   () => 'webgpu' | 'webgl2',
  fps:        () => number,
  drawCalls:  () => number,
  triangles:  () => number,
  buildings:  () => Array<{ id, definitionId, plot, level, state }>,
  selection:  () => string | null,
  select:     (buildingId: string) => void,
  cameraState:() => { alpha, beta, radius, target },
}
```

Every field is plain JSON — no mesh references escape. An E2E test asserting "the sawmill
appeared at plot (3,4) and is producing" is then a normal DOM-free assertion.

## 10. Review checklist

- [ ] No `new` inside `registerBeforeRender`
- [ ] Materials come from `MaterialLibrary`
- [ ] Repeated geometry uses instances or thin instances
- [ ] `dispose()` implemented and called
- [ ] No `vue` import under `game/`
- [ ] No Babylon object inside `ref`/`reactive`
- [ ] Colours come from the locked palette
- [ ] State conveyed by shape/icon, not hue alone
- [ ] Animations respect `prefers-reduced-motion`
- [ ] Draw calls and triangles still inside budget
