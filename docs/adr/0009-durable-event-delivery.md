# 0009 — Durable event delivery

- **Status:** Accepted
- **Date:** 2026-07-24
- **Amended:** 2026-07-25 (event identity and ordering), 2026-07-27 (erasure). See *Revision history*.

## Context

Modules talk to each other through integration events, dispatched in process over a channel (ADR-0001). That is the right cost for a reaction that can be rebuilt: if an analytics projection misses an event, the projection can be replayed from the events it did see, or recomputed.

It is the wrong cost for the rest. A consumer cannot read the publishing module's tables — that is the boundary the whole architecture rests on — so an event it never receives is a fact it can never learn. Notifications finding out about an interview is the sharp case: miss it and the reminder simply never fires, and nobody discovers this until the user misses the interview.

In-process dispatch has two gaps. The event is published *after* the transaction commits, so a crash in between loses it silently. And the queue is in memory, so anything still queued dies with the host.

Two consequences of choosing at-least-once delivery had to be settled before the first consumer was written, and are recorded here rather than left implicit:

**Idempotency needs a key, and there wasn't one.** A handler can be handed the same event twice — the dispatcher runs handlers and then commits, so a crash in between replays the row. "Handlers must be idempotent" is an instruction with nothing to act on unless the handler can recognize the repeat. Some events have a natural key (an interview's scheduled instant), but a stage change has none: the same application can legitimately move `Applied → Screening` twice, days apart, with a reopen in between.

**Delivery is not ordered, and the shape of the data hid that.** Rows are claimed in `(occurred_at, id)` order, and `occurred_at` defaults to `now()`, which in PostgreSQL is *transaction start* — so events written in one `SaveChanges` are exactly tied and fall back to their generated ids. A move that closes an application writes two rows this way. Worse across transactions: a long transaction can commit after a short one that started later, so its event surfaces to the dispatcher with an earlier `occurred_at` and is delivered second. Arrival order is not the order things happened.

## Decision

### The outbox

**A transactional outbox for events whose loss has consequences; in-process dispatch for the rest.**

- The publishing module owns an `outbox` table **in its own schema**, written through its own `DbContext`. The row is added in the **same `SaveChanges` as the state change**, so the fact and the announcement of it commit together or not at all. This is the entire mechanism; everything else is delivery.
- A dispatcher in Infrastructure polls, claims a batch with **`SELECT … FOR UPDATE SKIP LOCKED`**, and delivers. Two dispatchers — two API instances, or an API and a worker — divide the work rather than both delivering everything.
- **The dispatcher runs the handlers itself and marks the row processed only when they have all succeeded.** A handler that throws leaves the row owed, to be retried with an exponential backoff. This is what makes the guarantee *"if the state changed, the handlers eventually ran"* rather than merely *"the event was recorded"*.
- Delivery is therefore **at-least-once, and handlers must be idempotent** — an obligation the in-process bus already stated and this makes real.
- After a bounded number of failures a row is left alone: still unprocessed, with its error and attempt count. An event nobody can deliver must stay visible for a human rather than disappear.
- Events are registered under an **explicit, stable name**, not the CLR type name, so renaming a record cannot orphan rows already written. Registration captures the event type statically, so there is no reflection on the delivery path.
- **Which path an event takes is invisible to consumers.** A handler is registered the same way either way; only the publisher decides whether a miss is affordable.

### Event identity and ordering

**Every outbox event carries its own identity, and delivery order is explicitly not a guarantee consumers may use.**

- `IOutboxEvent` requires a `Guid EventId`, minted where the event is recorded. It identifies the *occurrence*, not the row: it travels in the payload, so it survives these events ever reaching consumers by another route. The outbox row keeps its own primary key for delivery bookkeeping; the two are deliberately separate concerns.
- **Consumers dedupe on `EventId`.** That is the contract a redelivery is measured against, and it is why the field is on the interface rather than left to each event to remember.
- **Events state whole facts, not deltas.** A stage change carries both ends of the move, not just the new stage, so it can be applied without having seen the event before it. This is what makes unordered delivery survivable rather than merely tolerated.
- **Consumers must be commutative.** Where a consumer keeps per-aggregate state, it applies last-write-wins on the event's own `OccurredAt` — the domain instant the publisher stamped — rather than on arrival order. Counters key off `EventId` instead of counting arrivals.
- Two events recorded in one transaction (the stage change and the terminal it reached) share an `OccurredAt` because they describe one instant. They are independent facts about it, so either order leaves the same result.

