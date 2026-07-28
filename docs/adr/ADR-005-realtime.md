# ADR-005 — SignalR for notifications only, with polling fallback

**Status:** Accepted (Phase 0)

## Context

Players need to see construction finish, transports arrive, and prices move without
refreshing. But this is an *asynchronous* game — nothing depends on sub-second latency, and
a real-time outage must not be a game outage.

## Decision

**SignalR** over WebSockets, carrying **notifications, not state**. Polling every 15 s is a
complete functional fallback.

| Message | Payload |
|---|---|
| `ConstructionCompleted` | buildingId, newLevel |
| `TransportArrived` | jobId, resources |
| `MarketPriceChanged` | resourceId, price |
| `BuildingHalted` | buildingId, reason |

## Rationale

**Why SignalR:** first-party in ASP.NET Core, automatic transport negotiation
(WebSocket → SSE → long-polling), built-in reconnection with backoff, typed hubs, and a
Redis backplane available when we need scale-out. Raw WebSockets would mean rebuilding all
of that.

**Why notifications and not state:** if a push could mutate the client's economic view, then
a dropped, duplicated, or reordered message becomes an economic bug. By making every push a
*hint to refresh*, the worst case of any delivery failure is staleness — which polling then
corrects. This is what keeps real-time an enhancement rather than a dependency.

**Why a polling fallback:** corporate proxies, aggressive mobile networks, and backgrounded
tabs all break WebSockets routinely. A game that stops working in those conditions is
broken for a meaningful fraction of players.

## Consequences

**Positive:** a hub outage degrades responsiveness, never correctness. Reconnection needs no
special state-reconciliation logic — the client simply refetches. Scale-out is a
configuration change (Redis backplane), deferred to Phase 9.

**Negative:** a notification followed by a refetch costs an extra round trip versus pushing
state directly. At our message volume this is irrelevant, and deltas keep payloads small.

**Negative:** two code paths (push and poll) to keep behaviourally equivalent. Mitigated by
an integration test that runs the full loop **with the hub disabled** — the fallback is
tested, not assumed.

## Rules

1. A notification is never authoritative data.
2. Full state is never broadcast; deltas only.
3. Nothing in the game is unreachable without a working hub.
4. Hub methods are authorised per connection; a client can only subscribe to its own city.
