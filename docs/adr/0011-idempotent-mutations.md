# 0011 — Idempotent mutations

- **Status:** Accepted
- **Date:** 2026-07-25

## Context

A mobile client on a bad connection cannot tell a request that never arrived from one that succeeded and whose response was lost. HTTP offers it nothing here: retrying a `POST` is how you get two applications, two interviews, and — since the events work — two `ApplicationSubmitted` events that Analytics will faithfully count. Not retrying is how you lose the user's entry entirely.

The pipeline makes one case worse than a duplicate. A retried transition is not a second move; it is an *illegal* one, because the application has already left the stage it was moving from. The user's phone flickered and the app says "you can't do that."

ADR-0010 settled the same problem at the other end of the system: consumers dedupe redeliveries on `EventId`. This is the client-facing half.

## Decision

**A client-supplied `Idempotency-Key` header on authenticated `POST`s, backed by a per-user replay cache in Redis.**

- **Every authenticated `POST` honours the header; nothing else does.** Not a per-endpoint opt-in list, which is a list to forget to update. `PUT` and `DELETE` are already idempotent by construction — a full-replace applied twice leaves the same state — so a key would be ceremony.
- **The unauthenticated auth surface is excluded, structurally.** Its responses carry tokens, which have no business in a cache with a 24-hour lifetime, and it has no authenticated caller to scope a key by. The exclusion is a property of the activation rule ("requires authorization"), not a list.
- **Keys are scoped to the caller.** One user can neither collide with, nor probe, another's keys.
- **A key is claimed atomically** with `SET NX`, so two simultaneous requests cannot both proceed. The loser is told the first is still running (`409`), or is given its recorded response once there is one.
- **The record carries a fingerprint of the request** — method, path, body. A key presented for a *different* request is refused (`422`) rather than answered with the first request's result, because silently swallowing a second, genuinely different operation looks exactly like data loss.
- **The disposition after execution depends on the outcome**, and this is the part worth stating precisely:
  - **2xx** — recorded, and replayed verbatim (status, body, `Location`) to any later request under that key.
  - **4xx** — the key is *released*. Nothing changed, so the caller must be free to correct the body and send it again under the same key.
  - **5xx or an unhandled exception** — neither. After a server error nobody can say whether the write committed, so the reservation is left to lapse: a retry inside the in-flight window is refused, and after it, the request runs again. This is why that window is a minute rather than a day.
- **If Redis is unreachable, the request fails** (`503`) rather than running without its guarantee. A caller who asked for exactly-once and silently got best-effort has no way to discover it.

## Consequences

- Duplicate submissions stop being a client-correctness problem and become a server guarantee, which is what makes the offline-tolerant retry loop in the mobile client (Sprint 16) buildable at all.
- Redis moves from "nice to have" to a **write-path dependency** for keyed requests. It already holds the Data Protection key ring, so it was on the critical path for other reasons; the failure mode is now more visible, and the health check that already exists is what catches it.
- The replay cache holds response bodies. Those bodies are the user's own data, so the cache inherits the same handling as the database: per-user keys, a short lifetime, and nothing from the auth surface.
- **A known gap this deliberately does not close.** Refresh-token rotation treats a replayed token as compromise and revokes the whole family, so a retried refresh logs the device out — the very scenario this ADR exists for, on the one endpoint it excludes. The fix is a grace window in the token model (a token replayed within N seconds of its rotation returns the successor it already minted), not a replay cache that would hold token pairs. Tracked for Sprint 15.

## Alternatives considered

- **A Postgres table instead of Redis** — rejected. The records expire, belong to no module's schema, and losing one costs a duplicate rather than a fact. That is cache-shaped, and putting it in a module's schema would make one module own a concern of the whole API.
- **Per-endpoint natural keys** (dedupe a create on its own fields) — rejected. It works for creates and not at all for a transition, and it makes every endpoint re-derive the rule. One uniform key is one decision instead of one per endpoint.
- **Explicit opt-in per endpoint** — rejected for the reason above: replay is safe wherever the record is user-scoped and fingerprinted, so the marker would add a maintenance obligation without adding safety.
- **Proceeding without the cache when Redis is down** — rejected. It converts a loud dependency failure into a silent guarantee failure, and duplicates are precisely what the caller was trying to avoid.
- **Recording 5xx responses for replay** — rejected. It would pin a transient failure to the key and leave the client no way to ever retry the operation.