### Erasure rides the same mechanism

`DELETE /account` answers `204` and erases nothing itself. The deletions happen in handlers — one per module, each in its own schema — off a `UserDataDeletionRequested` event, because no module can read another's tables and fanning out an announcement is the only way the other four ever learn. That event has exactly the property this ADR is about: its loss costs a consumer a fact it can never recover. Two failures followed from leaving it on the in-process bus, and the second is the serious one:

- A handler throws, is logged, and is dropped. That module's data survives while the others are erased.
- The host stops between the response and the dispatch. Then **nothing is erased at all — including Identity's own rows** — and no record survives that anyone ever asked.

In both cases the user has already been told the erasure was done. A `204` is a promise, and this is the one request in the system where quietly failing to keep it is the whole harm rather than an inconvenience: there is no follow-up screen where the user finds out, and no state they can inspect to discover it. So:

- **The erasure request is recorded in an outbox in the `identity` schema, in the same transaction that accepts it**, and delivered until every handler has succeeded.
- **The recorded row *is* the state change.** Everywhere else the outbox carries the announcement of a write happening beside it. An erasure request changes nothing on its own, so committing the row is what makes the request durable — and is what the `204` is a promise about. `DELETE /account` writes it and answers; nothing else happens in the request.
- **Identity gains a dispatcher over its own store**, alongside the one Applications already runs. Two dispatchers, two schemas, one mechanism; `SKIP LOCKED` means neither cares how many hosts are running.
- **Handlers are unchanged and unaware.** They were already registered as event handlers and already required to be idempotent. At-least-once delivery over idempotent handlers is precisely the shape erasure wants: a redelivery finds nothing left to do.
- **Identity's own handler is a set-based delete rather than a `UserManager` call.** Two reasons that only bite once delivery is durable. `UserManager.DeleteAsync` reports failure by returning a result rather than throwing, so a failed erasure would be indistinguishable from a successful one and the event would be marked delivered — durable delivery of a guarantee that was not kept. And it saves through the same `DbContext` the dispatcher is using, so a failed delete stays pending on the change tracker for the dispatcher's own save to commit by accident. One statement has neither problem, and matches how the other modules erase.
- **The erasure record is itself exempt from erasure.** Every other table carrying the user goes; this row does not. It is the record that the request was made and served, and — decisively — it is the row the dispatcher is holding under lock while the handlers run. A handler deleting it would leave the dispatcher unable to mark it processed, rolling back the batch and its own deletion with it, and the request would be claimed again to the same end. It leaves on the outbox's normal pruning, like every other processed row.

## Consequences

### The outbox

- The write path costs one extra row per event, in a transaction that was happening anyway. No distributed transaction, no broker.
- Delivery is *eventual*: an event is handled a poll interval after it is recorded, not synchronously. Nothing in the product needs it sooner.
- A slow or failing handler delays the events behind it in its batch, since delivery is sequential and the row stays claimed. Acceptable while handlers are local and fast; a genuinely slow consumer belongs behind the worker queue instead.
- The outbox table is operational data with a lifecycle: processed rows are pruned after a retention window, unprocessed ones are a signal. "Rows owed for too long" is a monitoring target when alerting is set up.
- Publishing modules acquire a dependency on the dispatcher being *running somewhere*. With `SKIP LOCKED` it does not matter which host, which is what makes moving it to the worker later a non-event.
- **Handlers must be isolated as well as idempotent.** The dispatcher claims a batch and delivers all of it in one DI scope, whereas the in-process bus scopes per event. A handler that swallows an exception must detach what it added, or the next message's `SaveChanges` re-attempts it and takes that message's work down with it.

### Identity and ordering

- Consumers need somewhere to record what they have already applied. For Analytics that is a small table keyed by `EventId`; for Notifications the delivery log it already owes for at-least-once push.
- The ordering rule is a real constraint on the projections in Sprint 9 and the reminder logic in Sprint 10: neither may be written as "apply the events in the order they arrive."
- `OccurredAt` becomes load-bearing rather than decorative, which makes it a clock-skew dependency. Exact on one host; NTP-accurate across several, against gaps between human actions measured in seconds. Acceptable, and worth revisiting only if these events are ever published from more than one process.
- The payload grows by one id per event. Nothing measurable against a row that already carries a JSON document.

