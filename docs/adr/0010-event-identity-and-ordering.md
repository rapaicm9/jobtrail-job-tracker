# 0010 — Event identity and ordering

- **Status:** Accepted
- **Date:** 2026-07-25

## Context

ADR-0009 put durable delivery behind a transactional outbox and stated the guarantee it buys: at-least-once, with idempotent consumers. Publishing the Applications module's full set of events makes two things concrete that were left implicit there, and both are cheaper to settle before the first consumer is written than after.

**Idempotency needs a key, and there wasn't one.** A handler can be handed the same event twice — the dispatcher runs handlers and then commits, so a crash in between replays the row. "Handlers must be idempotent" is an instruction with nothing to act on unless the handler can recognize the repeat. Some events have a natural key (an interview's scheduled instant), but a stage change has none: the same application can legitimately move `Applied → Screening` twice, days apart, with a reopen in between.

**Delivery is not ordered, and the shape of the data hid that.** Rows are claimed in `(occurred_at, id)` order, and `occurred_at` defaults to `now()`, which in PostgreSQL is *transaction start* — so events written in one `SaveChanges` are exactly tied and fall back to their generated ids. A move that closes an application writes two rows this way. Worse across transactions: a long transaction can commit after a short one that started later, so its event surfaces to the dispatcher with an earlier `occurred_at` and is delivered second. Arrival order is not the order things happened.

## Decision

**Every outbox event carries its own identity, and delivery order is explicitly not a guarantee consumers may use.**

- `IOutboxEvent` requires a `Guid EventId`, minted where the event is recorded. It identifies the *occurrence*, not the row: it travels in the payload, so it survives these events ever reaching consumers by another route. The outbox row keeps its own primary key for delivery bookkeeping; the two are deliberately separate concerns.
- **Consumers dedupe on `EventId`.** That is the contract a redelivery is measured against, and it is why the field is on the interface rather than left to each event to remember.
- **Events state whole facts, not deltas.** A stage change carries both ends of the move, not just the new stage, so it can be applied without having seen the event before it. This is what makes unordered delivery survivable rather than merely tolerated.
- **Consumers must be commutative.** Where a consumer keeps per-aggregate state, it applies last-write-wins on the event's own `OccurredAt` — the domain instant the publisher stamped — rather than on arrival order. Counters key off `EventId` instead of counting arrivals.
- Two events recorded in one transaction (the stage change and the terminal it reached) share an `OccurredAt` because they describe one instant. They are independent facts about it, so either order leaves the same result.

## Consequences

- Consumers need somewhere to record what they have already applied. For Analytics that is a small table keyed by `EventId`; for Notifications the delivery log it already owes for at-least-once push.
- The ordering rule is a real constraint on the projections in Sprint 9 and the reminder logic in Sprint 10: neither may be written as "apply the events in the order they arrive."
- `OccurredAt` becomes load-bearing rather than decorative, which makes it a clock-skew dependency. Exact on one host; NTP-accurate across several, against gaps between human actions measured in seconds. Acceptable, and worth revisiting only if these events are ever published from more than one process.
- The payload grows by one id per event. Nothing measurable against a row that already carries a JSON document.

## Alternatives considered

- **Natural keys per event** — rejected. It works for some events and not others, it makes every consumer re-derive the rule, and the derivation changes whenever an event gains a field. A uniform key is one decision instead of one per event type.
- **Passing the outbox row id to handlers** — rejected. It would put the dedup key in an envelope belonging to this delivery mechanism, so a consumer's idempotency would quietly depend on how the event reached it, and it would change the handler signature the in-process bus shares.
- **Chasing true commit ordering** — rejected. A `bigserial` does not provide it: the value is assigned at insert, before commit, so it inverts exactly as `now()` does. Real commit order means logical decoding, which would replace the outbox rather than repair it, at a scale this product will not reach.
- **Delivering strictly in order, one at a time** — rejected. It converts any slow or failing handler into a total stall, trading a constraint consumers can absorb for an availability risk they cannot.
