# Admin & Operations

> **Status:** Phase 8. The panel's API exists and is authorised; the panel's **UI does not**.
> Everything below is reachable with an HTTP client today.

## 1. Roles

| Role | Can | Policy |
|---|---|---|
| `Player` | Play. Nothing under `/api/admin`. | — |
| `Support` | Read everything: players, cities, audit, economy, flags. | `admin.read` |
| `Admin` | Everything Support can, plus tuning, flags, grants, resets. | `admin.write` |

Two policies rather than one, deliberately. Most operator work is *reading* — who is playing,
why a balance looks wrong — and read access should not carry the ability to hand out money.

**Elevation is a database operation, not an API call.** There is no endpoint that grants a
role, because an endpoint that can grant Admin is an endpoint that can be tricked into
granting Admin:

```sql
UPDATE players SET "Role" = 'Admin' WHERE "Email" = 'you@example.com';
```

The change takes effect on the player's next access token, so they must sign in again (or wait
out the 15-minute token lifetime).

## 2. Endpoints

### Read — `admin.read`
| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/admin/system` | Counts, money supply, environment, whether Redis is configured |
| `GET` | `/api/admin/players?page&pageSize&search` | Paged player list |
| `GET` | `/api/admin/players/{playerId}/city` | Full city inspection |
| `GET` | `/api/admin/audit?playerId&page&pageSize` | Audit ledger |
| `GET` | `/api/admin/economy` | Current tuning values |
| `GET` | `/api/admin/flags` | Feature flags |

### Write — `admin.write`
| Method | Path | Purpose |
|---|---|---|
| `PUT` | `/api/admin/economy` | Apply tuning and hot-reload the catalog |
| `PUT` | `/api/admin/flags/{key}` | Set a feature flag |
| `POST` | `/api/admin/players/{playerId}/grant` | Grant coins/materials — **audited** |
| `POST` | `/api/admin/players/{playerId}/reset` | Zero a test city's economy — **audited** |

Page sizes are clamped to 100 regardless of what is asked for. An admin panel is exactly where
an unbounded query gets written and then quietly loads a hundred thousand rows in production.

## 3. Retuning the economy without a deploy

This is Phase 8's acceptance criterion. Read the current values, change what you want, PUT the
whole document back:

```bash
curl -s -H "Authorization: Bearer $TOKEN" http://localhost:5084/api/admin/economy > tuning.json
```

Edit `tuning.json`, then:

```bash
curl -X PUT -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" --data @tuning.json http://localhost:5084/api/admin/economy
```

**The whole document, not one field at a time.** Economy numbers are only meaningful relative
to each other — raising bread's price without looking at flour is how a balance pass goes
wrong — so the panel edits them together.

**What actually happens.** The rows are written, then the in-memory catalog is reloaded. The
reload is the point: without it the rows would change while every running request kept using
the catalog loaded at startup, so the tool would appear to work and change nothing.

**What does not change instantly.** Existing market prices drift toward the new base over about
35 minutes (mean reversion, `ECONOMY_DESIGN.md` §7). Buildings mid-construction keep the
duration they started with. Both are correct: retuning must not retroactively rewrite work a
player already paid for.

**What tuning cannot do.** Unknown ids are skipped rather than created. Adding a resource or a
recipe changes the graph and its topological ranks, which is a seed change and a deploy — not
a slider. Values that would break settlement (a cycle time of zero) are rejected silently
rather than accepted.

**After any tuning pass, re-run the economy simulator** (`ECONOMY_DESIGN.md` §12) before
leaving it in place. The invariants it checks — no dominant strategy, per-building return
rising with chain depth — are exactly what a hurried price change breaks.

## 4. Audit trail

Every economic mutation is in `audit_ledger`, written in the same transaction as the change
(ADR-004). Operator actions additionally record `ActorPlayerId`.

That column matters more than it looks: without it the ledger records that a player's balance
rose by 5 000 and nothing about the operator who did it — which is precisely the question an
audit exists to answer.

```bash
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:5084/api/admin/audit?playerId=$PLAYER&pageSize=50"
```

Kinds currently written: `construction.started`, `upgrade.started`, `production.started`,
`production.paused`, `market.sold`, `quest.claimed`, `admin.granted`, `admin.reset`.

**Reconciliation.** `BalanceAfterCent` on each entry lets any balance be checked by replaying
the deltas. An integration test asserts the sum matches the stored balance, which catches any
mutation that bypassed the ledger.

## 5. Operator actions

Both require a **reason**. An unexplained grant is indistinguishable from abuse, and the reason
is stored in the audit metadata.

```bash
curl -X POST -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" -d '{"coins":500,"resources":[{"resource":"wood","quantity":100}],"reason":"support ticket 412"}' http://localhost:5084/api/admin/players/$PLAYER/grant
```

Grants are bounded — 100 000 coins and 10 000 units per resource. An unbounded grant endpoint
is one compromised admin account away from wrecking the economy, and no legitimate support case
needs more.

**Reset does not delete buildings.** It zeroes coins and inventory. Removing rows would take
the plot layout, the audit trail's subjects and the quest history with it; zeroing gives a
clean economic state to test against while leaving everything explicable afterwards.

## 6. Not built

- **The panel UI.** The API is complete and authorised; there is no front end for it yet.
- **Job monitoring.** There is no job queue to monitor — construction and delivery complete
  inside settlement (ADR-003), which is why none was needed.
- **Online player count.** Requires SignalR connection tracking, which arrives with real-time.
- **Running the simulator from the panel.** The simulator itself is not built yet.
