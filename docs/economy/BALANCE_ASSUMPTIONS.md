# Balance Assumptions

> Every number in [`ECONOMY_DESIGN.md`](ECONOMY_DESIGN.md) rests on an assumption. This file
> names each one, states **how to falsify it**, and records what we change if it turns out
> to be wrong. Numbers without a falsification test are guesses wearing a suit.

## How to read this

Each assumption has:
- **A-n** — the claim
- **Basis** — where it came from
- **Falsified if** — the observable that proves it wrong
- **Lever** — what we change in response

---

## A-1 — A 3–6 minute short loop sustains attention

**Basis:** Genre convention. Hay Day check-ins run ~3 min; Anno's build-decide cycle runs
~5 min. Below 2 min the loop feels frantic; above 8 min the player loses the thread.

**Falsified if:** Playtest median time-between-meaningful-decisions is < 90 s or > 8 min,
or session length clusters under 4 minutes.

**Lever:** L1 cycle times (30 s / 60 s / 120 s) — the cheapest knob in the game. All three
scale together to preserve the ratios in `RESOURCE_GRAPH.md` §3.

---

## A-2 — Per-building return must rise with chain depth

**Basis:** If processing does not beat extraction *per plot*, rational players sell raw
goods forever and the entire production graph is decoration.

Current: 240 → 300 → 420 coins/h/building (raw → processed → finished).

**Falsified if:** the simulator's Optimiser archetype maximises income by selling only raw
wood and grain at any point in a 30-day run.

**Lever:** finished-good base prices (`planks` 10, `flour` 10, `bread` 60). Raise the top
of the ladder rather than lowering raw prices — cheap raw goods make the first ten minutes
feel bad.

**Guarded by:** an automated invariant test (`ECONOMY_DESIGN.md` §6).

---

## A-3 — Market depth pushes players up the chain

**Basis:** Depth values (wood 500, bread 150) mean 120 wood/h moves the wood price ~12 %/h
while 30 bread/h moves bread ~10 %/h — but bread starts 30× more valuable, so the absolute
coin loss from saturating wood is far larger relative to income.

**Falsified if:** raw-good prices never drop below 0.8 × base under Optimiser play (depth
too generous), **or** hit the 0.4 floor within 30 min (depth too punishing).

**Lever:** `marketDepth` per resource first; `elasticity` (0.5) only if all resources move
in the same wrong direction.

---

## A-4 — Cost growth (2.5×) beating output growth (1.6×) keeps upgrades optional

**Basis:** If upgrading were strictly better than building wide, plots would be worthless
and the city would never grow visually — killing pillar P1.

**Falsified if:** > 80 % of simulated Optimiser spend goes to upgrades, or < 20 % does.
Either extreme means one strategy dominates.

**Lever:** `upgradeCurve.costFactor`. Narrow the gap to make upgrading more attractive;
widen it to favour building wide.

---

## A-5 — 1.25× buy/sell spread eliminates arbitrage

**Basis:** A round trip loses 20 % before the 3 % fee. No sequence of NPC trades is
profitable. This is structural, not a rate limit — it cannot be out-waited or scripted.

**Falsified if:** any test or player finds a profitable pure-trading loop with no production.

**Lever:** none needed — the invariant is arithmetic. If violated, the bug is in
implementation (e.g. price read at the wrong timestamp), not in tuning.

**Guarded by:** a property-based test that attempts random buy/sell sequences and asserts
terminal balance ≤ initial balance.

---

## A-6 — Warehouse capacity, not time, is the right offline cap

**Basis:** Pillar P3. Capping by storage reads as "I need a bigger warehouse" (actionable,
a goal). Capping by a timer reads as "the game punished me for sleeping" (resentment).

Town Hall L1 (100/resource) fills in ~50 min at 120 wood/h — deliberately fast, so the
first warehouse is an obvious and early purchase.

**Falsified if:** playtesters describe returning as disappointing, or the Idler archetype's
storage sits below 60 % full on return (cap never binds → no reason to upgrade).

**Lever:** Town Hall and Warehouse `storagePerResource`.

---

## A-7 — Quest rewards can carry the first 15 minutes

**Basis:** 1 200 coins from 7 quests vs ~240 coins/h passive. Early pacing is ~5× passive
income, letting build times stay short without inflating the long-run economy.

**Falsified if:** new players stall for > 3 min waiting for coins before the Sawmill, or
reach the Bakery in < 20 min (rewards too generous, no sense of earning it).

**Lever:** quest reward amounts. These are pure pacing and safe to retune — they do not
touch steady-state economy.

---

## A-8 — 8 buildings and 5 resources are enough to feel like an economy

**Basis:** One convergence point plus one divergence point is the minimum for a genuine
trade-off. The cut third chain (iron → tools) adds tension but no new *kind* of decision.

**Falsified if:** playtesters exhaust interesting decisions in under 45 min, or describe
the economy as "obvious" after one session.

**Lever:** promote the iron/tools chain from post-slice to slice scope. Only after the
5-building chain is proven fun — adding content to fix a boring core never works.

---

## A-9 — 2 %/min mean reversion (~35 min half-life) is the right recovery speed

**Basis:** Fast enough that a player returning next session sees a recovered market
(no lingering punishment); slow enough that dumping stock inside one session hurts.

**Falsified if:** players batch all sales into one dump with no penalty (too fast), or
prices are still depressed after a 2 h absence (too slow).

**Lever:** `market.recoveryPerMinute`.

---

## A-10 — Integer-only arithmetic will not produce visible rounding artefacts

**Basis:** All rates are integers per cycle; multipliers are applied at definition time and
floored once. Money is cent-precision `long`.

**Risk:** `1.6^2 = 2.56` → a base rate of 1/cycle floors to 2, a 25 % silent loss at L3.

**Mitigation (implemented in Phase 4):** rates are stored as **units per 1000 cycles**
internally, so upgrade multipliers apply with three digits of headroom before flooring.

**Falsified if:** a unit test finds > 2 % deviation between ideal and floored output at any
level ≤ 3.

---

## Review cadence

Re-evaluate the whole file at:
- End of Phase 6 (first full loop playable — simulator can finally run)
- End of Phase 7 (after first real playtest)
- Before any public test

Each revision appends a dated note; assumptions are never silently edited.
