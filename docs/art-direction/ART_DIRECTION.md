# Art Direction

> **Status:** Approved (Phase 0) · Owner: UI/UX + Technical Art
> **Risk note:** art is the highest-risk part of this project (see
> [`../roadmap/RISKS.md`](../roadmap/RISKS.md) R-01). This document exists to make the look
> achievable *without* an art team.

## 1. The look in one line

> **A warm, hand-made diorama you could pick up** — stylised low-poly, isometric, soft
> shadows, saturated but not candy-coloured.

Reference feel (not to copy): the readability of *Two Point* games, the warmth of *Anno*'s
early tiers, the silhouette clarity of *Islanders*.

## 2. Core strategy — quality from light, not from polygons

We have no art team and no licensed asset budget. Copying assets is prohibited. So the
strategy is:

> **Procedural modular geometry + excellent lighting + tight palette + good animation.**

This is a deliberate bet, and it is the right one:
- A 400-triangle building with a **strong silhouette**, a **coherent palette**, and a
  **warm three-point light rig** reads better than a 20 000-triangle asset with default
  lighting.
- It is 100 % original work → zero copyright risk.
- It costs nothing and is instantly re-skinnable when real assets arrive.

Every mesh in the slice is generated at runtime from Babylon primitives composed into
modular kits (§5). No downloaded models. No downloaded textures.

## 3. Palette

Locked. All colours below are defined once in `game/assets/MaterialLibrary.ts` and are the
**only** colours allowed in the 3D scene.

### Terrain & environment
| Role | Hex | Use |
|---|---|---|
| Grass light | `#8CC152` | Plot tops, open ground |
| Grass dark | `#6BA644` | Plot sides, variation |
| Dirt / path | `#C9A87C` | Roads, cleared ground |
| Stone | `#9E9E93` | Foundations, cliffs |
| Water | `#4FA3C7` | River, harbour (post-slice) |
| Water foam | `#BFE4F2` | Shore line |

### Buildings
| Role | Hex | Use |
|---|---|---|
| Timber | `#A9714B` | Walls, beams — the dominant building colour |
| Timber dark | `#7C5134` | Frames, shadow faces |
| Plaster | `#E8DCC8` | Upper storeys, contrast |
| Roof red | `#C0563F` | Residential, primary roofs |
| Roof blue | `#4A7BA7` | Civic buildings (Town Hall, Market) |
| Roof slate | `#5B6670` | Industrial (Sawmill, Foundry) |
| Metal | `#8D949C` | Machinery, chimneys |

### Signal colours (never used decoratively)
| Role | Hex | Meaning |
|---|---|---|
| Gold | `#F0B429` | Coins, rewards, selection highlight |
| Success | `#4CAF7D` | Valid placement, completion |
| Warning | `#E8A33D` | Halted production, low input |
| Danger | `#D9534F` | Invalid placement, error |

**Rule:** signal colours never appear on a building's base material. If gold appears, it
means *money*. This is what lets the player read city state at a glance (pillar P1).

### Sky (day/night cycle)
| Phase | Sky | Key light | Ambient |
|---|---|---|---|
| Dawn | `#FFD5A0` | `#FFC98A` warm, low | `#8FA5C4` |
| Day | `#A8D8F0` | `#FFF6E0` neutral, high | `#B8D4E8` |
| Dusk | `#E8956B` | `#FF9E5E` warm, low | `#7E88B0` |
| Night | `#2C3E60` | `#8FA8D8` cool, dim | `#3E4A6B` |

Night never goes truly dark — the city must stay readable. Building windows emit `#FFD98A`
at night, which is the single strongest "the city is alive" cue in the whole game and costs
one emissive material.

## 4. Lighting rig (fixed, do not improvise)

```
Key    DirectionalLight, elevation 50°, azimuth -35°, intensity 1.1, warm
Fill   HemisphericLight, sky = palette ambient, ground = #6B7A5A, intensity 0.55
Rim    DirectionalLight, opposite key, intensity 0.25, cool — separates silhouettes
Shadow Key only, 1024 map, PCF soft, desktop High only; blob decals elsewhere
```

The rim light is not optional. It is what stops a low-poly scene looking flat, and it costs
almost nothing.

## 5. Modular building kit

Every building is composed from the same small vocabulary, which is what makes 8 buildings
feel like one world:

