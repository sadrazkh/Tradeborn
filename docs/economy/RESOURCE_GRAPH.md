# Resource Graph

> Machine-checkable description of the vertical-slice economy. The JSON block in §4 is the
> **canonical shape** consumed by seed data and the economy simulator. Keep it in sync with
> [`ECONOMY_DESIGN.md`](ECONOMY_DESIGN.md).

## 1. Graph

```
              ┌──────────────┐
              │ Lumber Camp  │  ∅ → wood        120/h
              └──────┬───────┘
                     │ wood
                     ▼
              ┌──────────────┐
              │   Sawmill    │  2 wood → 1 planks    60/h
              └──────┬───────┘
                     │ planks
          ┌──────────┴──────────┐
          │ 30/h                │ 30/h  (surplus)
          ▼                     ▼
   ┌─────────────┐        ┌──────────┐
   │   Bakery    │        │  MARKET  │
   │ 2 flour     │        └──────────┘
   │ + 1 planks  │              ▲
   │ → 1 bread   │──── bread ───┘
   └─────────────┘   30/h
          ▲
          │ flour 60/h
   ┌──────┴───────┐
   │     Mill     │  2 grain → 1 flour    60/h
   └──────┬───────┘
          │ grain
   ┌──────┴───────┐
   │     Farm     │  ∅ → grain       120/h
   └──────────────┘
```

## 2. Graph properties

| Property | Value | Why it matters |
|---|---|---|
| Depth | 3 tiers (raw → processed → finished) | Enough for a real value ladder, small enough to learn in one session |
| Nodes | 5 resources, 6 producing buildings | Under the cognitive limit for a first hour |
| Convergence points | 1 (Bakery) | The single point where two chains must be balanced |
| Divergence points | 1 (Planks: sell vs. consume) | The single designed economic decision |
| Cycles | **none** | Guarantees settlement terminates; asserted in `ArchitectureTests` |
| Dead ends | none — every resource has a market | No resource is ever worthless |

**Acyclicity is a hard invariant.** The offline settlement algorithm resolves buildings in
topological order (see [`../architecture/REALTIME_AND_TIME_MODEL.md`](../architecture/REALTIME_AND_TIME_MODEL.md) §5).
A cycle would make settlement non-terminating. A unit test walks the seeded recipe graph
and fails on any cycle.

## 3. Balance ratios (integer by design)

| Relationship | Ratio | Meaning |
|---|---|---|
| Lumber Camp → Sawmill | **1 : 1** | One camp exactly feeds one sawmill (120 wood in, 120 consumed) |
| Farm → Mill | **1 : 1** | Same |
| Mill → Bakery | **1 : 1** | 60 flour out, 60 consumed |
| Sawmill → Bakery | **1 : 2** | Sawmill makes 60 planks, bakery eats 30 → **surplus 30** |

Every ratio is a clean integer so a player can reason about it without arithmetic. The
deliberate exception is Sawmill → Bakery, which is where the game asks its first real
question.

Minimum complete bread chain: **5 buildings** — 1 Lumber Camp, 1 Sawmill, 1 Farm, 1 Mill,
1 Bakery. Steady-state output: 30 bread/h + 30 surplus planks/h.

## 4. Canonical data

```json
{
  "schemaVersion": 1,
  "resources": [
    { "id": "wood",   "tier": "raw",       "basePriceCoins": 2,  "marketDepth": 500 },
    { "id": "grain",  "tier": "raw",       "basePriceCoins": 2,  "marketDepth": 500 },
    { "id": "planks", "tier": "processed", "basePriceCoins": 10, "marketDepth": 300 },
    { "id": "flour",  "tier": "processed", "basePriceCoins": 10, "marketDepth": 300 },
    { "id": "bread",  "tier": "finished",  "basePriceCoins": 60, "marketDepth": 150 }
  ],
  "recipes": [
    { "id": "extract_wood",  "building": "lumber_camp", "cycleSeconds": 30,
      "inputs": [],                                            "outputs": [{ "resource": "wood",   "qty": 1 }] },
    { "id": "extract_grain", "building": "farm",        "cycleSeconds": 30,
      "inputs": [],                                            "outputs": [{ "resource": "grain",  "qty": 1 }] },
    { "id": "saw_planks",    "building": "sawmill",     "cycleSeconds": 60,
      "inputs": [{ "resource": "wood",  "qty": 2 }],           "outputs": [{ "resource": "planks", "qty": 1 }] },
    { "id": "mill_flour",    "building": "mill",        "cycleSeconds": 60,
      "inputs": [{ "resource": "grain", "qty": 2 }],           "outputs": [{ "resource": "flour",  "qty": 1 }] },
    { "id": "bake_bread",    "building": "bakery",      "cycleSeconds": 120,
      "inputs": [{ "resource": "flour", "qty": 2 },
                 { "resource": "planks","qty": 1 }],           "outputs": [{ "resource": "bread",  "qty": 1 }] }
  ],
  "buildings": [
    { "id": "town_hall",   "prePlaced": true,  "storagePerResource": 100, "unlockCityLevel": 1 },
    { "id": "market",      "prePlaced": true,                             "unlockCityLevel": 1 },
    { "id": "lumber_camp", "buildCost": { "coins": 150, "wood": 20 },  "buildSeconds": 30,  "unlockCityLevel": 1 },
    { "id": "farm",        "buildCost": { "coins": 150, "wood": 20 },  "buildSeconds": 30,  "unlockCityLevel": 1 },
    { "id": "warehouse",   "buildCost": { "coins": 250, "wood": 40 },  "buildSeconds": 60,  "unlockCityLevel": 1,
      "storagePerResource": 200 },
    { "id": "sawmill",     "buildCost": { "coins": 400, "wood": 60 },  "buildSeconds": 120, "unlockCityLevel": 2 },
    { "id": "mill",        "buildCost": { "coins": 400, "wood": 60 },  "buildSeconds": 120, "unlockCityLevel": 2 },
    { "id": "bakery",      "buildCost": { "coins": 900, "wood": 40, "planks": 30 },
                                                                       "buildSeconds": 300, "unlockCityLevel": 3 }
  ],
  "upgradeCurve": { "outputFactor": 1.6, "costFactor": 2.5, "timeFactor": 3.0, "maxLevel": 3 },
  "market": {
    "elasticity": 0.5, "recoveryPerMinute": 0.02,
    "priceFloorFactor": 0.4, "priceCeilingFactor": 1.6,
    "buySellSpread": 1.25, "transactionFeePercent": 3
  }
}
```

## 5. Post-slice extension (design only — do not build)

The third chain slots in without touching any of the above:

```
Iron Mine → iron_ore ──► Foundry (2 iron_ore → 1 iron) ──┐
                                                          ├──► Tool Workshop
Sawmill  → planks ───────────────────────────────────────┘    (2 iron + 2 planks → 1 tools)
```

This adds a **second** consumer of planks, converting the slice's surplus decision into a
three-way choice. Deliberately deferred: it adds tension but no new *kind* of decision, so
it belongs after the slice is proven fun.
