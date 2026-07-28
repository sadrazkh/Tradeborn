# Economy Design — Vertical Slice

> **Status:** Approved (Phase 0) · **This file is the tuning source of truth.**
> Every number here is reproduced in `Tradeborn.Infrastructure` seed data and **nowhere
> else**. No economic constant may appear as a literal in domain or UI code.
> Changing a number here → change the seed → re-run the simulator → commit both.

## 1. Numeric representation (non-negotiable)

| Quantity | CLR type | Unit | Rationale |
|---|---|---|---|
| Money | `long` | 1 coin = **100 cent** | Exact integer arithmetic. No `float`/`double`/`decimal` in balances. |
| Resources | `long` | 1 unit | Integer units only. There is no "half a plank". |
| Rates / multipliers | `decimal` | ratio | Config-only. Never stored in a balance. |
| Durations | `TimeSpan` / `long` seconds | second | Server clock only. |

**Rule:** every economic mutation is integer addition or subtraction. Multipliers are
applied at *definition* time (producing an integer rate), never at *transaction* time.
This makes the whole economy exactly reproducible and diffable — which is what makes the
deterministic tests in [`../testing/TEST_STRATEGY.md`](../testing/TEST_STRATEGY.md) possible.

Money is displayed as coins (`1 234`) but stored as cent (`123 400`).

## 2. Resources (5)

| Id | Name | Tier | Base price (coins) | Market depth | Notes |
|---|---|---|---|---|---|
| `wood` | Wood | Raw | 2 | 500 | Extracted, no input |
| `grain` | Grain | Raw | 2 | 500 | Extracted, no input |
| `planks` | Planks | Processed | 10 | 300 | Dual use: sellable **and** bakery input |
| `flour` | Flour | Processed | 10 | 300 | |
| `bread` | Bread | Finished | 60 | 150 | Highest margin, deepest chain |

**Market depth** = units that can be sold before price moves by a full elasticity step.
Raw goods have deep-but-cheap markets; finished goods have shallow-but-rich markets. This
is the mechanism that pushes players up the chain (§6).

## 3. Production chain

```
  Lumber Camp ──120 wood/h──►  Sawmill ──60 planks/h──┬──► 30/h  Bakery input
                                                       └──► 30/h  surplus → sell
                                                                      ▲
  Farm ────────120 grain/h──►  Mill ────60 flour/h────► Bakery ──30 bread/h──► sell
```

The **30 surplus planks per hour** are the designed decision point (pillar P2). One sawmill
overshoots one bakery by exactly 2×. The player must choose: bank the planks toward a
second bakery (which needs a second Farm + Mill too), or sell them for steady income.

## 4. Buildings (8)

Rates are **level 1**. Upgrade formula in §5.

| Building | Inputs → Output | Cycle | L1 rate/h | Build cost | Build time |
|---|---|---|---|---|---|
| **Town Hall** | — | — | — | *pre-placed* | — |
| **Market** | — | — | — | *pre-placed* | — |
| **Lumber Camp** | — → 1 `wood` | 30 s | 120 wood | 150c + 20 wood | 30 s |
| **Farm** | — → 1 `grain` | 30 s | 120 grain | 150c + 20 wood | 30 s |
| **Warehouse** | — (storage) | — | +200/resource | 250c + 40 wood | 60 s |
| **Sawmill** | 2 `wood` → 1 `planks` | 60 s | 60 planks | 400c + 60 wood | 120 s |
| **Mill** | 2 `grain` → 1 `flour` | 60 s | 60 flour | 400c + 60 wood | 120 s |
| **Bakery** | 2 `flour` + 1 `planks` → 1 `bread` | 120 s | 30 bread | 900c + 40 wood + 30 planks | 300 s |

**Town Hall** (L1, pre-placed): city level anchor, quest giver, base storage **100/resource**.
**Market** (L1, pre-placed): NPC trading post. Sell limits scale with its level.

Build times are deliberately short so the vertical slice is demonstrable in one sitting.
They lengthen in the live-tuned build; the *ratios* are what matter.

## 5. Upgrade formula

For a building at level `L` going to `L+1`:

```
outputRate(L)   = baseRate  × 1.6^(L-1)      → rounded down to integer units/cycle
upgradeCost(L)  = baseCost  × 2.5^(L-1)      → coins and each material
upgradeTime(L)  = baseTime  × 3.0^(L-1)
storageBonus(L) = baseStorage × 1.6^(L-1)    (Warehouse / Town Hall)
```

Max level in the vertical slice: **3**.

Because cost grows faster (2.5×) than output (1.6×), upgrading is *not* automatically
correct — it competes with building a second structure. That is the intended tension.
Upgrading wins when plots are scarce; building wide wins when coins are scarce.

### Worked example — Lumber Camp
| Level | Wood/h | Upgrade cost | Upgrade time | Marginal payback |
|---|---|---|---|---|
| 1 | 120 | — | — | — |
| 2 | 192 | 375c + 50 wood | 90 s | ~2.6 h |
| 3 | 307 | 938c + 125 wood | 270 s | ~4.1 h |

## 6. Value-add ladder — why processing wins

| Strategy | Buildings | Coins/h | **Coins/h per building** |
|---|---|---|---|
| Sell raw wood | LC | 240 | 240 |
| Sell planks | LC + Sawmill | 600 | 300 |
| Sell flour | Farm + Mill | 600 | 300 |
| **Full bread chain** | LC+SM+Farm+Mill+Bakery | 1 800 + 300 surplus = **2 100** | **420** |

