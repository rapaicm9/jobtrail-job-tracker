# 0006 — Reminder scheduling and delivery

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-07-31 — the scheduler is Quartz.NET rather than Hangfire; there is no trigger per reminder, only a sweep; the reminder times, the late cutoff and the past-instant rule are settled; push and its transport are deferred to the mobile-client sprints. See *Revision history*.

## Context

Reminders are a core feature and the main reason a separate worker process exists: interview reminders, application-deadline and offer-decision reminders, and (Pro) automated follow-ups. They are time-driven, occasionally bursty, and depend on an external push provider — the one external runtime dependency in an otherwise self-contained, manual-entry app. A missed reminder is a real user harm (a missed interview), so delivery must be reliable and correct across time zones.

## Decision

An **event → schedule → deliver** pipeline:

1. **Event, durably.** When a date-bearing thing happens in Applications (`InterviewScheduled`, `ApplicationDeadlineSet`, `OfferDecisionDeadlineSet`), the module writes the event to its **transactional outbox** in the same transaction as the state change; an Infrastructure dispatcher publishes it. Losing one of these would lose a reminder, so these specifically use the outbox rather than in-memory dispatch.
2. **Record.** The Notifications module consumes the event and writes a `Reminder` row carrying the instant it is due. The instant is stored in **UTC, computed from the user's stored IANA time zone**, so "the morning before" means their local morning. Consumption happens in the **API host**, because that is where the outbox dispatcher runs.
3. **Fire.** A **Quartz.NET recurring job in the worker** sweeps for reminders that have come due and delivers them. Quartz runs on a **persistent ADO job store over PostgreSQL** — an in-memory schedule would be lost on a restart, and the sweep is the only thing standing between a due reminder and silence.
4. **Deliver.** In-app delivery is a row the client reads. Push delivery arrives with the mobile client and is **not built in the backend release** — see *What v1 actually delivers*.
5. **Retract.** Cancelling is a state change on the `Reminder` row: `ApplicationStageChanged` and `ApplicationReachedTerminal` retire now-irrelevant reminders, `InterviewCancelled` retires the ones armed for that round, and a deadline event carrying a **null** date says the deadline is gone and its reminders with it.
6. **Automated follow-up (Pro)** is a second Quartz **recurring** job that scans applications sitting in `Applied` past the rule's `N` days with no stage change and raises follow-up reminders. Default `N` is **7 days**, one rule per account in v1.

### One store, not two

The obvious way to use a scheduler is a trigger per reminder, and it is the wrong shape here. It would give this module **two durable records of one fact** — the `Reminder` row and the job store's tables — written in separate transactions, and step 5 is exactly where they diverge, because retracting would mean updating a row *and* unscheduling a trigger with nothing making the pair atomic. The fix for that is a rule ("the row decides, the trigger is only a timer") plus a sweep to catch triggers that went missing — at which point the sweep is doing the real work and the triggers are redundant machinery whose only remaining property is that they can drift.

So Quartz holds **two triggers in total**: the due-reminder sweep and the follow-up scan. A reminder is a row and nothing else. Retraction is a column update, erasure is a delete, and there is no second store to reconcile with because there is no second store.

This also fits where the two processes actually sit. Event consumption happens in the API host; firing happens in the worker. With a trigger per reminder the API would have to schedule into Quartz as well; with a sweep it inserts a row and the worker finds it. One writer, one reader, one table.

What Quartz still earns: a durable recurring schedule that survives restarts, misfire handling for a sweep that should have run while the process was down, and clustering the day a second worker exists — configuration rather than redesign. **Its tables live in the `notifications` schema**, via the configurable table prefix; one module, one schema, and a scheduler used by one module is not an exception to it.

The cost is that firing precision is bounded by the sweep interval. At the granularity these reminders work in — the morning before, an hour before — a minute of lateness is invisible.

### When each reminder fires

| Reminder | Fires |
| --- | --- |
| Interview | the **morning before**, and **an hour before** |
| Application deadline | **three days before** and the **morning of** |
| Offer decision | **three days before**, **the day before**, and the **morning of** |
| Follow-up *(Pro)* | `N` days after `Applied` with no stage change, `N` default 7 |

