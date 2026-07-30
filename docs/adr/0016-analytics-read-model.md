# 0016 — Analytics read model

- **Status:** Accepted
- **Date:** 2026-07-29
- **Amended:** 2026-07-30 — the timestamp guard is per fact group, not per row, and per assignment rather than per statement; monotone facts need no guard; the funnel needs its own columns; ties apply; the module also holds one row it was told rather than derived. See *Revision history*.

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

### Two kinds of column, and only one of them needs guarding

The guard above is **per fact group, not per row**, and the distinction decides whether the model is correct.

A single row-level watermark looks like the obvious reading and is wrong. An `InterviewScheduled` that occurred later but arrived first would reject an entire stage change as stale — discarding the stage, the outcome and the entry timestamp, none of which that interview carries. Guarding each group separately means an out-of-order event loses only the facts it actually competes for. In practice this is two watermarks: one over the stage group (stage, outcome, entry time, closed time), one over the campaign, which is written both by the initial attribution on `ApplicationSubmitted` and by every later `ApplicationMovedToCampaign`.

**Facts that record the first time something happened need no watermark at all.** Written as `LEAST(existing, incoming)`, they are commutative and idempotent, so neither redelivery nor arrival order can disturb them — the machinery the latest-wins columns need simply does not apply. PostgreSQL's `LEAST` ignores nulls and returns null only when everything is null, which handles "nothing recorded yet" and "nothing new" without any surrounding logic. That is a documented deviation from the SQL standard, so the correctness of these columns is tied to PostgreSQL specifically, which is worth knowing and is not a cost here.

**The guard belongs on each assignment, not on the statement.** An `ON CONFLICT DO UPDATE` may carry a `WHERE`, and using it here is the obvious move and the wrong one: it gates the *whole* update, so a stale event would skip the monotone columns too — the ones that are meant to be immune to ordering. The funnel would then develop holes that appear only under retry. So each latest-wins column carries its own conditional and the monotone ones sit outside it. The cost is a more verbose statement; the return is that every column states the rule it actually follows.

**A tie applies rather than losing.** The comparison is *at least as new*, not *newer*. A pipeline move and the closure or reopening it amounts to are recorded in one transaction and carry one instant, so a strict comparison would let whichever arrived first silently discard the other — losing either the stage or the outcome, at random, on every terminal transition. Applying both is safe because they write disjoint columns, which is itself a constraint worth preserving: if two events at one instant ever come to write the same column, this rule stops holding and the tie has to be broken some other way.

### The funnel cannot be read off the current stage

A forward move may skip, and a terminal stage erases the path. An application that went Applied → Screening → Rejected shows `Rejected`, which says nothing about having reached Screening; one that jumped Applied → Offer never had an interview. A funnel or a response rate computed from the current stage is wrong in both directions.

So the row keeps **an explicit timestamp for each pipeline stage it first reached**, plus the first genuine response. Three columns then serve four figures: the funnel and the rates count non-null entries, time-to-offer measures to one of them, and time-in-stage falls out of the intervals between them, because the next stage's entry is the previous stage's exit. These describe the **first pass** through the pipeline; a reopened application that walks a stage twice is measured on the first walk.

**A response is not merely a move off Applied.** Being ghosted is the absence of a response and withdrawing is the user's own act; counting either would inflate the response rate with precisely the applications that never got an answer.

### Two columns are nullable that look like they should not be

`campaign_id` and `applied_date` are carried only by `ApplicationSubmitted`, and delivery is unordered — so an application whose stage change arrives first has to be recordable without them. The alternative is refusing the row, which drops the stage change instead, and losing a fact is the outcome this whole design exists to avoid. A briefly incomplete row that heals when the missing event lands is the cheaper failure, and the reads filter accordingly.

### Campaign

**A column on the base row, not a rollup of its own.** `ApplicationMovedToCampaign` is then a single-column update — it carries both ends of the move, so it applies without knowing the campaign the row currently shows, and it re-applies harmlessly.

Campaign-scoped figures are the same aggregation with one more predicate. The read endpoints take an optional campaign filter, and **the filter itself is never gated**: an account with one campaign simply gets the same numbers back, so gating it would refuse a narrowing that changes nothing. That is separate from whether the endpoint it narrows is gated — the paid figures are (ADR 0005), and the filter adds no second gate on top of that.