```
Base       box or hexagonal pad, 1×1 or 2×2 plots, slightly inset from plot edge
Body       1–3 stacked boxes, each ≤ 1.2 plot heights, offset for asymmetry
Roof       gable | hip | flat-with-parapet | industrial sawtooth
Accents    chimney, door, window strip, sign board, awning, crate stack
Motion     one signature moving part (saw blade, mill sail, cart, smoke)
```

**Silhouette rule:** each building must be identifiable from its black silhouette alone at
128 px. This is tested by literally rendering the roster in flat black at that size during
review. If two buildings are confusable, one gets a distinguishing accent — height, roof
shape, or its signature moving part.

### Level progression visuals
| Level | Change |
|---|---|
| 1 | Base form, small, plain |
| 2 | Taller / wider, second material appears, an accent added, motion speeds up |
| 3 | Distinct silhouette change (extra wing, larger roof), warm emissive detail, props appear around the base |

Upgrading must be **obvious from across the city** without reading a number.

## 6. Animation principles

Short, snappy, overshoot slightly. Nothing linear.

| Event | Duration | Curve |
|---|---|---|
| Building placed | 400 ms | scale 0 → 1.08 → 1.0, elastic |
| Construction stage | 600 ms | ease-out, dust puff at start |
| Selection | 150 ms | ease-out |
| Coin fly to HUD | 700 ms | arc, ease-in-out, slight spin |
| Camera focus | 500 ms | ease-in-out cubic |
| Level-up | 1 200 ms | pull-back + light warm + fanfare |

Idle animations (saw spinning, smoke, sails turning) run continuously but are **paused when
off-screen** and **removed entirely at Low quality**.

## 7. UI style

Floating, translucent, minimal. The 3D scene is the interface; the HUD annotates it.

- **Panels:** `#16202E` at 88 % alpha, 16 px corner radius, 1 px `#F0B429` at 15 % border,
  soft outer shadow. Backdrop blur where supported.
- **Type:** Inter (or system UI stack). Numbers use **tabular figures** — rolling counters
  must not jitter.
- **Layout:** bottom-centre = primary actions; top-left = resources; top-right = player &
  settings; contextual cards appear **anchored to the building in 3D space**, not in a
  corner.
- **No tables. No forms on the main screen. No modal unless destructive.**
- Resource counts animate by rolling, never by snapping.
- Touch targets ≥ 44 px. All interactive elements reachable one-handed on mobile
  (bottom 60 % of screen).

## 8. Audio direction

Warm, wooden, low-frequency. Nothing shrill.

| Layer | Content |
|---|---|
| Ambience | Wind, distant birds; crossfades with day/night |
| Building loops | Positional, low volume, attenuated by camera distance |
| UI | Soft wooden clicks; coin chime for income |
| Stingers | Construction complete, level up, quest complete |

Audio is **hooked but silent** in the slice (`AudioManager` with a no-op sink) so that
adding licensed sound later touches no gameplay code. Default volume 40 %; a mute control
is present from day one.

## 9. Accessibility (from Phase 2, not retrofitted)

- Colour-blind safe: state is **never** conveyed by hue alone. Halted buildings get a mote
  *icon*, invalid placement gets an ✕ *glyph*, not just red.
- `prefers-reduced-motion` → disables camera shake, screen transitions, and idle
  animations; keeps functional feedback.
- Full keyboard navigation for HUD; camera on WASD/arrows.
- Minimum contrast 4.5:1 for text; UI scale setting 80–140 %.
- No flashing above 3 Hz.

## 10. Asset register & licensing

**Current asset inventory: zero third-party assets.** Everything is procedurally generated
in code from Babylon primitives.

| Asset | Source | Licence | Status |
|---|---|---|---|
| All meshes (slice) | Generated in `MaterialLibrary` / `ModelRegistry` | Original work | ✅ |
| All materials | Solid colours from §3 | Original work | ✅ |
| Fonts | Inter | SIL OFL 1.1 | ✅ |
| Icons | Custom SVG | Original work | ✅ |
| Audio | *none yet* | — | pending |

**Process (mandatory):** no asset enters the repository without a row in this table naming
its source and licence. Any asset of unclear provenance is rejected. When commissioned or
licensed assets arrive, `ModelRegistry` swaps procedural meshes for glTF loads behind the
same interface — no gameplay code changes.
