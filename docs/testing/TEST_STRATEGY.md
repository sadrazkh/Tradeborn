# Test Strategy

> A test that cannot fail is documentation with a build cost. Every test here exists to
> catch a specific, named defect.

## 1. Shape

```
        ╱  E2E (Playwright)        ~15   the 6-min demo, FPS, mobile
       ╱   Integration (real DB)   ~60   concurrency, idempotency, security
      ╱    Architecture             ~15   boundaries, banned APIs
     ╱     Unit (Domain/App)      ~250   economy rules, settlement, pricing
```

Weighted toward unit tests because **the economy is pure logic**. Deterministic settlement
and integer arithmetic mean almost every economic rule is testable with no I/O.

## 2. Unit tests — `Tradeborn.UnitTests`

No database, no HTTP, no clock. `FakeTimeProvider` everywhere.

**What must be covered:**
- `Money`: negative balance throws; overflow checked; no implicit float conversion
- Settlement: cycles limited by time / input / capacity, independently and together
- Settlement: `ProgressSeconds` preserved across partial cycles
- Settlement: `HaltReason` correctly identifies the binding constraint
- Upgrade curves produce expected integer rates at levels 1–3 (rounding guard, A-10)
- Market: price impact, mean reversion, floor/ceiling clamping
- Market: buy price is always ≥ 1.25 × sell price
- XP and level thresholds
- Placement rules: occupied, locked, unaffordable, prerequisite missing
- Recipe graph is acyclic

**The two invariant tests that matter most:**

```csharp
[Fact] // Determinism — the foundation of the whole time model
public void Settling_in_one_jump_equals_settling_in_many_steps()
{
    var a = City.Seeded(); var b = City.Seeded();
    Settle(a, start.AddHours(8));
    for (var i = 1; i <= 480; i++) Settle(b, start.AddMinutes(i));
    a.Should().BeEquivalentTo(b);
}

[Fact] // Balance — processing must beat extraction per building
public void Deeper_chains_yield_more_coins_per_hour_per_building()
{
    Raw().Should().BeLessThan(Processed());
    Processed().Should().BeLessThan(Finished());
}
```

**Property-based (FsCheck):** random buy/sell sequences against the NPC market must never
increase the player's terminal balance without production (arbitrage impossibility, T10).

## 3. Integration tests — `Tradeborn.IntegrationTests`

Real PostgreSQL, real EF Core, `WebApplicationFactory`.

**Database provisioning:**
1. If `TRADEBORN_TEST_POSTGRES` is set → use it (the local-dev path on this machine).
2. Else if Docker is available → Testcontainers, throwaway container per run.
3. Else → **skip with an explicit message**, never silently pass.

Because Docker is absent locally (`RISKS.md` R-09), the default local path is (1) against a
`tradeborn_test` database, dropped and recreated per run. CI always has Docker and uses (2).

**What must be covered:**
- Migrations apply from empty; seed is idempotent (run twice → identical state)
- **T3** replayed `Idempotency-Key` → one effect, identical response
- **T4** 20 parallel builds on one plot → exactly 1 succeeds, 19 get a clean error
- **T4** 20 parallel sales of the same stock → total sold ≤ stock held
- **T5** quest reward claimed twice → granted once
- **T1** forged resource/coin amounts in a request body are ignored
- **T2** tampered price is ignored; server price is used
- **T7** cross-tenant access → 404 (not 403 — do not confirm existence)
- **T12** refresh-token reuse revokes the family
- Ledger reconciliation: sum of deltas == stored balance, for every player
- City read path issues **no N+1 queries** (EF interceptor counts queries)
- Full state restored after simulated restart
- App behaves correctly with the SignalR hub disabled (polling fallback)
- App boots with Redis unreachable (A-01)
- Production environment: Swagger and debug endpoints absent

## 4. Architecture tests — `Tradeborn.ArchitectureTests`

Cheap, fast, and they prevent the slow decay that makes a monolith a big ball of mud.