### What is not consumed

**`InterviewCancelled` has no projection.** The column it would touch records that a round was once booked, and cancelling it does not make that untrue. Un-setting a monotone column would also destroy the property that makes it safe — that the earliest value always wins regardless of order — in exchange for a figure nobody asks for.

### One row here is authored rather than derived

The weekly application goal is a number the user types, and it lives in this schema beside the projected rows. That is a real exception to the sentence this record opens with, so it is stated rather than left to be discovered.

It belongs here because the figure it is read against is here. Progress toward a weekly goal is a count of the account's applications inside the current week, which is this module's arithmetic over this module's rows; putting the target anywhere else would mean either a second implementation of the weekly count in a module that does not own it, or a cross-module read to fetch one integer.

Three things follow from the row being authored, and they are the useful part:

- **It has no ordering constraint.** The erasure below runs late because an owed event could rebuild a projected row. No event carries the goal, so nothing can put it back and it would be safe to delete at any point. It is erased with the rest because that is where the module gives back what it holds, not because the sequence demands it.
- **"Rebuildable" does not apply to it at all.** The base rows are unrebuildable because the stream behind them is pruned; this row is unrebuildable because there was never a stream. The backup story stated in the consequences covers both, for different reasons.
- **The account is the key.** One goal per account, so the owner is the primary key and the database holds the rule. Clearing a goal deletes the row rather than zeroing the column, so "no goal" has one representation and no read has to know that a stored zero means absent.

**The goal carries no campaign**, though every other read here takes an optional campaign filter. A target of eight measured against one campaign's slice reports progress toward a number the user never set. The goal spans the search; the filter is not offered rather than being offered and ignored.

**Progress is reported only when a target is set.** With no goal the response carries a null target and a null count, not a count on its own — that would be a bare weekly application total, which is what the paid trend sells, handed to every tier through an ungated route. The consequence is that an account which never had Pro has nothing to read here, and one that downgrades keeps both its target and its progress, which is what ADR 0005 requires of data the account entered.

### Erasure

One delete by owner per table, and **the base rows must go after the Applications module's erasure**. That module's erasure removes the events still owed on the user's behalf; if these rows went first, an owed event delivered in the gap would rebuild one for an account that had just been erased. Handlers run in registration order, so the ordering is a property of where the host composes this module — invisible at the call site, and therefore asserted by a test rather than left to a comment.

### What the reads return

**The pipeline snapshot is sparse.** It reports the stages the account actually has, and does not pad the set out with zeros. Padding would mean this module declaring the complete list of stages, which is the pipeline owner's to define and the one thing storing the stage as arrived text was chosen to avoid. A client renders the board and therefore already holds that list, so filling gaps is work it is doing anyway. Ordering is still deterministic — the live pipeline in its own order, everything else after it alphabetically — because outcomes carry no order among themselves and inventing one would be a second claim not worth making.

**A row with no stage counts toward the total but is not a column.** An interview or a campaign move creates the row if it arrives first and neither carries a stage, so a null is reachable. It is still an application the account recorded, so the total includes it; null is not a stage, so the snapshot omits it. Without stating this the two figures look inconsistent, and a client would eventually be handed a column with no name.

**An account with nothing recorded gets zeros and a 200**, never a 404. Having no applications is a normal state rather than a missing resource.

### Aggregation and rebuild

Figures are computed from the base rows on read, behind the output cache. **Rebuilding means recomputing from the base rows** — not replaying the stream, which is not available.

## Consequences