"Morning" is **11:00 in the user's own timezone** — late enough not to be missed overnight, early enough to act on. Every clock-based reminder uses that one time; the relative ones ("an hour before") need no clock of their own.

**A reminder whose instant has already passed is never created.** Book an interview for two hours from now and the morning-before moment is long gone; recording it would only give the sweep something to fire immediately, which reads as a bug rather than a courtesy. The same rule quietly handles a deadline set for tomorrow: the three-days-before reminder is not created, the morning-of one is.

### Late is not delivered

A reminder found more than **10 minutes** past its instant is **dropped and logged, never sent**. Being told about something that has already happened is worse than silence: it is noise that teaches the user to ignore the app, and it cannot change what they do.

The tolerance is not zero, and cannot be — the sweep discovers every reminder slightly after its instant, so a zero cutoff would drop all of them and the module would appear to do nothing at all. Ten minutes is normal sweep jitter with headroom, and it means a worker that was down does not deliver its backlog on the way up.

### What v1 actually delivers

**In-app only, written directly by the sweep.** The in-app feed is a table the client lists, dismisses from, and takes an unread count off; nothing expires on its own in v1.

**Push is deferred to the mobile-client sprints**, and with it the Redis Stream that carries deliveries, the device-token registration surface, and the whole fragile-provider apparatus: client-side rate limiting, retry with backoff and jitter, a circuit breaker parking messages in a delayed stream, and a dead-letter stream with alerting. The reason is not that push is unimportant — it ships in v1 — but that **none of that policy can be designed against a fake.** What to retry and what to abandon is a function of the provider's error taxonomy: an unregistered token is permanent and should deregister the device, an unavailable backend is transient and should back off, a quota rejection is what the token bucket exists for. A fake `IPushClient` expresses none of those, so building the hardening now means building it against imagined failures.

`IPushClient` is therefore introduced as a seam with a fake behind it, and the transport stays **Redis Streams** as decided — the choice is not reopened, only its construction is sequenced. In-app delivery wants no queue at all: it is an insert, with no external call and no failure mode worth queueing.

**Handlers are idempotent** regardless of channel — at-least-once means a reminder delivered twice must be a cheap no-op, guarded by a unique key on the delivery log. That is a property of the pipeline, not of the provider, so it is built now.

## Consequences

- Reminders are reliable and time-zone-correct, and the trace can span event → schedule → deliver (the W3C `traceparent` is carried on the queue message).
- The worker has real, ongoing work and is the natural first extraction candidate if reminder volume ever demands independent scaling.
- Idempotency and the outbox are non-negotiable; they are the cost of at-least-once delivery.
- **The scheduler brings a second set of tables and a second thing to operate**, and that is the price of not hand-rolling one. It buys a tested recurring schedule, misfire handling and clustering that is available the day a second worker exists. What it does not buy is a dashboard — Hangfire's is the one concrete thing given up by preferring Apache 2.0 (ADR 0002), and the replacement is the `reminders` table itself plus the metrics and alerting the hardening sprint brings.
- **Quartz's clustering stays off** while there is one worker. It is opt-in, and turning it on later is configuration rather than redesign — which is a large part of why a real scheduler was taken rather than a hand-rolled loop.
- **The reminders table is the whole state of the feature**, which makes it easy to reason about and easy to observe: "what is pending" and "what fired" are queries, not an inspection of a scheduler's internals.
- **Firing is only as precise as the sweep**, and any future reminder type that needs to-the-second accuracy would have to argue for a trigger of its own — and inherit the two-store problem this design avoided.
- **The backend release delivers in-app reminders only.** If the mobile client slips, v1 ships with a working reminder feed rather than dead push code; the web client is a real consumer of it. That is the deliberate upside of sequencing push last.
- **The push hardening is deferred, not descoped**, and lands with the adapter that makes it designable. Anything scheduled against it — dead-letter alerting, stream-depth metrics — moves with it.

## Alternatives considered

