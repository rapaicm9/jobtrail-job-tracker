# 0012 — Durable erasure

- **Status:** Accepted
- **Date:** 2026-07-27

## Context

`DELETE /account` answers `204` and erases nothing itself. The deletions happen in handlers — one per module, each in its own schema — off a `UserDataDeletionRequested` event. That shape is right: no module can read another's tables, so fanning out an announcement is the only way the other four ever learn.

The delivery underneath it was not. The event went onto the in-process bus, which by its own contract queues in memory, retries nothing, and drops whatever is outstanding when the host stops. Two failures follow, and the second is the serious one:

- A handler throws, is logged, and is dropped. That module's data survives while the others are erased.
- The host stops between the response and the dispatch. Then **nothing is erased at all — including Identity's own rows.** The account, its sessions, its applications and its notes are all still there, and no record survives that anyone ever asked.

In both cases the user has already been told the erasure was done. A `204` is a promise, and this is the one request in the system where quietly failing to keep it is the whole harm rather than an inconvenience: there is no follow-up screen where the user finds out, and no state they can inspect to discover it.

ADR-0009 built a transactional outbox for exactly this class of event — one whose loss costs a consumer a fact it can never recover — and gave it to the Applications module. This applies the same mechanism to the one event Identity publishes that has the same property.

## Decision

**The erasure request is recorded in an outbox in the `identity` schema, in the same transaction that accepts it, and delivered until every handler has succeeded.**

- **The recorded row *is* the state change.** Everywhere else the outbox carries the announcement of a write happening beside it. An erasure request changes nothing on its own, so committing the row is what makes the request durable — and is what the `204` is a promise about. `DELETE /account` writes it and answers; nothing else happens in the request.
- **Identity gains a dispatcher over its own store**, alongside the one Applications already runs. Two dispatchers, two schemas, one mechanism; `SKIP LOCKED` means neither cares how many hosts are running.
- **Handlers are unchanged and unaware.** They were already registered as event handlers and already required to be idempotent, because the in-process bus said so and this makes it real. At-least-once delivery over idempotent handlers is precisely the shape erasure wants: a redelivery finds nothing left to do.
- **Identity's own handler is a set-based delete rather than a `UserManager` call.** Two reasons that only bite once delivery is durable. `UserManager.DeleteAsync` reports failure by returning a result rather than throwing, so a failed erasure would be indistinguishable from a successful one and the event would be marked delivered — durable delivery of a guarantee that was not kept. And it saves through the same `DbContext` the dispatcher is using, so a failed delete stays pending on the change tracker for the dispatcher's own save to commit by accident. One statement has neither problem, and matches how the other modules erase.
- **The erasure record is itself exempt from erasure.** Every other table carrying the user goes; this row does not. It is the record that the request was made and served, and — decisively — it is the row the dispatcher is holding under lock while the handlers run. A handler deleting it would leave the dispatcher unable to mark it processed, rolling back the batch and its own deletion with it, and the request would be claimed again to the same end. It leaves on the outbox's normal pruning, like every other processed row.

## Consequences

- The guarantee changes from *"the fan-out was queued"* to *"if the request was accepted, the handlers eventually ran."* That is the guarantee the `204` was already making.
- Erasure spans several transactions — Identity's inside the dispatcher's, each other module's its own — so between retries a user can be erased from some modules and not others. Redelivery closes it, and the only observable intermediate state is data that is on its way out.
- **A handler that shares the dispatcher's `DbContext` cannot open its own transaction, and the dispatcher commits whether or not delivery succeeded.** Such a handler must therefore be a single atomic statement. Identity's is; the other modules' handlers use their own contexts and are unaffected. Worth knowing before adding the second handler to this schema.
- A processed erasure record outlives the account by the retention window, holding one account identifier and no other personal data. It is a record of a request being honoured, which is the kind of thing worth being able to answer questions about.
- The account remains usable between the `204` and the delivery — unchanged from before, and now bounded by a dispatcher that will certainly get there rather than one that might not. Locking the account at the moment of request is a separate decision and is deliberately not taken here.

## Alternatives considered

- **Erasing synchronously inside the request** — rejected. It couples the user's response to four modules being healthy, turns any one of their failures into a failed erasure request, and makes the endpoint's latency the sum of everything the account touches. The fan-out exists to avoid exactly this.
- **Keeping the in-process bus and adding retries to it** — rejected. The queue is in memory; retrying harder does nothing about the host that stops holding it. The gap is durability, not persistence of effort.
- **A retry queue in Redis** — rejected. It would survive a restart, but not the window between the request committing and the enqueue succeeding — which is the same commit-then-publish gap, moved. Only a row written in the accepting transaction closes it.
- **Deleting the erasure record as part of the erasure** — rejected, and it is the tempting mistake: every other table carrying the user is emptied, so this one looks like an oversight. It would strand the delivery that is carrying it out.
