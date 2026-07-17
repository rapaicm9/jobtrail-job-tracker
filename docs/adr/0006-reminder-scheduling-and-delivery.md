# 0006 — Reminder scheduling and delivery

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

Reminders are a core feature and the main reason a separate worker process exists: interview reminders, application-deadline and offer-decision reminders, and (Pro) automated follow-ups. They are time-driven, occasionally bursty, and depend on an external push provider — the one external runtime dependency in an otherwise self-contained, manual-entry app. A missed reminder is a real user harm (a missed interview), so delivery must be reliable and correct across time zones.

## Decision

An **event → schedule → deliver** pipeline:

1. **Event, durably.** When a date-bearing thing happens in Applications (`InterviewScheduled`, `ApplicationDeadlineSet`, `OfferDecisionDeadlineSet`), the module writes the event to its **transactional outbox** in the same transaction as the state change; an Infrastructure dispatcher publishes it. Losing one of these would lose a reminder, so these specifically use the outbox rather than in-memory dispatch.
2. **Schedule.** The Notifications module consumes the event and schedules a **Hangfire** job at the fire-at instant. The instant is stored in **UTC, computed from the user's stored IANA time zone**, so "the day before at 9am" means the user's local 9am.
3. **Deliver.** When the job fires it enqueues a delivery message on a **Redis Stream**; a worker consumer sends it through `IPushClient` and writes a delivery-log row.
4. **Cancel on response.** `ApplicationStageChanged` cancels pending, now-irrelevant reminders for that application (notably follow-ups once the application has moved).
5. **Automated follow-up (Pro)** is a Hangfire **recurring** job that scans applications sitting in `Applied` past the rule's `N` days with no stage change and enqueues follow-ups. This is the steady background load that justifies the worker.

Delivery treats the push provider as fragile: client-side rate limiting (Redis token bucket, global across workers), retry with backoff + jitter, a circuit breaker that parks messages in a delayed stream when open, and a dead-letter stream with alerting. Handlers are **idempotent** — at-least-once delivery means a reminder firing twice must be a cheap no-op (guarded by the delivery-log key). Channels in v1: in-app + push; email is deferred.

## Consequences

- Reminders are reliable and time-zone-correct, and the trace can span event → schedule → deliver (the W3C `traceparent` is carried on the queue message).
- The worker has real, ongoing work and is the natural first extraction candidate if reminder volume ever demands independent scaling.
- Idempotency and the outbox are non-negotiable; they are the cost of at-least-once delivery.

## Alternatives considered

- **In-process/in-memory scheduling** — rejected; a process restart would drop pending reminders.
- **A cron-style periodic scan for everything** (no per-reminder scheduling) — rejected for precise, user-facing times like interviews; the recurring scan is kept only where it fits (follow-ups).
- **Computing fire times in local time at delivery** — rejected; storing UTC computed from the IANA zone is the correct, DST-safe approach.
