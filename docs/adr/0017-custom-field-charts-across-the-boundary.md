# 0017 — Custom-field charts across the module boundary

- **Status:** Accepted
- **Date:** 2026-07-29
- **Amended:** 2026-07-31 — the GIN index does not serve this query; a number field returns summary statistics, not bins; the output cache this record called for is not built. See *Revision history*.

## Context

Pro accounts are promised charts over their own custom fields — the select, number and date types; text and URL were never chartable. The data those charts need lives entirely inside the Applications module: definitions in a relational table, answers in one `jsonb` column on the application keyed by definition id, with a `jsonb_path_ops` GIN index and the type coercion that makes a filter mean the right thing for each type (ADR 0004). Analytics cannot read any of it.

**No published event carries an answer**, and that is a decision rather than a gap. The events carry ids and the few values a consumer could not otherwise derive; the user's own account of their job search — the role, the notes, the compensation — was deliberately kept off the stream. A custom field is the most user-authored data in the product: the label is theirs, the select options are theirs, and the answers are whatever they typed. Routing that onto the stream would put arbitrary user text into every consumer's store and into every outbox payload, reversing a line the module has already drawn.

So the charts need a route that does not exist yet, and the obvious one is the one the module spent a sprint avoiding.

## Decision

**The Applications module aggregates its own custom-field data and exposes the result through its Contracts assembly. Analytics calls it when a chart is asked for and stores nothing.**

- **Contracts gains a read query returning buckets, not answers.** For an owner and a definition id: counts per option for a select, a five-number summary for a number, counts per month for a date. Individual answers never cross the boundary — only figures that are already aggregated do.

  A number field returns **count, minimum, quartiles and maximum rather than histogram bins**. Bin boundaries are a presentation decision, and any set chosen here is one the chart cannot undo; five order statistics carry the shape of the distribution and let the caller draw a box plot, or anything else, without being committed to a bin count it did not pick.

  **Checkbox is not offered**, matching the product's own list of chartable types. It would not breach the safety property below — a checkbox is not free text — so it is a scope decision that could be revisited cheaply.

- **The aggregation runs where the data and the per-type coercion already are.** Reproducing them elsewhere would mean reproducing the bag itself, which is the thing being avoided.

  It is worth being exact about what this does *not* buy, because the original wording of this record overstated it: **the `jsonb_path_ops` GIN index does not serve this query.** That operator class supports containment alone, which is what filtering by an exact value needs and what aggregating every value for one path cannot use. The chart is a scan whichever module runs it. The boundary and the coercion are the reasons this lives in Applications; the index is not one of them.

  What the query does do is **project a single path out of the document rather than loading the bag**, so no other field's values — a free-text note, a URL — are so much as fetched while a chart is drawn. That is a narrower guarantee than the boundary alone gives, and it is the one worth having here.
- **The query offers nothing for text or URL.** Those types are not chartable, so free text has no route out of the module even by mistake. The narrowness is a safety property, not just scope.
- **The call is synchronous and budgeted**, in the same shape as the entitlement and profile queries other modules already make across Contracts. It is a call to a module, not a read of its tables, and it stays within the boundary rules for that reason.
- **The gate lives on the Analytics endpoint** that serves the panel, re-checked in the handler as every gated operation is (ADR 0005). Reading definitions remains ungated, so an account that lost the entitlement can still interpret what it already recorded.

## Consequences

- **One panel on the dashboard is served synchronously from another module rather than from the read model.** It is the only one — every other figure comes from Analytics' own base rows (ADR 0016) — and it is worth knowing that the exception exists and why, rather than discovering it later as an inconsistency.
- **These charts are live where their neighbours are near-real-time.** A panel fed by events lags the stream slightly; this one cannot lag, because no stream is involved. Two panels on one screen can therefore disagree for a moment after a write. That is a real artifact and the endpoint should be honest about it rather than pretending to a consistency it does not have.
- **The panel depends on the Applications module being reachable.** A failure there degrades one panel, and it should render as unavailable — never as zero, which is a number the user would believe. This required a new kernel error type, `Unavailable`, mapping to **503**: the request was understood and permitted, and something it depends on is not answering. A 500 would say the server is broken when the rest of the dashboard is fine, and an empty chart would be a claim about the user's own data that Analytics is in no position to make.
- **"No chart here" is one answer covering three cases** — no such definition, somebody else's, and a type that is not charted. They are deliberately not distinguished: ownership lives inside the query as it does everywhere in that module, and separating them would confirm that an id the caller does not own exists.
- **Repeat views of a dashboard are repeat aggregations, and that is accepted rather than cached away.** This record originally called for output caching here, on the grounds that it matters more for this panel than for the base-row ones because the work lands in another module's database. The asymmetry is real; the conclusion did not survive being costed. The aggregation is a scan over one account's applications — hundreds of rows — so what a cache would save is unmeasurable, while ASP.NET Core declines by default to cache responses to authenticated requests, meaning a cache here starts by disabling that protection and hand-rolling per-caller keying. Trading a cross-account leak risk against an unobserved saving is the wrong way round. If the panel ever shows up in a latency measurement, this is the first place a cache would go, and the owner-plus-definition-plus-query key is still the right one.
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

## Revision history

- **2026-07-29 — original.** The Applications module aggregates its own custom-field data and publishes buckets through Contracts; Analytics stores nothing, text and URL are offered nothing, and the panel degrades to unavailable rather than zero.
- **2026-07-30 — built, and one of the stated reasons did not hold.** This record justified running the aggregation inside Applications partly on the `jsonb_path_ops` GIN index. That index serves containment, which is what the custom-field *filter* needs; aggregating every value for one path cannot use it, so the chart is a scan wherever it runs. The boundary and the per-type coercion are the real reasons and are unchanged — but building on a rationale that does not hold is how a later decision goes wrong, so it is corrected here rather than left. Settled at the same time: a number field returns five order statistics rather than bins, because bin boundaries are a presentation choice the caller cannot undo; checkbox stays out of scope though it would breach nothing; the query projects one JSON path rather than loading the bag, so no other field's values are fetched while a chart is drawn; "no chart here" covers three cases as one answer; and degradation needed a new `ErrorType.Unavailable` mapping to 503.
- **2026-07-31 — the output cache this record asked for is not being built.** The consequence above called for caching keyed by owner, definition and query, on the grounds that this panel's work lands in another module's database. That asymmetry is real and is why this would be the first panel to cache; the call itself did not survive being costed. The aggregation scans one account's applications, so the saving is unmeasurable, while caching an authenticated response means switching off the framework's own refusal to do so and taking on per-caller keying by hand — a cross-account leak risk accepted for a gain nobody had measured. Rewritten as an accepted cost with the trigger that would reverse it. ADR 0016 records the same decision for the base-row panels.
