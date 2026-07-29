# 0017 — Custom-field charts across the module boundary

- **Status:** Accepted
- **Date:** 2026-07-29

## Context

Pro accounts are promised charts over their own custom fields — the select, number and date types; text and URL were never chartable. The data those charts need lives entirely inside the Applications module: definitions in a relational table, answers in one `jsonb` column on the application keyed by definition id, with a `jsonb_path_ops` GIN index and the type coercion that makes a filter mean the right thing for each type (ADR 0004). Analytics cannot read any of it.

**No published event carries an answer**, and that is a decision rather than a gap. The events carry ids and the few values a consumer could not otherwise derive; the user's own account of their job search — the role, the notes, the compensation — was deliberately kept off the stream. A custom field is the most user-authored data in the product: the label is theirs, the select options are theirs, and the answers are whatever they typed. Routing that onto the stream would put arbitrary user text into every consumer's store and into every outbox payload, reversing a line the module has already drawn.

So the charts need a route that does not exist yet, and the obvious one is the one the module spent a sprint avoiding.

## Decision

**The Applications module aggregates its own custom-field data and exposes the result through its Contracts assembly. Analytics calls it when a chart is asked for and stores nothing.**

- **Contracts gains a read query returning buckets, not answers.** For an owner and a definition id: counts per option for a select, a distribution for a number, counts per period for a date. Individual answers never cross the boundary — only figures that are already aggregated do.
- **The aggregation runs where the data and its index already are.** The GIN index and the per-type coercion exist in the Applications module and are what make this cheap; reproducing them elsewhere would mean reproducing the bag itself, which is the thing being avoided.
- **The query offers nothing for text or URL.** Those types are not chartable, so free text has no route out of the module even by mistake. The narrowness is a safety property, not just scope.
- **The call is synchronous and budgeted**, in the same shape as the entitlement and profile queries other modules already make across Contracts. It is a call to a module, not a read of its tables, and it stays within the boundary rules for that reason.
- **The gate lives on the Analytics endpoint** that serves the panel, re-checked in the handler as every gated operation is (ADR 0005). Reading definitions remains ungated, so an account that lost the entitlement can still interpret what it already recorded.

## Consequences

- **One panel on the dashboard is served synchronously from another module rather than from the read model.** It is the only one — every other figure comes from Analytics' own base rows (ADR 0016) — and it is worth knowing that the exception exists and why, rather than discovering it later as an inconsistency.
- **These charts are live where their neighbours are near-real-time.** A panel fed by events lags the stream slightly; this one cannot lag, because no stream is involved. Two panels on one screen can therefore disagree for a moment after a write. That is a real artifact and the endpoint should be honest about it rather than pretending to a consistency it does not have.
- **The panel depends on the Applications module being reachable.** A failure there degrades one panel, and it should render as unavailable — never as zero, which is a number the user would believe.
- Output caching keyed by owner, definition and query matters more here than for the base-row panels, because the work lands in another module's database. Repeat views of a dashboard should not become repeat aggregations.
- **Nothing has to be mirrored, and this is the largest saving.** Definitions get renamed, archived and created constantly; because they are read at the source when the chart is drawn, none of that needs propagating, reconciling, or backfilling. The alternative's ongoing cost is precisely this, and it never ends.
- No PII enters the analytics store, trivially, because nothing enters it.
- The Applications module acquires a reader it does not control, which is the usual cost of a Contracts query: the shape of the returned buckets is now a published interface, and changing the aggregation is a change to a contract rather than an internal edit.

## Alternatives considered

- **Events carrying answers, plus definition-mirror events** — rejected, and it was the closer call. It would keep the dashboard uniform and self-contained, which is genuinely attractive. Against that: it needs definitions mirrored into Analytics and kept in step forever through renames, archival and creation; it puts user-authored labels, options and answers onto the stream after the module deliberately kept free text off it; and a repeatedly edited number field emits an event per edit. That is a permanent, growing apparatus in service of one panel.
- **Storing answers in the analytics schema** — rejected on the grounds above, and separately because a custom field's answer is the user's own writing, which the store is meant not to hold.
- **Analytics reading the `jsonb` column directly** — not available. The boundary is what makes the module independently reasonable about its own data.
- **Dropping custom-field charts from the first version** — considered seriously, since it is the only metric forcing this decision and everything else in the module is derivable from events already published. Rejected: it is a named part of what the paid tier sells, and it would have to be withheld while the rest of the paid analytics shipped, which is the worst moment to be missing it.
- **A materialized view spanning both schemas** — rejected. It is a cross-schema read in different clothing, and it would put the Applications module's storage shape into an object Analytics owns and refreshes.
- **Precomputing the buckets inside Applications and publishing them periodically** — rejected. It reintroduces the mirror, adds staleness the synchronous call does not have, and produces the figures whether or not anyone opens the chart.
