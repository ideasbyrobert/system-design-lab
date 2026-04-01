# Cart State And Routing Primer

## Routing Key Convention

Storefront now recognizes one session-routing identity:

- header: `X-Session-Key`
- cookie: `lab-session`

Resolution rules:

1. If `X-Session-Key` is present, use it.
2. Otherwise, if `lab-session` is present, use it.
3. Otherwise, generate a new key and return it to the client.

When Storefront generates a new session key, it emits:

- response header `X-Session-Key`
- response cookie `lab-session`

When the client already supplied a session key, Storefront still echoes the effective key in the response header so later layers can observe the convention explicitly.

## Why Correctness Does Not Depend On Sticky Routing

Cart truth is not kept in Storefront memory.

The authoritative cart state is stored in `primary.db` and owned by `Cart.Api`. Storefront only forwards cart mutations and validates the returned contract.

That means:

- request A and request B can hit different Storefront instances
- proxy routing mode can change later
- no Storefront instance needs to "own" a cart in memory

As long as requests reach `Cart.Api` and `Cart.Api` persists the resulting cart state correctly, the cart remains correct even without sticky routing.

This is the important separation:

- correctness comes from durable authoritative state
- routing policy affects performance and locality, not cart truth

## Why Sticky Routing Still Matters

Even when correctness is DB-backed, sticky routing can still change behavior that users feel:

- a proxy can keep the same session flowing to the same backend more often
- per-instance warm caches can become more effective
- connection reuse and locality can improve
- repeated reads or writes from the same session may avoid avoidable cold paths

So stickiness is still worth demonstrating later, but it should be taught honestly:

- it is not required for correctness in this cart design
- it can still change latency, cache hit rate, and locality

## Groundwork Now In Place

This ticket prepares the later proxy experiments by making the routing identity explicit today:

- Storefront emits or echoes `X-Session-Key`
- Storefront can bootstrap `lab-session` when the client has no session key yet
- request traces now include `sessionKey`
- cart request stage metadata records whether the session key came from `header`, `cookie`, or `generated`

That gives the future proxy tickets a stable routing key without forcing cart truth into server memory.
