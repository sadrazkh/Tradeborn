# Security Model

> **Status:** Approved (Phase 0) · Scope: **economic integrity under a web threat model.**
> Not in scope: kernel anti-cheat, client attestation, DRM.

## 1. Threat model

The attacker is a competent player with browser devtools, an HTTP client, and patience.
They can read all client code, forge any request, replay requests, run requests in
parallel, and lie about their clock. They cannot break TLS or read the server's database.

**Therefore:** the client is treated as hostile input. Always.

## 2. Threats and controls

| # | Threat | Control | Verified by |
|---|---|---|---|
| T1 | Forge resource/coin amounts | Client sends **intent only** (`build X on plot Y`). Server computes all amounts. | Integration test: forged body ignored |
| T2 | Manipulate market price | Price never accepted from client. Server re-reads price inside the transaction. | Integration test: sale with tampered price |
| T3 | Replay a sale to double-spend | Mandatory `Idempotency-Key`, unique per player, stored in the same transaction | Integration test: same key twice → one effect |
| T4 | Concurrent requests to double-spend | `SELECT … FOR UPDATE` on the city row; `xmin` concurrency token | Integration test: 20 parallel builds, 1 succeeds |
| T5 | Claim a quest reward twice | Reward claim is a state transition guarded by status + idempotency | Integration test |
| T6 | Fake the clock to finish construction early | All time from server `TimeProvider`; client timestamps ignored | Architecture test bans `DateTime.UtcNow` in Domain/Application |
| T7 | Build on another player's city | Every command resolves the city **from the token**, never from the request body | Integration test: cross-tenant attempt → 404 |
| T8 | Bypass unlock requirements | Server validates city level, prerequisites, and cost against settled state | Unit tests per rule |
| T9 | Spam requests to brute-force races | Per-endpoint + per-player rate limiting | Integration test |
| T10 | Infinite arbitrage on NPC market | 1.25× buy/sell spread makes round trips structurally lossy | Property-based test |
| T11 | Exfiltrate other players' data | Authorisation on every endpoint; responses are player-scoped projections | Integration test |
| T12 | Token theft | Short-lived access token; refresh token in `HttpOnly`+`Secure`+`SameSite=Strict` cookie, rotated on use | Manual + integration |

## 3. Core principle — intent, not outcome

```
❌  POST /api/market/sell  { "resource":"bread", "qty":10, "price":60, "total":600 }
✅  POST /api/market/sell  { "resource":"bread", "qty":10 }
    Idempotency-Key: 7f3a…
```

The server determines price, fee, and total. Any client-supplied field that could
influence an outcome is **absent from the DTO entirely** — not validated, not present. A
field that does not exist cannot be tampered with.

The same rule governs construction, upgrades, production orders, and quest claims.

## 4. Authentication & authorisation

- **Access token:** JWT, 15 min, in memory only (never `localStorage`).
- **Refresh token:** opaque, 30 days, `HttpOnly` + `Secure` + `SameSite=Strict` cookie,
  **rotated on every use** with reuse detection — a replayed refresh token revokes the
  whole family and forces re-login.
- Passwords hashed with **Argon2id** (fallback bcrypt cost 12).
- Every endpoint is `RequireAuthorization()` by default; anonymous access is opt-in per
  endpoint and reviewed.
- Roles: `Player`, `Admin`, `Support` (read-only). Admin endpoints additionally require a
  separate policy and are IP-restricted in production.

See [ADR-007](../adr/ADR-007-authentication.md).

## 5. Rate limiting

| Scope | Limit |
|---|---|
| Anonymous, per IP | 30 req/min |
| Authenticated, global per player | 240 req/min |
| Economic commands, per player | 60 req/min |
| Login, per IP | 10 req/5 min, exponential backoff |
| Registration, per IP | 5 req/hour |

Implemented with .NET built-in rate limiting; backed by Redis from Phase 3 so limits hold
across instances. Exceeding a limit returns `429` with `Retry-After` — never a silent drop.

## 6. Audit trail

Every economic mutation appends to an immutable ledger **inside the same transaction**:

```
audit_ledger(id, player_id, city_id, occurred_at_utc, kind,
             resource_deltas jsonb, money_delta_cent, balance_after_cent,
             correlation_id, idempotency_key, metadata jsonb)
```

Properties:
- Append-only. No `UPDATE`/`DELETE` grant on the table for the application role.
- `balance_after_cent` lets any balance be reconciled by replay — an integration test sums
  deltas and asserts they equal the stored balance.
- Retained 90 days hot, then archived.

This is what makes fraud investigation and rollback possible without event sourcing
([ADR-004](../adr/ADR-004-economy-persistence.md)).

## 7. Input validation

- FluentValidation on every command DTO; validation runs **before** the handler.
- Quantities: bounded `long`, must be > 0 and ≤ a per-operation cap.
- Ids: strongly-typed structs parsed at the boundary; malformed ids → `400`, never `500`.
- All string input length-capped and HTML-escaped on output.
- JSON depth and size limits enabled on the request pipeline.
- **Domain invariants are enforced in the domain too** — validation is a UX nicety and the
  first line of defence, not the last. `Money` throws on negative balances regardless of
  what any validator did.

## 8. Secrets

- Never committed. `.gitignore` covers `appsettings.*.Local.json`, `.env*`, `secrets.json`.
- Local dev: .NET User Secrets. Production: environment variables / secret store.
- `appsettings.json` ships **only** non-secret defaults and placeholder connection strings.
- CI runs `gitleaks` on every push; a hit fails the build.
- JWT signing key: ≥ 256-bit, rotated on incident, never in source.

## 9. Transport & headers

HTTPS enforced (HSTS in production). Response headers: `Content-Security-Policy` (no
`unsafe-eval`; WebGPU/WebGL need none), `X-Content-Type-Options: nosniff`,
`Referrer-Policy: strict-origin-when-cross-origin`, `X-Frame-Options: DENY`.

CORS is **not enabled** — the SPA is same-origin by design. If a future Telegram Mini App
needs it, it gets an explicit allow-list, never `*`.

CSRF: the refresh cookie is `SameSite=Strict` and the API is JSON-only with a required
`Authorization` header, so classic form CSRF does not apply. The refresh endpoint
additionally requires a double-submit token.

## 10. Production hardening

- Swagger/OpenAPI, debug endpoints, and the developer exception page are **disabled** in
  production by configuration, and their absence is asserted by an integration test running
  in the Production environment.
- Detailed errors never reach the client; correlation id does, so support can find the log.
- Database user has no DDL rights at runtime; migrations run as a separate role.
- Health endpoints: `/health/live` anonymous and minimal, `/health/ready` restricted.

## 11. Anti-cheat philosophy

We do not try to make the client trustworthy — that is unwinnable on the web. We make the
client **irrelevant**: it holds no authority, so compromising it yields nothing beyond a
prettier way to send the same intents any HTTP client could send.

Detection of anomalous *patterns* (impossible action rates, statistically improbable income)
is an analytics concern for Phase 8+, feeding a review queue rather than automated bans.
