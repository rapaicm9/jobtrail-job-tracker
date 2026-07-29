# 0016 — Analytics read model

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

Analytics is a read model in its own schema, built from the events the Applications module publishes. It cannot read that module's tables, so everything it can ever say has to be assembled from what arrives on the stream and kept.

Two properties of the delivery underneath it decide most of the design.

**Delivery is at-least-once and unordered** (ADR 0009). An event may arrive twice, and two events written in the same transaction may arrive in either order. Every projection therefore has to be safe to apply repeatedly, and safe to apply without knowing what came before it.

**The outbox is a delivery buffer, not a log.** A processed row is pruned once it is past its retention window. There is no stream sitting behind the read model waiting to be replayed — whatever Analytics does not keep at the time is gone for good. This is the property that turns "we can add that dimension later and rebuild" from a safe deferral into a decision that cannot be revisited, and it is worth being explicit that the phrase "rebuildable projections" cannot mean replay here.

The figures asked of the module also split in two. Some are account-wide: the pipeline snapshot, total applied, rates, trends. Others are the same figures for one campaign — and a campaign is a whole job search, so an account running more than one is asking how this search is going compared to the last. `ApplicationMovedToCampaign` is already published and unconsumed, precisely so this question stays answerable.

## Decision

**Analytics keeps one row per application and derives every figure by aggregating those rows. It stores no counters.**

### The base row

One table, keyed by application id, holding what the events carry: the owner, the current stage, the campaign, the applied date, the source, the work mode, the terminal outcome once there is one, and the timestamps the time-based metrics need. It is the module's own record, not a copy of another module's table — it holds what was announced, in the shape the metrics want.

- **Every projection is an upsert on the application id.** Redelivery is then idempotent by construction rather than by arithmetic: applying the same `ApplicationStageChanged` twice writes the same row twice and the second write changes nothing. There is no dedup table and no `EventId` ledger, because there is nothing to double.
- **A write carries the event's `OccurredAt`, and an older write does not overwrite a newer fact.** This is what makes unordered delivery survivable rather than merely tolerated. Two moves delivered back to front — `Screening → Interview` arriving before `Applied → Screening` — would otherwise leave the row at `Screening`, permanently, with nothing to signal it. Each event states both ends of its move, so any one of them can be applied alone; the timestamp guard is what stops the wrong one winning.
- **The row carries every dimension the events already offer, including ones no metric uses yet.** A dimension added later can only be populated for applications seen after it was added, because there is no stream to go back to. Carrying source and work mode from the start costs two columns; recovering them later costs the history.

### Campaign

**A column on the base row, not a rollup of its own.** `ApplicationMovedToCampaign` is then a single-column update — it carries both ends of the move, so it applies without knowing the campaign the row currently shows, and it re-applies harmlessly.

Campaign-scoped figures are the same aggregation with one more predicate. The read endpoints take an optional campaign filter, and **it is not gated**: reading is never gated (ADR 0005), and an account with one campaign simply gets the same numbers back.

### Aggregation and rebuild

Figures are computed from the base rows on read, behind the output cache. **Rebuilding means recomputing from the base rows** — not replaying the stream, which is not available.

## Consequences

- **"Rebuildable" is honest, but narrower than it sounds, and this is the sharp edge.** Rollups rebuild from base rows. The base rows cannot be rebuilt from anything: they are the system of record for analytics and need backing up like any other table. Losing them loses the history, and no amount of correct projection code changes that.
- Aggregating on read costs more than incrementing a counter. At the volume one person's job search reaches — hundreds of rows over a year, not millions — a grouped scan is not a cost worth designing against, and the output cache absorbs the repeat views a dashboard generates. If the shape were ever wrong it would be wrong slowly and visibly, which is the failure mode to prefer.
- Adding a metric usually costs a query rather than a migration and a backfill, because the facts are already in the rows. That is the main return on storing rows instead of totals.
- Erasure is one delete by owner, which is what the per-module erasure contract wants.
- **No user PII reaches the store.** The row holds ids, a stage, dates, a campaign and two enum values that travel as text. The role, notes, company name and compensation stay where the user wrote them, which is the same line the events already draw.
- The timestamp guard means a row records *when each fact was true*, not when it was received. Anything reading the table directly has to respect that, and a metric computed from arrival order would be wrong.

## Alternatives considered

- **Incremental counter tables** — rejected. Under at-least-once delivery every increment needs an `EventId` ledger to dedupe against, and a campaign move becomes a decrement and an increment that must both apply or neither. It is buildable and it is a great deal of machinery to get a number that a grouped scan over base rows returns for free. Counters earn their complexity at volumes this product does not reach.
- **Replaying the outbox to rebuild** — not available. Processed rows are pruned; the buffer is empty of exactly the history a rebuild would need.
- **Raising the outbox retention so it can serve as a permanent log** — rejected. It converts a delivery buffer into an event store by neglecting to clean it, grows without bound, and puts one module's durability requirement onto infrastructure every module shares. A permanent log is a thing worth building deliberately if it is ever wanted; it is not a thing worth acquiring by disabling a prune.
- **No campaign dimension, leaving `ApplicationMovedToCampaign` unconsumed** — rejected. It is the cheaper build today and it is the one deferral pruning makes irreversible: every move that happened while the dimension was missing is unrecoverable, so the figures could never be produced for the applications that most needed them. The event was published ahead of its consumer for this reason, and declining to consume it now would waste that foresight.
- **A separate campaign rollup beside the base rows** — rejected. It inherits the counter problems above and adds a second structure to keep in step with the first.
- **Reading the Applications module's tables** — not available, and the constraint is the point rather than an obstacle: it is what forces the events to carry whole facts.
