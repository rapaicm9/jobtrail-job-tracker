# 0006 — Reminder scheduling and delivery

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-07-31 — the scheduler is Quartz.NET rather than Hangfire, and the reminder row is the source of truth the trigger must defer to. See *Revision history*.

## Context

Reminders are a core feature and the main reason a separate worker process exists: interview reminders, application-deadline and offer-decision reminders, and (Pro) automated follow-ups. They are time-driven, occasionally bursty, and depend on an external push provider — the one external runtime dependency in an otherwise self-contained, manual-entry app. A missed reminder is a real user harm (a missed interview), so delivery must be reliable and correct across time zones.

## Decision

An **event → schedule → deliver** pipeline:

1. **Event, durably.** When a date-bearing thing happens in Applications (`InterviewScheduled`, `ApplicationDeadlineSet`, `OfferDecisionDeadlineSet`), the module writes the event to its **transactional outbox** in the same transaction as the state change; an Infrastructure dispatcher publishes it. Losing one of these would lose a reminder, so these specifically use the outbox rather than in-memory dispatch.
2. **Schedule.** The Notifications module consumes the event, writes a `Reminder` row, and registers a **Quartz.NET** trigger at the fire-at instant. The instant is stored in **UTC, computed from the user's stored IANA time zone**, so "the day before at 9am" means the user's local 9am. Quartz runs on a **persistent ADO job store over PostgreSQL** — an in-memory store would drop everything pending on a restart, which is the failure this whole pipeline exists to prevent.
3. **Deliver.** When the job fires it enqueues a delivery message on a **Redis Stream**; a worker consumer sends it through `IPushClient` and writes a delivery-log row.
4. **Cancel on response.** `ApplicationStageChanged` cancels pending, now-irrelevant reminders for that application (notably follow-ups once the application has moved).
5. **Automated follow-up (Pro)** is a Quartz **recurring** job that scans applications sitting in `Applied` past the rule's `N` days with no stage change and enqueues follow-ups. This is the steady background load that justifies the worker.

### The reminder row is the truth; the trigger is only a timer

Using a scheduler means this module keeps **two durable records of one fact**: the `Reminder` row and the job store's own tables. They are written in separate transactions and can therefore disagree — and step 4 is exactly where they will, because cancelling means updating a row *and* unscheduling a trigger, with no shared transaction to make the pair atomic.

So the rule is one-directional: **the row decides, and the trigger never does.** A firing job re-reads its `Reminder` and does nothing unless it is still `Pending`; unscheduling is a best-effort tidy-up rather than the mechanism by which cancellation works. A trigger that survives its cancelled row then costs one wasted wake-up, and a row whose trigger was lost is caught by a sweep for overdue `Pending` reminders. Both failures are recoverable; the reverse arrangement — trusting the scheduler and treating the row as a log — has failure modes that silently send a reminder the user was told was cancelled.

That sweep is worth building anyway, because it is also the answer to a job store that has been restored from a backup, or a fire-at instant that passed while the worker was down.

**Quartz's tables live in the `notifications` schema**, addressed through its configurable table prefix. One module, one schema is the rule the whole system is built on, and a scheduler used by exactly one module is not an exception to it.

Delivery treats the push provider as fragile: client-side rate limiting (Redis token bucket, global across workers), retry with backoff + jitter, a circuit breaker that parks messages in a delayed stream when open, and a dead-letter stream with alerting. Handlers are **idempotent** — at-least-once delivery means a reminder firing twice must be a cheap no-op (guarded by the delivery-log key). Channels in v1: in-app + push; email is deferred.

## Consequences

- Reminders are reliable and time-zone-correct, and the trace can span event → schedule → deliver (the W3C `traceparent` is carried on the queue message).
- The worker has real, ongoing work and is the natural first extraction candidate if reminder volume ever demands independent scaling.
- Idempotency and the outbox are non-negotiable; they are the cost of at-least-once delivery.
- **The scheduler brings a second set of tables and a second thing to operate**, and that is the price of not hand-rolling one. It buys a tested trigger engine, misfire handling and clustering that is available the day a second worker exists. What it does not buy is a dashboard — Hangfire's is the one concrete thing given up by preferring Apache 2.0 (ADR 0002), and the replacement is the `reminders` table itself plus the metrics and alerting Sprint 11 brings.
- **Quartz's clustering stays off** while there is one worker. It is opt-in, and turning it on later is configuration rather than redesign — which is a large part of why a real scheduler was taken rather than a poller.
- **Two stores of one fact is a standing hazard, not a one-off.** Every future reminder type has to honour the row-decides rule, and a sweep for overdue `Pending` rows is permanent infrastructure rather than a stopgap.

## Alternatives considered

- **In-process/in-memory scheduling** — rejected; a process restart would drop pending reminders. This is also what rules out the minimal schedulers (NCronJob, Coravel): they are pleasant and they do not persist.
- **A cron-style periodic scan for everything** (no per-reminder scheduling) — rejected for precise, user-facing times like interviews; the recurring scan is kept only where it fits (follow-ups).
- **Computing fire times in local time at delivery** — rejected; storing UTC computed from the IANA zone is the correct, DST-safe approach.
- **Hangfire** — the original choice here, replaced 2026-07-31 before either was added. Licensing decided it: Hangfire's core and its PostgreSQL storage are LGPL v3, against a stack that is otherwise MIT/first-party (ADR 0002). Capability-wise the two are close enough that this project would not have noticed the difference; Hangfire's dashboard is the one real loss.
- **No scheduler at all — poll the `Reminder` table** for rows whose fire-at has passed, claiming with `SKIP LOCKED`, exactly as the outbox dispatcher already does. Genuinely close, and rejected on judgement rather than on a defect. In its favour: it needs no dependency, it collapses the two-stores problem entirely because the row would be the only record, and cancellation becomes an ordinary column update that the poller simply never picks up. Against it: it is scheduling infrastructure written by hand — misfire semantics, claim expiry, and eventually clustering — where a maintained library already has all three, and the poll interval becomes a floor on precision that has to be argued about. The deciding consideration was that a real scheduler is a better place to be standing if reminder types multiply, and that the two-stores hazard is manageable by a rule that fits in a paragraph. **If that rule ever proves hard to hold, this is the alternative to come back to**, and the sweep for overdue `Pending` rows is most of it already built.

## Revision history

- **2026-07-16 — original.** The event → schedule → deliver pipeline, UTC fire-at instants computed from the user's IANA zone, cancellation on response, and the fragile-provider posture around delivery.
- **2026-07-31 — the scheduler is Quartz.NET, and the reminder row outranks it.** The original named Hangfire; ADR 0002's instruction to re-verify a licence before first adding it came due, and Hangfire is LGPL v3 against a stack that is otherwise MIT/first-party. Quartz.NET is Apache 2.0 and needs no exception. Deciding it forced the more useful question out into the open: any scheduler means two durable records of one fact, written in separate transactions, and cancellation is where they diverge. Hence the rule added above — **the row decides, the trigger is only a timer** — a firing job re-reads its reminder and no-ops unless it is still `Pending`, and a sweep for overdue `Pending` rows catches triggers that were lost. Also settled: the job store is persistent and its tables live in the `notifications` schema, clustering stays off while there is one worker, and the alternative of no scheduler at all is recorded in full because it was close and is where to return if the row-decides rule proves hard to hold.
