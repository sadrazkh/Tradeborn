# Vertical Slice — Definition of Done

> **Status:** Approved (Phase 0) · This is the contract. Anything not listed here is out of
> scope until the slice ships.

## 1. Purpose

Prove in **under 10 minutes of play** that Tradeborn is fun, alive, and technically sound.
Not a demo of features — a demo of the *feeling*.

## 2. The 17-step player path (from the brief, mapped to phases)

| # | Step | Phase | Acceptance |
|---|---|---|---|
| 1 | Log in | 1 | Register/login, session survives refresh |
| 2 | Enter own 3D city | 2 | Scene loads ≤ 4 s desktop, city state from server |
| 3 | Smooth camera | 2 | Orbit/pan/zoom, mouse + touch, 60 fps desktop |
| 4 | Select a plot | 2 | Hover lift, click select, valid/invalid indicator |
| 5 | Build a building | 3 | Server-validated placement, cost deducted atomically |
| 6 | Watch staged construction | 3 | 4 visual stages driven by server progress |
| 7 | Start production | 4 | Production order accepted, building animates |
| 8 | See goods hauled | 5 | Vehicle drives factory → warehouse along road graph |
| 9 | Goods delivered to warehouse | 5 | Server-side delivery, inventory increases |
| 10 | Sell at market | 6 | NPC market, server-priced, price moves |
| 11 | Receive coins + XP | 6 | HUD updates, coin-fly animation, ledger entry |
| 12 | Upgrade a building | 3 | Cost validated, upgrade queued |
| 13 | See the 3D change | 3 | Different mesh at L2, visibly larger |
| 14 | See a small economic event | 6 | Price shift with in-world + HUD signal |
| 15 | Get a quest and reward | 7 | 7-quest tutorial chain, rewards granted once |
| 16 | Full server-side save | 1–6 | Refresh restores exact state |
| 17 | Production continues offline | 4 | Close browser 10 min → return → correct output |

## 3. Content scope (fixed)

| | Count | Items |
|---|---|---|
| Resources | 5 | wood, grain, planks, flour, bread |
| Buildings | 8 | Town Hall, Market, Lumber Camp, Farm, Warehouse, Sawmill, Mill, Bakery |
| Recipes | 5 | extract ×2, process ×2, assemble ×1 |
| Building levels | 3 | |
| Plots | 64 (8×8) | 16 unlocked at city level 1 |
| Quests | 7 | Tutorial chain |
| Vehicles | 1 type | Cart/truck, pooled, max 10 concurrent |
| Terrain types | 3 | Grass, dirt, stone |

Numbers in [`../economy/ECONOMY_DESIGN.md`](../economy/ECONOMY_DESIGN.md).

## 4. Explicitly excluded

Alliances · PvP · chat · world map · player-to-player market · stock exchange · politics ·
banking · multiple continents · NFT/blockchain · real-money anything · microservices ·
Kubernetes · AI in the core loop · third-person controller · combat · Telegram Mini App.

Extension points and roadmap notes only — no code.

## 5. Acceptance criteria

### Player experience
- [ ] New player reaches first sale in **< 6 min** with no external explanation
- [ ] No tables or forms on the main screen
- [ ] Every economic change has a visible in-world consequence
- [ ] City looks visibly different after 10 min of play
- [ ] Playable one-handed on a phone
- [ ] Player can articulate why they built their second building

### Technical
- [ ] `dotnet build` clean, zero warnings in `Domain`/`Application`
- [ ] All unit, integration, and architecture tests green
- [ ] Migrations apply from empty DB; seed is idempotent (running twice changes nothing)
- [ ] Documented one-command local startup, verified from a clean clone
- [ ] Client cannot influence any economic outcome (T1–T12 in `SECURITY_MODEL.md` tested)
- [ ] No double-spend under 20 parallel identical requests
- [ ] No duplicate reward under replayed idempotency key
- [ ] Desktop ≥ 50 fps p95; mobile ≥ 25 fps p95
- [ ] WebGL2 fallback verified with WebGPU force-disabled
- [ ] SignalR reconnect restores correct state; polling fallback works with hub disabled
- [ ] Health checks, structured logs, correlation ids live
- [ ] Settling 8 h in one jump == settling in 480 one-minute jumps (determinism)

## 6. Demo script (the 6-minute proof)

```
0:00  Register → city loads, camera sweeps over the plots
0:20  Tutorial: orbit and zoom
0:40  Select plot → build Lumber Camp (150c + 20 wood)
0:50  Construction stages play; quest 1 completes (+50c)
1:20  Lumber Camp starts producing; saw motion, smoke, wood counter rises
1:40  Build Warehouse; capacity indicator grows
2:10  Cart drives camp → warehouse, unloads. Quest 4 (+100c)
2:40  Open Market → sell 60 wood → coins fly to HUD, wood price visibly dips
3:00  Quest 5 (+200c). Build Sawmill
3:40  Sawmill consumes wood → produces planks. Chain visibly connected
4:20  Upgrade Lumber Camp → L2 mesh is larger, output rises
5:00  Economic event: "Bread demand rising" — market signal + in-world reaction
5:30  Refresh the page → identical state restored
6:00  Session recap shows offline production
```

If any beat here needs verbal explanation to land, the slice is not done.

## 7. Non-goals for the slice

Not aiming for: content volume, art fidelity, balance perfection, scale beyond ~100
concurrent players, or production-grade ops. The slice proves the *core*; phases 8–9 harden it.
