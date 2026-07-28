# Core Loops

> **Status:** Approved (Phase 0) · Numbers here are the **source of truth for tuning**
> and are mirrored in seed data. Do not hardcode them anywhere else.

## 1. Loop hierarchy

```
MOMENT      2-10 s    Click a plot, place a building, watch a truck arrive
SHORT       3-6 min   Produce → haul → store → sell → reinvest
MEDIUM      1-3 h     Complete a production chain, upgrade a bottleneck
LONG        2-7 days  Specialise the city, unlock a district, dominate a good
```

Each loop must feed the next. If the short loop does not visibly advance a medium goal,
the player disengages at ~20 minutes.

## 2. Moment loop (2–10 seconds)

The unit of *feel*. Every one of these must have audio + visual acknowledgement within
100 ms of the click, even if the server round-trip takes longer (optimistic visual, server
reconciles — see [`../architecture/REALTIME_AND_TIME_MODEL.md`](../architecture/REALTIME_AND_TIME_MODEL.md)).

| Action | Feedback |
|---|---|
| Hover plot | Plot tile lifts 4 cm, soft outline |
| Select building | Rim highlight, radial info card fades in |
| Place building | Dust puff, thud, ground flattens |
| Collect / sell | Coin arc to HUD, cash-register chime, counter rolls up |
| Level up | Camera pulls back slightly, light warms, fanfare |

**Rule:** no action waits on the network to *look* like it happened. No action is *trusted*
until the server confirms.

## 3. Short loop (3–6 minutes) — the heartbeat

```
        ┌──────────────────────────────────────────────┐
        │                                              │
   [1] Check what is short  ──►  [2] Fix the bottleneck │
        ▲                              │               │
        │                              ▼               │
   [5] Reinvest coins  ◄──  [4] Sell  ◄──  [3] Haul & store
        │                                              │
        └──────────────────────────────────────────────┘
```

1. **Read the city** (10–20 s). Warning motes float over halted buildings. The player
   learns state by *looking*, not by opening a panel.
2. **Fix the bottleneck** (20–60 s). Start a production order, build, or upgrade.
3. **Haul & store** (30–120 s, passive). Trucks visibly move goods to the warehouse.
   The player can watch or ignore this.
4. **Sell** (10–30 s). Open the market, sell surplus. Price feedback is immediate.
5. **Reinvest** (10–60 s). Spend on the next building or upgrade.

**Target:** a full pass in **3–6 minutes**, ending with the player strictly better off and
with one obvious next goal. If a pass ends with "now I wait", the loop has failed.

### Anti-chore guarantee
The player **never** has to tap buildings to collect output. Production auto-delivers to
the warehouse via the logistics system. The only mandatory manual acts are *decisions*:
what to build, what to produce, what to sell, what to upgrade.

## 4. Medium loop (1–3 hours)

Completing one vertical chain end-to-end.

```
Raw extractor  →  Processor  →  Assembler  →  Market
Lumber Camp    →  Sawmill    →  ┐
                                ├─ Bakery  →  Bread (high margin)
Farm           →  Mill       →  ┘
```

Beat structure:
1. Player notices bread sells for far more than planks.
2. Discovers bread needs *two* chains converging.
3. Builds the missing chain (Farm → Mill).
4. Hits the plank shortfall — bakery eats planks the sawmill was selling.
5. **The decision:** upgrade the sawmill, or accept lower plank income?
6. Resolves it; bread flows; income roughly triples.

Step 5 is the first real economic decision in the game. Everything before it is teaching.

## 5. Long loop (2–7 days)

- **Specialise.** The city cannot be good at everything — plots are finite. Committing to
  bread means fewer plots for tools.
- **Unlock a district.** City level opens a new plot cluster with different terrain
  (riverside → docks, hills → ore).
- **Upgrade the spine.** Warehouse and Market upgrades raise the ceiling on everything.
- **Dominate a good.** Being the region's cheapest bread producer becomes an identity.

**Not in the vertical slice.** Extension points only — see
[`../roadmap/IMPLEMENTATION_PLAN.md`](../roadmap/IMPLEMENTATION_PLAN.md).

## 6. Session shapes

| Session | Length | What the player does |
|---|---|---|
| Check-in | 2–4 min | Collect offline output, sell, start one order, leave |
| Standard | 12–20 min | 3–4 short loops, one medium goal advanced |
| Deep | 40–60 min | Restructure the chain, plan a specialisation |

The game must be **complete and satisfying at 2 minutes**. Everything longer is optional.
This is what makes pillar P3 (Respectful Persistence) real rather than aspirational.

## 7. Offline handling

On return, the player sees a **Session Recap**: an animated summary of what was produced,
what was delivered, and — critically — **what stopped and why**.

Offline output is capped by warehouse capacity, not by a timer. A full warehouse is a
*design signal*: "your storage is the bottleneck now". It is never framed as a punishment,
and never as "you lost X because you were away".

Capacity math and the exact settlement algorithm live in
[`../architecture/REALTIME_AND_TIME_MODEL.md`](../architecture/REALTIME_AND_TIME_MODEL.md).

## 8. Failure modes to watch in playtest

| Symptom | Likely cause | Fix lever |
|---|---|---|
| Player idles waiting for timers | Short loop has dead air | Shorten L1 production times |
| Player sells only raw goods | Value-add multiplier too low | Raise processed base prices |
| Player never upgrades | Upgrade cost/benefit unclear | Show projected delta in upgrade card |
| Player stops at 20 min | No visible medium goal | Strengthen quest chain signposting |
| Warehouse always full | Capacity too low vs output | Raise base capacity or warehouse tiers |