### Erasure

- The guarantee changes from *"the fan-out was queued"* to *"if the request was accepted, the handlers eventually ran."* That is the guarantee the `204` was already making.
- Erasure spans several transactions — Identity's inside the dispatcher's, each other module's its own — so between retries a user can be erased from some modules and not others. Redelivery closes it, and the only observable intermediate state is data that is on its way out.
- **A handler that shares the dispatcher's `DbContext` cannot open its own transaction, and the dispatcher commits whether or not delivery succeeded.** Such a handler must therefore be a single atomic statement. Identity's is; the other modules' handlers use their own contexts and are unaffected. Worth knowing before adding the second handler to this schema.
- A processed erasure record outlives the account by the retention window, holding one account identifier and no other personal data. It is a record of a request being honoured, which is the kind of thing worth being able to answer questions about.
- The account remains usable between the `204` and the delivery — unchanged from before, and now bounded by a dispatcher that will certainly get there rather than one that might not. Locking the account at the moment of request is a separate decision and is deliberately not taken here.

## Alternatives considered

### The outbox

- **In-process dispatch for everything** — rejected. It cannot survive a crash between commit and publish, and a lost reminder is invisible until the user misses an interview.
- **Publishing inside the transaction, synchronously** — rejected. It makes the publisher wait on its consumers and lets a consumer's failure roll back the user's write, which is precisely the coupling events exist to avoid.
- **A message broker (RabbitMQ, Kafka)** — rejected for v1. It would add an operational component to the deployment footprint without removing the need for an outbox, since the same commit-then-publish gap exists in front of any broker.
- **Marking the row processed as soon as the event is queued** — rejected. It is barely more than in-process dispatch: the crash window moves but does not close, and the retry machinery would have nothing to retry.

### Identity and ordering

- **Natural keys per event** — rejected. It works for some events and not others, it makes every consumer re-derive the rule, and the derivation changes whenever an event gains a field. A uniform key is one decision instead of one per event type.
- **Passing the outbox row id to handlers** — rejected. It would put the dedup key in an envelope belonging to this delivery mechanism, so a consumer's idempotency would quietly depend on how the event reached it, and it would change the handler signature the in-process bus shares.
- **Chasing true commit ordering** — rejected. A `bigserial` does not provide it: the value is assigned at insert, before commit, so it inverts exactly as `now()` does. Real commit order means logical decoding, which would replace the outbox rather than repair it, at a scale this product will not reach.
- **Delivering strictly in order, one at a time** — rejected. It converts any slow or failing handler into a total stall, trading a constraint consumers can absorb for an availability risk they cannot.

### Erasure

- **Erasing synchronously inside the request** — rejected. It couples the user's response to four modules being healthy, turns any one of their failures into a failed erasure request, and makes the endpoint's latency the sum of everything the account touches. The fan-out exists to avoid exactly this.
- **Keeping the in-process bus and adding retries to it** — rejected. The queue is in memory; retrying harder does nothing about the host that stops holding it. The gap is durability, not persistence of effort.
- **A retry queue in Redis** — rejected. It would survive a restart, but not the window between the request committing and the enqueue succeeding — which is the same commit-then-publish gap, moved. Only a row written in the accepting transaction closes it.
- **Deleting the erasure record as part of the erasure** — rejected, and it is the tempting mistake: every other table carrying the user is emptied, so this one looks like an oversight. It would strand the delivery that is carrying it out.

## Revision history

- **2026-07-24 — original.** The outbox, the dispatcher, and at-least-once delivery over idempotent handlers, applied to the Applications module.
- **2026-07-25 — event identity and ordering** *(recorded separately at the time as ADR 0010)*. Publishing the Applications module's full event set made two of the original's implicit claims concrete: idempotent consumers need a dedup key that did not exist, and the gap between claim order and the order things actually happened needed stating before any consumer assumed otherwise.
- **2026-07-27 — erasure** *(recorded separately at the time as ADR 0012)*. `UserDataDeletionRequested` had the property this ADR was written for and was still riding the in-process bus, which meant a `204` could be a promise the system silently failed to keep. Identity gained its own outbox and dispatcher; the mechanism did not change.