- **"Rebuildable" is honest, but narrower than it sounds, and this is the sharp edge.** Rollups rebuild from base rows. The base rows cannot be rebuilt from anything: they are the system of record for analytics and need backing up like any other table. Losing them loses the history, and no amount of correct projection code changes that.
- Aggregating on read costs more than incrementing a counter. At the volume one person's job search reaches — hundreds of rows over a year, not millions — a grouped scan is not a cost worth designing against, and the output cache absorbs the repeat views a dashboard generates. If the shape were ever wrong it would be wrong slowly and visibly, which is the failure mode to prefer.
- Adding a metric usually costs a query rather than a migration and a backfill, because the facts are already in the rows. That is the main return on storing rows instead of totals.
- Erasure is one delete by owner, which is what the per-module erasure contract wants.
- **The module has a write surface, which a read model is not obliged to have.** It is one route setting one integer, and it does not make this a transactional module: nothing here is published, nothing else depends on the goal, and no other module may read it. Worth naming so the next thing that wants a write here has to argue for itself rather than cite this.
- **No user PII reaches the store.** The row holds ids, a stage, dates, a campaign and two enum values that travel as text, plus one integer the account chose for itself. The role, notes, company name and compensation stay where the user wrote them, which is the same line the events already draw.
- The timestamp guard means a row records *when each fact was true*, not when it was received. Anything reading the table directly has to respect that, and a metric computed from arrival order would be wrong.
- **Stage is stored as the text that arrived, with no enum of this module's own.** The pipeline belongs to the Applications module and mirroring it here would mean asserting knowledge of something not owned, with an unrecognised name throwing on the delivery path. Stored as text, an unknown stage is reported as itself; nothing downstream depends on knowing the full set, because the funnel reads the timestamp columns rather than the stage.
- **The base rows begin at the migration that creates them.** Everything recorded before it is invisible to analytics forever, pruning having removed the events. That is free while the product has no users and stops being free at the first deploy, which is the moment this table's backup story starts to matter.

## Alternatives considered

- **Incremental counter tables** — rejected. Under at-least-once delivery every increment needs an `EventId` ledger to dedupe against, and a campaign move becomes a decrement and an increment that must both apply or neither. It is buildable and it is a great deal of machinery to get a number that a grouped scan over base rows returns for free. Counters earn their complexity at volumes this product does not reach.
- **Replaying the outbox to rebuild** — not available. Processed rows are pruned; the buffer is empty of exactly the history a rebuild would need.
- **Raising the outbox retention so it can serve as a permanent log** — rejected. It converts a delivery buffer into an event store by neglecting to clean it, grows without bound, and puts one module's durability requirement onto infrastructure every module shares. A permanent log is a thing worth building deliberately if it is ever wanted; it is not a thing worth acquiring by disabling a prune.
- **No campaign dimension, leaving `ApplicationMovedToCampaign` unconsumed** — rejected. It is the cheaper build today and it is the one deferral pruning makes irreversible: every move that happened while the dimension was missing is unrecoverable, so the figures could never be produced for the applications that most needed them. The event was published ahead of its consumer for this reason, and declining to consume it now would waste that foresight.
- **A separate campaign rollup beside the base rows** — rejected. It inherits the counter problems above and adds a second structure to keep in step with the first.
- **Reading the Applications module's tables** — not available, and the constraint is the point rather than an obstacle: it is what forces the events to carry whole facts.
- **One `occurred_at` guarding the whole row** — rejected; see the revision history. It is the natural reading of "an older write does not overwrite a newer fact" and it silently discards facts that the newer event never carried.
- **Deriving the funnel from the current stage** — rejected. It costs nothing to store and cannot be recovered later, and skipped and terminal stages both make the current one an unreliable witness to the path.
- **Keeping the deadline dates the events carry** — rejected, and it is the one omission this ADR's own "carry every dimension" rule argues against. They are scheduling inputs that Notifications owns rather than dimensions of any figure the dashboard shows, and consuming the two deadline events to store them would widen the projection set for a metric nobody has asked for. Recorded here because pruning makes it irreversible: if a deadline-based figure is ever wanted, it can only be built for applications recorded after that decision.
- **Mirroring the pipeline as an enum in this module** — rejected, in favour of storing the stage text as it arrived.
- **Keeping the weekly goal in Identity, as a profile preference** — rejected. It is a preference in shape only; what makes it useful is the count it is measured against, which lives here. Identity would then hold a paid analytics feature, and this module would need a cross-boundary read to fetch one integer on every dashboard load.
- **Keeping it in Applications, beside the applications it counts** — rejected. The weekly bucketing already exists here for the trend, and a second implementation of "which week is this date in" in the module that owns the transactions is the way the two eventually disagree by a day.
- **Letting a downgraded account lower its goal without the entitlement** — considered, since lowering is a reduction and ADR 0005 keeps reductions open. Rejected: it needs a "the new target must be strictly lower" rule inside the write, which is fiddly to explain and worse to discover, and deleting the goal already provides the way back to the free shape. The whole of the write is gated and the whole of the delete is not.