- `Domain` references no external package except the BCL
- `Domain` does not reference `Application`, `Infrastructure`, or `Web`
- `Application` does not reference `Infrastructure` or `Web`
- No `DateTime.Now` / `DateTime.UtcNow` / `DateTimeOffset.UtcNow` in `Domain` or `Application`
- No `float` / `double` in any type under `Domain.Economy`
- Entities are not exposed by `Web` endpoints (DTOs only)
- Cross-module: `Domain.<A>` does not reference `Domain.<B>` internals outside the shared kernel
- All `Application` command handlers are `internal sealed`
- Every public endpoint has an authorisation attribute or an explicit `AllowAnonymous`

## 5. Frontend tests

**Unit (Vitest):** Pinia stores, API client, formatters, clock-offset calculation, pure
helpers in `game/` (grid math, path building, price formatting).

**Component (Vitest + Testing Library):** HUD components with loading, error, and empty
states. Every component that fetches must have all three tested — missing states are the
most common UI defect.

**Not unit-tested:** Babylon rendering. It is covered by E2E through the debug bridge, which
is honest about what is actually being verified.

## 6. E2E — `Tradeborn.EndToEndTests` (Playwright, Phase 7)

Testing a `<canvas>` requires the `window.__tradeborn` bridge
(`../art-direction/SCENE_GUIDELINES.md` §9). Without it these tests would only be able to
assert that a canvas element exists.

**The critical path** — the 6-minute demo script from `VERTICAL_SLICE.md` §6, asserted beat
by beat:
```ts
await expect.poll(() => page.evaluate(() => __tradeborn.ready())).toBe(true)
await placeBuilding('lumber_camp', { col: 3, row: 4 })
await expect.poll(() => buildingAt(3, 4)?.state).toBe('UnderConstruction')
await expect.poll(() => buildingAt(3, 4)?.state, { timeout: 40_000 }).toBe('Idle')
```

**Also covered:**
- WebGL2 fallback: launch with WebGPU disabled → `__tradeborn.renderer() === 'webgl2'`
- Performance: fixed 60 s camera path, assert p95 FPS ≥ 50 desktop / ≥ 25 mobile emulation
- Budgets: assert draw calls and triangles within `PERFORMANCE_BUDGET.md` limits
- Mobile viewport: full loop completable with touch only
- Refresh mid-play restores identical state
- Memory: 10 min soak, heap growth within budget

## 7. Economy simulation — `tools/economy-simulator` (Phase 6)

Not a test framework, but its output gates the build. Runs 1/7/30-day horizons for
Optimiser / Casual / Idler and asserts the six invariants in `ECONOMY_DESIGN.md` §12. A
violated invariant fails CI with a report naming which one and by how much.

## 8. CI pipeline

```
lint (dotnet format, eslint)
  → build (warnings as errors in Domain + Application)
  → unit tests
  → architecture tests
  → integration tests (Testcontainers)
  → frontend unit tests
  → bundle size gate
  → gitleaks
  → [phase 6+] economy simulation
  → [phase 7+] E2E
```

Fails on: any test failure, coverage drop in `Domain` below 85 %, bundle over budget, secret
detected, or an economy invariant violation.

## 9. What we deliberately do not test

- Babylon's own rendering correctness (trust the engine)
- EF Core query translation (trust the ORM; we *do* assert query counts)
- Exact pixel output (brittle; silhouette review is manual and human)
- Third-party library internals

## 10. Manual QA checklist (per phase)

- [ ] Clean clone → documented command → app runs
- [ ] Register, play 10 minutes, refresh — state intact
- [ ] Real mid-range phone: playable, readable, one-handed
- [ ] Offline 10 minutes → return → production correct and recap shown
- [ ] Devtools console clean of errors and warnings
- [ ] Network tab: no oversized or duplicated requests
- [ ] Slow 3G throttle: loading is graceful, not broken