- **In-process/in-memory scheduling** — rejected; a process restart would drop pending reminders. This is also what rules out the minimal schedulers (NCronJob, Coravel): they are pleasant and they do not persist.
- **A cron-style periodic scan for everything** (no per-reminder scheduling) — rejected for precise, user-facing times like interviews; the recurring scan is kept only where it fits (follow-ups).
- **Computing fire times in local time at delivery** — rejected; storing UTC computed from the IANA zone is the correct, DST-safe approach.
- **Hangfire** — the original choice here, replaced 2026-07-31 before either was added. Licensing decided it: Hangfire's core and its PostgreSQL storage are LGPL v3, against a stack that is otherwise MIT/first-party (ADR 0002). Capability-wise the two are close enough that this project would not have noticed the difference; Hangfire's dashboard is the one real loss.
- **A Quartz trigger per reminder** — the obvious way to use a scheduler, and rejected; see *One store, not two*. It creates a second durable record of every reminder, and the discipline needed to keep the two in step is only worth accepting if the scheduler is doing something the sweep cannot.
- **No scheduler at all — a hand-rolled poll of the `Reminder` table**, claiming with `SKIP LOCKED` exactly as the outbox dispatcher already does. Very close to what was chosen, and the difference is narrow: the same sweep, hosted by Quartz rather than by hand. Quartz was preferred for the durable recurring schedule, the misfire handling when the worker was down, and clustering the day a second worker exists — three things that would otherwise be written and tested here. Had those not been wanted, this would have been the answer, and it remains a small step away.
- **Building the Redis Stream delivery path in the backend release** — deferred to the mobile-client sprints instead. The stream carries push, push needs a device, and no device exists until the mobile client does; meanwhile the retry and abandonment policy it would carry cannot be designed against a fake provider. The transport choice is unchanged, only when it is built.

## Revision history

- **2026-07-16 — original.** The event → schedule → deliver pipeline, UTC fire-at instants computed from the user's IANA zone, cancellation on response, and the fragile-provider posture around delivery.
- **2026-07-31 — the scheduler is Quartz.NET, and the reminder row outranks it.** The original named Hangfire; ADR 0002's instruction to re-verify a licence before first adding it came due, and Hangfire is LGPL v3 against a stack that is otherwise MIT/first-party. Quartz.NET is Apache 2.0 and needs no exception. Deciding it forced the more useful question out into the open: any scheduler means two durable records of one fact, written in separate transactions, and cancellation is where they diverge. Hence the rule added above — **the row decides, the trigger is only a timer** — a firing job re-reads its reminder and no-ops unless it is still `Pending`, and a sweep for overdue `Pending` rows catches triggers that were lost. Also settled: the job store is persistent and its tables live in the `notifications` schema, clustering stays off while there is one worker, and the alternative of no scheduler at all is recorded in full because it was close and is where to return if the row-decides rule proves hard to hold.
- **2026-07-31 — one store, concrete times, and push sequenced last.** Written the same day as the entry above and superseding half of it: settling the scheduler exposed that a trigger per reminder was never the right shape. The previous entry's remedy — *the row decides, the trigger is only a timer*, plus a sweep to catch lost triggers — described a design in which the sweep does the real work and the per-reminder triggers only add something to drift. So the triggers are gone; Quartz holds the sweep and the follow-up scan, a reminder is a row, and retraction is a column update. It also fits the process split, since events are consumed in the API host and reminders fire in the worker.

  Settled alongside it, because none of it was written down and all of it blocks a first implementation: **the morning nudge is 11:00 in the user's own timezone** and every clock-based reminder uses it; application deadlines fire three days before and the morning of, offer decisions add the day before; the follow-up default is **7 days**, one rule per account. **A reminder whose instant has already passed is never created**, and one found more than **10 minutes** late is dropped rather than sent — the tolerance cannot be zero, because the sweep discovers everything slightly late and a zero cutoff would silently drop the lot.

  And **push is deferred to the mobile-client sprints** with its Redis Stream, its device-token registration and its whole fragile-provider apparatus. Not descoped — push ships in v1 — but the retry-and-abandon policy is a function of the provider's error taxonomy, and a fake `IPushClient` expresses none of it. The backend release delivers in-app only, written straight from the sweep, which also means a slipping mobile client leaves a working feature rather than dead code.
