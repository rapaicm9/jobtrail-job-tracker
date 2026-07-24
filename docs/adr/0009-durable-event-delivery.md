# 0009 — Durable event delivery

- **Status:** Accepted
- **Date:** 2026-07-24

## Context

Modules talk to each other through integration events, dispatched in process over a channel (ADR-0001). That is the right cost for a reaction that can be rebuilt: if an analytics projection misses an event, the projection can be replayed from the events it did see, or recomputed.

It is the wrong cost for the rest. A consumer cannot read the publishing module's tables — that is the boundary the whole architecture rests on — so an event it never receives is a fact it can never learn. Notifications finding out about an interview is the sharp case: miss it and the reminder simply never fires, and nobody discovers this until the user misses the interview.

In-process dispatch has two gaps. The event is published *after* the transaction commits, so a crash in between loses it silently. And the queue is in memory, so anything still queued dies with the host.

## Decision

**A transactional outbox for events whose loss has consequences; in-process dispatch for the rest.**

- The publishing module owns an `outbox` table **in its own schema**, written through its own `DbContext`. The row is added in the **same `SaveChanges` as the state change**, so the fact and the announcement of it commit together or not at all. This is the entire mechanism; everything else is delivery.
- A dispatcher in Infrastructure polls, claims a batch with **`SELECT … FOR UPDATE SKIP LOCKED`**, and delivers. Two dispatchers — two API instances, or an API and a worker — divide the work rather than both delivering everything.
- **The dispatcher runs the handlers itself and marks the row processed only when they have all succeeded.** A handler that throws leaves the row owed, to be retried with an exponential backoff. This is what makes the guarantee *"if the state changed, the handlers eventually ran"* rather than merely *"the event was recorded"*.
- Delivery is therefore **at-least-once, and handlers must be idempotent** — an obligation the in-process bus already stated and this makes real.
- After a bounded number of failures a row is left alone: still unprocessed, with its error and attempt count. An event nobody can deliver must stay visible for a human rather than disappear.
- Events are registered under an **explicit, stable name**, not the CLR type name, so renaming a record cannot orphan rows already written. Registration captures the event type statically, so there is no reflection on the delivery path.
- **Which path an event takes is invisible to consumers.** A handler is registered the same way either way; only the publisher decides whether a miss is affordable.

## Consequences

- The write path costs one extra row per event, in a transaction that was happening anyway. No distributed transaction, no broker.
- Delivery is *eventual*: an event is handled a poll interval after it is recorded, not synchronously. Nothing in the product needs it sooner.
- A slow or failing handler delays the events behind it in its batch, since delivery is sequential and the row stays claimed. Acceptable while handlers are local and fast; a genuinely slow consumer belongs behind the worker queue instead.
- The outbox table is operational data with a lifecycle: processed rows are pruned after a retention window, unprocessed ones are a signal. "Rows owed for too long" is a monitoring target when alerting is set up.
- Publishing modules acquire a dependency on the dispatcher being *running somewhere*. With `SKIP LOCKED` it does not matter which host, which is what makes moving it to the worker later a non-event.

## Alternatives considered

- **In-process dispatch for everything** — rejected. It cannot survive a crash between commit and publish, and a lost reminder is invisible until the user misses an interview.
- **Publishing inside the transaction, synchronously** — rejected. It makes the publisher wait on its consumers and lets a consumer's failure roll back the user's write, which is precisely the coupling events exist to avoid.
- **A message broker (RabbitMQ, Kafka)** — rejected for v1. It would add an operational component to the deployment footprint without removing the need for an outbox, since the same commit-then-publish gap exists in front of any broker.
- **Marking the row processed as soon as the event is queued** — rejected. It is barely more than in-process dispatch: the crash window moves but does not close, and the retry machinery would have nothing to retry.
