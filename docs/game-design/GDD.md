# Game Design Document — Tradeborn

> **Status:** Approved (Phase 0), scoped to the vertical slice.
> Companion documents carry the detail: [`CORE_LOOPS.md`](CORE_LOOPS.md),
> [`VERTICAL_SLICE.md`](VERTICAL_SLICE.md), [`PLAYER_JOURNEY.md`](PLAYER_JOURNEY.md),
> [`../economy/ECONOMY_DESIGN.md`](../economy/ECONOMY_DESIGN.md).

## 1. Overview

| | |
|---|---|
| **Title** | Tradeborn |
| **Genre** | Persistent online economic strategy / production-chain builder |
| **Platform** | Web (desktop + mobile browser), PWA installable |
| **Session** | 2–20 min, 2–3 sessions/day |
| **Model** | Asynchronous persistent — the world runs while you are away |
| **Audience** | 18–45, supply-chain optimisers and progress-builders (see `../vision/GAME_VISION.md` §4) |
| **Monetisation** | None in slice; cosmetic-only when it arrives |

**Pitch:** Build a trading city whose economy you can *watch*. Extract, refine, haul, and
sell — and see every coin's origin in the streets below.

## 2. Player fantasy & verbs

The player is an **economic planner**, not an avatar. The verbs are:

**Observe** → **Decide** → **Commit** → **Watch it happen** → **Reinvest**

"Watch it happen" is the verb that distinguishes Tradeborn. In the reference genre that
step is a progress bar. Here it is trucks, cranes, smoke, and light.

## 3. Systems

### 3.1 City & plots
An 8×8 plot grid on mixed terrain. 16 plots unlocked at city level 1; more unlock with
city level. Plots have a terrain type (grass / dirt / stone) which gates what can be built
there in future tiers — in the slice, all slice buildings accept grass and dirt.

Plot scarcity is the pressure that makes the upgrade-vs-expand decision real (§3.4).

### 3.2 Buildings
Eight building types (`../economy/RESOURCE_GRAPH.md` §4). Each has:
inputs · outputs · cycle time · capacity contribution · level (1–3) · build cost ·
upgrade cost · per-level mesh · unlock requirement · active/halted state · halt reason.

States: `UnderConstruction` → `Idle` → `Producing` → `Halted(NoInput | NoCapacity)`.
Every state has a distinct visual (`../art-direction/SCENE_GUIDELINES.md` §6).

### 3.3 Production
Buildings with a recipe produce continuously while inputs and output capacity allow.
Production is **automatic** — the player sets what to produce, not when to collect. This is
the anti-chore guarantee from `CORE_LOOPS.md` §3.

Output is computed by Deterministic Lazy Settlement
(`../architecture/REALTIME_AND_TIME_MODEL.md`), so it runs identically whether the player
is watching or asleep.

### 3.4 The central decision
One sawmill produces 60 planks/h; one bakery consumes 30. The surplus can be sold for
steady income, or banked toward a second bakery — which also requires a second farm and
mill, and therefore three more plots.

```
Sell surplus          →  income now, no plot cost, lower ceiling
Build second bakery   →  3 plots, ~1 h payback, much higher ceiling
Upgrade the sawmill   →  0 plots, worse ratio, but no new chain to manage
```

There is no correct answer; that is the point. Every later system is a variation on this.

### 3.5 Logistics
Goods do not teleport. Completed output is dispatched as a `TransportJob` that travels the
road graph from producer to warehouse. Travel takes real time and is visible.

The vehicle animation is cosmetic; the server decides arrival
(`../architecture/REALTIME_AND_TIME_MODEL.md` §6). A killed tab, a backgrounded phone, or a
dropped connection never costs the player goods.

### 3.6 Storage
Capacity is per-resource: `TownHall(level) + Σ Warehouse(level)`. Production **halts** at
capacity — nothing overflows, nothing is destroyed. A full warehouse is the primary offline
cap and the main driver of warehouse upgrades (pillar P3).

### 3.7 Market
NPC market with dynamic pricing. Selling pushes price down proportionally to volume against
a per-resource depth; prices mean-revert to base over ~35 min. Buy price is 1.25× sell
price, making arbitrage structurally impossible
(`../economy/ECONOMY_DESIGN.md` §7).

Player-to-player trading is **post-slice**.

### 3.8 Progression
Player XP and level from building, upgrading, producing, selling, and quests.
City level from total building levels, gating plot and building unlocks.
Curves in `../economy/ECONOMY_DESIGN.md` §9.

### 3.9 Quests
A 7-step tutorial chain that teaches by doing (`PLAYER_JOURNEY.md`). Each quest has an
objective, an in-world highlight pointing at its target, and a reward. Rewards carry early
pacing so build times can stay short.

Daily-style tasks exist as a **single demonstration task** in the slice to prove the system,
not as a retention mechanic.

### 3.10 Events
Small economic events (e.g. "bread demand rising") shift prices temporarily and are
signalled both in the HUD and in the world. In the slice: one scripted event to prove the
pipeline. Systemic events are post-slice.

## 4. Controls

| Action | Desktop | Mobile |
|---|---|---|
| Orbit | Left-drag | One-finger drag |
| Pan | Right-drag / middle-drag | Two-finger drag |
| Zoom | Wheel | Pinch |
| Select | Left-click | Tap |
| Context | Right-click | Long-press |
| Cancel | `Esc` | Back / tap empty ground |
| Camera | WASD / arrows | — |

Camera never moves on a tap. Selection never requires precision — pick radius is forgiving.

## 5. Interface

The 3D city **is** the interface. HUD is a thin translucent layer:

- **Top-left:** resource counters (rolling, tabular figures)
- **Top-right:** player level, XP bar, settings
- **Bottom-centre:** primary action (Build), quest tracker
- **Contextual:** building card anchored in 3D space to the selected building
- **Market:** a slide-over panel, the only place with dense numbers, opened deliberately

No tables, no forms, no dashboards on the main screen. Information is conveyed by the world
first and confirmed by numbers second.

## 6. Failure & friction

There is **no fail state**. The player cannot go bankrupt, lose buildings, or be raided in
the slice. Friction comes from scarcity and opportunity cost, never from punishment.

Halted production is framed as a *signal*, not a failure: a warning mote that says "this
building wants something", which is a goal, not a scolding.

## 7. Retention philosophy

Return drivers: production completed while away, a visibly changed city, a quest in
progress, a market opportunity. Explicitly forbidden: energy systems, streaks, wilting,
decay, fake scarcity, dark patterns, ads, pay-to-win (`../vision/GAME_VISION.md` §8).

## 8. Post-slice roadmap (design only)

Third chain (iron → tools) · player-to-player market · alliances & shared projects ·
regional world map with trade routes · specialisation & city identity · systemic events ·
seasons · leaderboards. Sequencing in [`../roadmap/IMPLEMENTATION_PLAN.md`](../roadmap/IMPLEMENTATION_PLAN.md).
