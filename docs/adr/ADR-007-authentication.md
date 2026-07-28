# ADR-007 — JWT access token + rotating refresh cookie

**Status:** Accepted (Phase 0)

## Context

A same-origin SPA served by ASP.NET needs authentication that survives refresh, is safe
against XSS token theft, works with SignalR, and does not require a session store.

## Decision

- **Access token:** JWT, **15 min**, held **in memory only** (JS variable, never
  `localStorage` or `sessionStorage`)
- **Refresh token:** opaque random value, **30 days**, in an `HttpOnly` + `Secure` +
  `SameSite=Strict` cookie, **rotated on every use** with reuse detection
- **Passwords:** Argon2id (fallback bcrypt cost 12)

## Rationale

**Why not tokens in `localStorage`:** any XSS becomes permanent account compromise. An
in-memory access token dies with the tab and is only valid for 15 minutes.

**Why not a pure cookie session:** SignalR and future non-browser clients are simpler with a
bearer token, and a stateless access token avoids a session lookup per request.

**Why rotation with reuse detection:** if a refresh token is ever replayed — meaning it was
stolen — the entire token family is revoked and the user must re-authenticate. This turns
refresh-token theft from a silent long-term compromise into a detected, contained event.

**Why `SameSite=Strict`:** the SPA is same-origin by design, so `Strict` costs nothing and
removes classic CSRF from the threat model. The refresh endpoint additionally uses a
double-submit token.

## Flow

```
Login       → access (memory, 15 min) + refresh cookie (30 d)
API call    → Authorization: Bearer <access>
401         → POST /auth/refresh (cookie) → new access + NEW refresh; old one invalidated
Reuse of an already-rotated refresh → revoke entire family, force re-login
Logout      → revoke family, clear cookie
```

On page load the SPA holds no access token, so it silently calls `/auth/refresh` first. This
is why a refresh restores the session without a login prompt.

## SignalR

The access token is passed via the query string on connect (SignalR's standard mechanism for
WebSockets, which cannot set headers) and validated per connection. Short token lifetime
limits the exposure of a token appearing in a URL; hub reconnection fetches a fresh one.

## Consequences

**Positive:** XSS cannot steal a long-lived credential; CSRF largely eliminated; no session
store; stateless validation; theft is detectable.

**Negative:** a refresh round trip on every page load (~50 ms, once). Acceptable.

**Negative:** refresh-token families require storage and cleanup. One table plus a periodic
purge job.

## Extension

`IAuthenticationProvider` abstracts the credential check so OAuth/social login
([Q-03](../roadmap/DECISIONS_REQUIRED.md)) can be added without touching token issuance.
Roles: `Player`, `Admin`, `Support` (read-only); admin endpoints require a separate policy
and are IP-restricted in production.