Per-building return rises monotonically with chain depth (240 → 300 → 420). Combined with
shallow market depth on raw goods (§7), dumping 120 wood/h crashes the wood price within
minutes, while 30 bread/h barely moves bread. Both forces point the same way.

**Balance invariant (asserted in tests):** `coinsPerHourPerBuilding` must be strictly
increasing across the three strategies above. If a tuning change breaks this, the test
fails and the change is rejected.

## 7. NPC market model

### 7.1 Price impact of selling
```
impact    = (volume / depth) × elasticity        elasticity = 0.5
newPrice  = clamp(price × (1 - impact), floor, ceiling)
floor     = 0.40 × basePrice
ceiling   = 1.60 × basePrice
```

### 7.2 Price recovery
Prices mean-revert toward base, evaluated lazily on read (no ticking job):
```
recoveryPerMinute = 0.02                          → ~35 min half-life
price(t) = base + (priceAtLastTrade - base) × (1 - 0.02)^minutesElapsed
```
This is a pure function of `(base, lastPrice, lastTradeAt, now)` — computable on demand,
requires no scheduled job, and is trivially unit-testable.

### 7.3 Anti-exploit rules
| Rule | Value | Prevents |
|---|---|---|
| Buy/sell spread | NPC buy price = sell price × **1.25** | Buy-low-sell-high arbitrage loop |
| Transaction fee | **3 %** on sale proceeds | Micro-churn spam |
| Price floor / ceiling | 0.4× / 1.6× base | Total collapse or runaway |
| Per-sale volume cap | Market level × 200 units | One-shot market crash |
| Server-side pricing | Client **never** sends a price | Price tampering |
| Idempotency key | Required on every sale | Double-sell on retry |

The spread rule is the important one: with buy = 1.25 × sell, a round trip always loses
20 %. Infinite arbitrage is structurally impossible, not merely rate-limited.

## 8. Storage

```
capacity(resource) = TownHallStorage(level) + Σ WarehouseStorage(level)
```
Base: Town Hall L1 = 100/resource; Warehouse L1 = 200/resource.

Production **halts** when the output resource is at capacity — it does not overflow and it
does not destroy goods. A halted building shows a warning mote and its animation stops
(pillar P1). This is the primary offline cap and the main driver of warehouse upgrades.

## 9. Progression

### Player XP
| Event | XP |
|---|---|
| Building constructed | 10 × level |
| Building upgraded | 25 × new level |
| Production batch completed | 1 |
| Goods sold | 1 per 20 coins of proceeds |
| Quest completed | quest-specific |

`xpForLevel(n) = 100 × 1.5^(n-1)` → 100, 150, 225, 338, 506 …

### City level
`cityLevel = floor(Σ(building levels) / 4)`, capped by Town Hall level × 2.
Gates plot unlocks and building availability.

### Unlocks
| City level | Unlocks |
|---|---|
| 1 | Lumber Camp, Farm, Warehouse |
| 2 | Sawmill, Mill |
| 3 | Bakery, second plot cluster |
| 4 | *(post-slice: Iron Mine, Foundry, Tool Workshop)* |

## 10. Starting state

```
coins           800
wood             80
grain             0
planks            0
flour             0
bread             0
buildings       Town Hall L1, Market L1 (pre-placed)
plots           8 × 8 grid, 16 unlocked at city level 1
```

Affordability check: Lumber Camp (150c + 20 wood) → 650c / 60 wood left.
Warehouse (250c + 40 wood) → 400c / 20 wood left. Both reachable immediately, which is
required by the tutorial in [`PLAYER_JOURNEY.md`](../game-design/PLAYER_JOURNEY.md).

## 11. Quest rewards (early pacing)

Quest rewards — not raw production — carry the first 15 minutes. This lets build times stay
short without inflating passive income.

| # | Quest | Reward |
|---|---|---|
| 1 | Build a Lumber Camp | 50c, 20 XP |
| 2 | Start wood production | 50c, 20 XP |
| 3 | Build a Warehouse | 100c, 30 XP |
| 4 | Receive first delivery | 100c, 30 XP |
| 5 | Sell goods at the Market | 200c, 50 XP |
| 6 | Upgrade any building | 300c, 80 XP |
| 7 | Build a Sawmill | 400c, 100 XP |

Cumulative: **1 200 coins, 330 XP** → player level 3 by the end of the tutorial chain,
with enough capital to reach the Bakery in roughly 45–60 minutes of mixed play.

## 12. What the simulator must prove

`tools/economy-simulator` (Phase 6) runs 1 / 7 / 30-day horizons against three archetypes —
**Optimiser** (perfect play), **Casual** (3 sessions/day), **Idler** (1 session/day) — and
must report:

- [ ] No strategy yields negative return after its payback window
- [ ] Bread chain beats plank-only by 30–60 % coins/h/building (not 5 %, not 500 %)
- [ ] Money supply growth < 15 %/day at steady state (inflation guard)
- [ ] No resource price pinned at floor or ceiling for > 2 h
- [ ] Warehouse is the binding constraint for the Idler, not production rate
- [ ] No dominant strategy: top and second strategy within 25 % of each other

Any run violating these fails the build. See
[`BALANCE_ASSUMPTIONS.md`](BALANCE_ASSUMPTIONS.md) for what each number assumes and how to
falsify it.
