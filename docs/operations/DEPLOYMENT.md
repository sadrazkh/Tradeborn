# Deployment & Backup

> **Status:** Phase 8 documentation. **Nothing here has been executed** — Tradeborn has never
> been deployed anywhere. This is the intended procedure, written now so the decisions are
> made before the first deploy rather than during it. Marked clearly so nobody mistakes it for
> a tested runbook.

## 1. What ships

One container. `dotnet publish` runs `npm ci && npm run build`, emits the SPA into `wwwroot/`,
and packages everything (ADR-002). There is no separate frontend deployment, no CORS, and no
CDN required to run.

```bash
dotnet publish src/Tradeborn.Web/Tradeborn.Web.csproj -c Release -o ./publish
```

External dependencies: **PostgreSQL** (required) and **Redis** (required in production from
Phase 3 — the app boots without it and logs a warning, which is correct for development and
wrong for production).

## 2. Configuration

Nothing secret is committed. `appsettings.json` carries non-secret defaults and placeholder
connection strings only; CI runs `gitleaks` and fails on a hit.

| Setting | Source in production |
|---|---|
| `ConnectionStrings:Postgres` | Environment variable / secret store |
| `ConnectionStrings:Redis` | Environment variable / secret store |
| `Tradeborn:Auth:SigningKey` | Secret store, ≥ 32 chars, rotated on incident |
| `ASPNETCORE_ENVIRONMENT` | `Production` |

**Startup fails fast** if the signing key is missing or too short. That is deliberate: signing
tokens with a committed constant would be worse than not booting.

Verify after deploy that `GET /api/admin/system` reports `environment: "Production"` and
`redisConfigured: true`. Both are reported rather than assumed precisely so this check is
possible.

## 3. Migrations

Migrations are **not** applied automatically in production. The app applies them on start in
development only; a production deploy runs them as a separate step, as a separate database
role that has DDL rights the application role does not.

```bash
dotnet ef database update --project src/Tradeborn.Infrastructure --startup-project src/Tradeborn.Web
```

Order matters: **migrate, then deploy the new image.** Every migration to date is additive
(new tables, new nullable or defaulted columns), so the previous version keeps running against
the migrated schema. Preserve that property — a migration that drops or renames a column in
the same release as the code that stops using it cannot be rolled back without data loss.

Current migrations, in order:

| Migration | Adds |
|---|---|
| `InitialSchema` | Players, cities, buildings, inventory, plots, catalog, refresh tokens |
| `ConstructionAndAudit` | Construction state, building costs, idempotency keys, audit ledger |
| `TransportAndBuffers` | Transport jobs, output buffers |
| `MarketAndProgress` | Market prices, price history |
| `QuestsAndCounters` | Claimed quests, delivery/sale counters |
| `RolesAndAdmin` | Player roles, feature flags, audit actor |

## 4. Backup

**What actually needs backing up: PostgreSQL, and nothing else.** Everything else is derivable
— `wwwroot` is build output, Redis is a cache, and the game catalog is re-seeded on start.

```bash
pg_dump --format=custom --no-owner --file=tradeborn-$(date +%F).dump "$TRADEBORN_POSTGRES"
```

Suggested policy, not yet in place:

| | |
|---|---|
| Frequency | Nightly full dump, plus WAL archiving for point-in-time recovery |
| Retention | 7 daily, 4 weekly, 6 monthly |
| Location | Off-host, encrypted at rest |
| Restore drill | Quarterly, into a scratch database — a backup nobody has restored is a hope |

Restore:

```bash
pg_restore --clean --if-exists --no-owner --dbname="$TARGET" tradeborn-2026-07-28.dump
```

**The audit ledger is what makes a partial restore survivable.** If a restore loses recent
transactions, the ledger from a later backup can reconstruct balances rather than leaving
players' economies silently wrong (ADR-004).

## 5. Health

| Endpoint | Purpose | Access |
|---|---|---|
| `/health/live` | Process is up | Anonymous, minimal |
| `/health/ready` | Dependencies reachable | Anonymous today — **should be restricted in production** |

`/health/ready` returning dependency detail to the internet is a small information leak.
Restricting it is a deployment concern (reverse proxy or network policy) and is listed in the
hardening checklist below rather than solved in code.

## 6. Production hardening checklist

Not yet done. Each item is deployment configuration rather than application code, which is why
none of it is in the repository:

- [ ] HTTPS enforced, HSTS enabled
- [ ] `/api/admin/*` behind an IP allow-list (`SECURITY_MODEL.md` §10)
- [ ] `/health/ready` not publicly reachable
- [ ] Application database role has no DDL rights; migrations run as a separate role
- [ ] `audit_ledger` has no `UPDATE`/`DELETE` grant for the application role
- [ ] Redis configured and reachable
- [ ] Backups running and one restore drill completed
- [ ] Log aggregation receiving structured logs with correlation ids
- [ ] Rate limits verified under load (Phase 9)

## 7. Rollback

Because migrations are additive, rolling back is redeploying the previous image — the schema
stays ahead and the old code ignores what it does not know about.

Rolling a migration *back* is a different and riskier operation. If it is ever necessary,
restore from backup rather than reversing a migration against live data.
