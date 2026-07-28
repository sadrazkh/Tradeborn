# ADR-006 — Procedural meshes now, glTF pipeline behind a registry

**Status:** Accepted (Phase 0)

## Context

The look must be appealing, mobile-friendly, and legally clean. There is no art team and no
asset budget, and the brief forbids assets of unclear provenance.

## Decision

Two layers:

1. **Now (slice):** every mesh is generated at runtime from Babylon primitives composed into
   a modular kit, using a fixed palette and shared materials. No downloaded models or
   textures.
2. **Later:** an authored **glTF/GLB + Draco + KTX2** pipeline, loaded through the same
   `ModelRegistry` interface, so swapping in real assets touches no gameplay code.

```ts
interface ModelRegistry {
  create(definitionId: string, level: number): TransformNode
}
// ProceduralModelRegistry  → slice
// GltfModelRegistry        → when authored assets exist
```

## Rationale

**Why procedural first:** it removes the single biggest schedule and legal risk
([R-01](../roadmap/RISKS.md)). It is original work by construction, costs nothing, loads
instantly, and is trivially re-skinnable. Critically, it lets us discover whether the
*lighting, palette, silhouette, and animation* work — which is where stylised quality
actually comes from — before spending anything on geometry.

**Why glTF later:** it is the only open, well-supported 3D format with first-party Babylon
support, Draco geometry compression, and KTX2/Basis texture compression (GPU-native, so it
saves VRAM as well as bandwidth — the constraint that actually matters on mobile).

**Why a registry seam:** without it, "add real art" becomes a refactor of every renderer.
With it, it is one class.

## Consequences

**Positive:** zero copyright risk; zero asset download in the slice (helps the loading
budget substantially); instant iteration on shapes; the art bar is set by lighting and
animation, which are cheap to improve.

**Negative:** procedural geometry has a ceiling. It will not match a hand-authored asset.
Accepted — the slice's job is to prove the *feel*, and §2 of
[`../art-direction/ART_DIRECTION.md`](../art-direction/ART_DIRECTION.md) argues that
silhouette and light dominate perceived quality at isometric distance.

**Negative:** mesh generation costs CPU at load. Bounded by generating each master mesh
**once** and instancing from it — which we need for the draw-call budget anyway.

## Asset governance

No asset enters the repository without a row in the register in
`ART_DIRECTION.md` §10 naming its source and licence. Unclear provenance → rejected. This
applies to models, textures, fonts, audio, and icons.

## Loading strategy (when assets exist)

Progressive: terrain and plots first (scene becomes interactive), then buildings by
frequency, then props, then audio. The player can move the camera before everything has
loaded — perceived load time is what the budget in
[`../architecture/PERFORMANCE_BUDGET.md`](../architecture/PERFORMANCE_BUDGET.md) §4 measures.
