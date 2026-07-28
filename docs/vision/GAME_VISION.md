# Tradeborn — Game Vision

> **Status:** Approved (Phase 0) · **Owner:** Technical Director · **Last update:** Phase 0

## 1. One sentence

> Tradeborn is a persistent online economy game where you grow a trading city that you
> *watch* work — every coin you earn has a truck, a chimney, or a crane behind it.

## 2. The single design idea

**Every number has a body.**

If a value changes in the database and nothing changes in the 3D city, the feature is
unfinished. This is the one rule that separates Tradeborn from the browser-strategy genre
it comes from. Travian shows you a table of resources. Tradeborn shows you a sawmill
running out of logs, and *then* the number.

This rule is testable, and it is enforced in review:

| Server-side change | Required visual consequence |
|---|---|
| Construction started | Ground clears, scaffolding + crane appear |
| Construction progresses | Building grows through discrete stages |
| Production running | Machine animation, smoke/steam, light on |
| Production halted (no input) | Animation stops, warning mote above building |
| Goods delivered | A vehicle physically drove there and unloaded |
| Building upgraded | Different mesh, larger footprint, richer lighting |
| Market price falls | Market stall visibly emptier / fewer NPC buyers |
| Player offline earnings | Replayed as a short "while you were away" summary |

## 3. What Tradeborn is not

- **Not a city builder.** Beauty is a reward, not the goal. The goal is throughput and margin.
- **Not an idle/clicker.** Waiting is not the mechanic. *Scarcity* is the mechanic.
  Time gates exist to create planning windows, never to sell time-skips.
- **Not an admin panel.** No tables, no forms, no dashboards on the main screen.
- **Not a war game (yet).** Conflict in v1 is economic: competing for market demand,
  not burning each other's villages.
- **Not third-person.** You are a market force, not an avatar.

## 4. Target player

**Primary — "The Optimiser" (25–45).** Played Factorio, Anno, Transport Tycoon, or spent
too long in a spreadsheet tuning a supply chain. Wants a system that rewards understanding.
Plays 15–40 min/day in 2–3 sessions. Will not tolerate pay-to-win or fake urgency.

**Secondary — "The Grower" (18–40).** Comes from Hay Day / Clash of Clans / Township.
Wants visible progress and a pleasant place to return to. Will not tolerate a spreadsheet.

**Design consequence:** the *systems* serve the Optimiser; the *presentation* serves the
Grower. Depth must be opt-in and readable, never mandatory to enjoy the first hour.

**Explicitly not targeted in v1:** hardcore PvP raiders, real-money traders, idle-game
whales. Designing for them would compromise pillars 2 and 3.

## 5. Game pillars

Every design decision must serve one of these four. A feature that serves none is cut.

### P1 — Visible Economy
The 3D city is the primary UI. Numbers confirm what you already saw. Any mechanic that
cannot be shown in the world must be redesigned or dropped.

### P2 — Meaningful Scarcity
The player makes decisions because a resource is *short*, never because a timer is long.
Bottlenecks — not cooldowns — create the interesting choice. The canonical example in the
vertical slice: your sawmill produces 60 planks/hour, your bakery only consumes 30. The
other 30 can be sold now, or saved toward a second bakery. That is the whole game in
miniature.

### P3 — Respectful Persistence
The world runs while you are away and does not punish you for leaving. No wilting crops,
no decaying buildings, no "log in daily or lose your streak". Offline progress is capped
by *storage capacity*, not by guilt. Coming back is a reward, not a chore.

### P4 — Readable Depth
Deep systems, shallow interface. A new player should reach their first sale without
reading anything. A 40-hour player should still be finding new margin. Complexity is
revealed by unlock, never dumped up front.

## 6. The fantasy

You arrive at an empty river bend with a small purse. Forty hours later, freighters queue
at your docks because *your* city is the cheapest source of bread on the coast, and you
know exactly which four decisions made that true.

## 7. Success criteria for the vertical slice

The slice succeeds when an unbriefed player:

1. Reaches their first sale in **under 6 minutes** without reading documentation.
2. Can explain, unprompted, **why** they built their second building.
3. Says the city "feels alive" before they say it "looks nice".
4. Returns the next day **without a push notification**.

Criteria 2 and 4 are the real tests. 1 and 3 are necessary but not sufficient.

## 8. Business model position (v1 non-binding)

Monetisation is **out of scope for the vertical slice** and no hooks will be built for it.
When it arrives, it is constrained by pillar 3 and §15 of the project brief:

- **Allowed:** cosmetic city themes, additional build queue slots, account-level QoL.
- **Forbidden:** selling resources, selling time-skips, loot boxes, energy systems,
  ads, anything that makes waiting worse in order to sell the fix.

This is recorded now so that no system built in phases 1–9 quietly assumes otherwise.

## 9. Related documents

- Detailed mechanics → [`../game-design/GDD.md`](../game-design/GDD.md)
- Loop timings → [`../game-design/CORE_LOOPS.md`](../game-design/CORE_LOOPS.md)
- Slice scope → [`../game-design/VERTICAL_SLICE.md`](../game-design/VERTICAL_SLICE.md)
- Numbers → [`../economy/ECONOMY_DESIGN.md`](../economy/ECONOMY_DESIGN.md)
- Look → [`../art-direction/ART_DIRECTION.md`](../art-direction/ART_DIRECTION.md)