## Revision history

- **2026-07-29 — original.** One base row per application, upserted by application id, aggregated on read; no counters. The campaign as a column rather than a rollup, and the outbox's pruning recorded as the property that makes a missed dimension unrecoverable.
- **2026-07-30 — the guard is per fact group, and half the columns do not need one.** Building the table forced the original's single sentence — *a write carries the event's `OccurredAt`, and an older write does not overwrite a newer fact* — to resolve into something more specific. Read as one watermark over the row it is a bug: a late-arriving interview would reject a whole stage change, discarding facts that interview never carried. Two watermarks replaced it, one per group of competing writers. Separately, the facts that record *the first time* something happened turn out to need no guard whatsoever, being commutative under `LEAST`. Also settled here: the funnel needs per-stage entry timestamps of its own, because skipped and terminal stages make the current stage an unreliable witness to the path; `campaign_id` and `applied_date` are nullable as a direct consequence of unordered delivery; the stage is stored as arrived text rather than a mirrored enum; and the deadline dates are deliberately not kept.
- **2026-07-30 — building the projections settled two more things about the guard.** The first: an `ON CONFLICT DO UPDATE` can carry a `WHERE`, and putting the ordering guard there is wrong, because it gates the whole update and a stale event would skip the monotone columns as well — the exact columns the previous amendment had just established need no guard. The condition moved onto each latest-wins assignment. The second: the comparison has to be *at least as new* rather than *newer*, because a move and the closure it amounts to are written in one transaction and share an instant, so a strict comparison drops whichever arrives second. Also settled here: `InterviewCancelled` is not consumed, and erasure runs after the Applications module's so that an owed event cannot rebuild a row for an erased account.
- **2026-07-30 — the read contract, settled with the first endpoints.** The snapshot reports the stages the account has rather than a padded set, which is what storing the stage as arrived text implies once something has to render it. A row whose stage is not yet known counts toward the total and is not a column — reachable because two of the projections create a row without a stage. An empty account is zeros and a 200. The optional campaign filter, ungated as this record already required, ships on the Free figures rather than waiting for the paid ones, since adding a parameter to an endpoint already in use is a contract change and the predicate is the same either way.
- **2026-07-30 — the paid figures, and one aggregation for all of them.** The nine paid figures need five shapes of aggregation but read the same narrow slice of the same rows, so the rows are materialised once and everything is computed in memory. That is this record's own read-time bet taken one step further, and it buys two things: a median needs no ordered-set aggregate, and the stage-interval logic becomes code a unit test can drive rather than SQL only a database can answer for. It is linear in the account's applications; if that stops being cheap the aggregation moves into SQL behind the same response. Settled with it: durations are **medians with their sample counts**, because one reply after five months moves a mean by weeks; a figure with nothing behind it is **null rather than zero**, matching what ADR 0017 already required of the custom-field panel; all three rates are **over the total applied** so no client has to guess a denominator; the breakdowns **keep the not-recorded slice**, unlike the pipeline snapshot's unknown stage, because there null is a passing artifact of delivery order and here it is a fact about what the user filled in. This record's aside that "reading is never gated" was too loosely worded and is now scoped to the campaign filter; ADR 0005 carries the corrected rule.
- **2026-07-30 — one row here is authored, and this record read as though none could be.** The weekly goal is a number the user typed, so it is neither projected nor rebuildable nor subject to the erasure ordering, and the opening sentence — *one row per application, aggregated on read* — had to stop being the whole story. Recorded with it: why the goal lives in this module rather than in Identity or Applications, that the account id is the key so the database holds the one-goal-per-account rule, that clearing means deleting the row rather than storing a zero, that the goal deliberately spans the account while every other read here takes a campaign filter, and that **progress is withheld when no target is set** — a weekly count on its own is a paid figure, and an ungated route must not hand it to every tier. The module now has a write surface, which is worth naming as an exception so the next write has to argue for itself.
