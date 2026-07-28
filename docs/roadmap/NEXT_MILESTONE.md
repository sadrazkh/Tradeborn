# After the Slice

> What comes next, and roughly what each costs. Written at the end of Phase 9 so the sequencing
> decisions are made while the reasoning is fresh — not so anything here is committed to.
>
> **Nothing in this document should start before the slice is verified** (`TECH_DEBT.md` D-1,
> D-2). Building on unverified foundations is how a project acquires a floor it cannot trust.

## Sequencing principle

`GAME_VISION.md` §7 sets the bar for the slice: an unbriefed player reaches their first sale in
under six minutes, and — the harder test — can explain *why* they built their second building.

Nothing below matters until that is true of real players. Content added to a core that is not
yet fun makes a bigger unfun thing.

## M1 · Close the slice *(highest value, smallest cost)*

| Work | Notes |
|---|---|
| Run the integration tests | `TECH_DEBT.md` D-1 |
| Play the six-minute script end to end | D-2 |
| Render vehicles from real transport jobs | D-3 — the biggest gap between economy and presentation |
| Economy simulator | D-5 — before any balance decision touches real players |
| Playwright E2E + FPS measurement | D-6 |

**Exit:** every claim in `VERTICAL_SLICE.md` §5 is verified rather than argued.

## M2 · First playtest

Not a feature milestone. Put the slice in front of ~10 people who have never seen it and
measure the funnel already instrumented in `PLAYER_JOURNEY.md`.

The two numbers that decide everything else:

- **`tutorial_first_sale` median < 6 min** — if not, the tutorial is wrong, not the content
- **`day2_return` > 30 %** — if not, the loop is not yet compelling and more content will not fix it

**This is the milestone most likely to invalidate the ones below**, which is exactly why it
comes before them.

## M3 · The third chain — iron, foundry, tools

Deferred from the slice deliberately (`DECISIONS_REQUIRED.md` A-03): it adds tension but no new
*kind* of decision.

```
Iron Mine → iron_ore → Foundry (2 ore → 1 iron) ─┐
                                                  ├→ Tool Workshop (2 iron + 2 planks → 1 tools)
Sawmill  → planks ───────────────────────────────┘
```

The reason to add it: it makes planks contested by **two** consumers, turning the slice's
two-way surplus decision into a three-way one. That is a genuine deepening of the existing
choice rather than more of the same.

**Cost:** small. Seed data, one building mesh, one recipe. The engine already supports it —
`SettlementEngine` is generic over the recipe graph and the topological ranks are computed, not
hardcoded.

**Risk:** low. Guarded by the acyclicity test and the balance invariant.

## M4 · Player-to-player market

The first genuinely social system, and the first that breaks an assumption the whole codebase
currently rests on.

**What changes structurally.** Today the City is the transactional boundary and one player's
command never touches another player's city (`ARCHITECTURE.md` §5). A player trade touches two
cities in one transaction. That means:

- A defined **lock order** across two cities, or a deadlock the first busy evening
- Escrow, so goods are never in both places or neither
- The audit ledger gaining a counterparty

**Cost:** medium-large. Not because the feature is big, but because it is the first thing to
challenge the aggregate boundary, and getting that wrong is an economy-corrupting class of bug.

**Prerequisite:** the arbitrage guarantee (T10) is currently arithmetic — the NPC spread makes
round trips lossy. Player trading removes that guarantee entirely, because players set their
own prices. A fresh exploit analysis is required *before* any code.

## M5 · Alliances and shared projects

**Cost:** medium. Mostly new: membership, roles, a shared goal with contributions.

**The design risk is larger than the technical one.** Pillar P3 (Respectful Persistence) says
the world must not punish absence. A shared project with a deadline punishes the whole alliance
for one member's absence — which is how cooperative systems become obligations. Any design here
has to solve that before it is built.

## M6 · Regional world map and trade routes

**Cost:** large. A `World` module, region topology, inter-city routes, and travel times that
are no longer a function of one city's geometry.

**The interesting part:** it makes *location* an economic input for the first time. Being near
a grain region should mean something. That is a real deepening — but it also means the balance
numbers in `ECONOMY_DESIGN.md`, which assume every city is identical, need revisiting.

## Not planned

Recorded so the answer is "decided against", not "not thought about":

| | Why |
|---|---|
| Combat / raiding | Conflict in Tradeborn is economic (`GAME_VISION.md` §3). Raiding punishes absence — a direct pillar violation. |
| Energy systems, streaks, decay | Forbidden by §8. Not deferred — excluded. |
| Pay-to-win, loot boxes | Same. |
| Microservices | ADR-002. Extraction triggers are recorded; none has fired. |
| AI in the core loop | `ARCHITECTURE.md` §10. The `IAdvisorService` seam exists; no paid call will sit in a gameplay request. |

## Rough shape

| Milestone | Relative cost | Sequencing |
|---|---|---|
| M1 Close the slice | S | Now |
| M2 First playtest | XS | Immediately after M1 |
| M3 Third chain | S | After M2 confirms the loop |
| M4 Player market | L | Needs a fresh exploit analysis first |
| M5 Alliances | M | Needs the P3 problem solved in design |
| M6 World map | XL | Needs the economy rebalanced for non-identical cities |

Deliberately relative rather than in weeks. This project has no velocity data — a single
developer, no completed milestone measured end to end — and inventing calendar estimates from
nothing would give them a precision they have not earned.
