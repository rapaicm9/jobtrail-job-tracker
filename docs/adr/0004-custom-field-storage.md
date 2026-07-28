# 0004 — Custom field storage and querying

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-07-27 — the storage mechanism and the indexing rule, once the slice was built. See *Revision history*.

## Context

Applications carry a fixed set of fields everyone needs, plus a Pro feature letting each user define their own fields (of varying types) that then appear on every application. The per-user fields are heterogeneous and unknown at schema-design time, but Pro users must still be able to filter, sort, and chart on them. I want flexibility without an entity-attribute-value swamp or a second database.

One property of the problem decides most of what follows: **the keys are runtime data.** A custom field's identity is a row a user created a moment ago, so the shape of the value bag is not knowable when the model is built, and any mechanism that fixes its shape at model-build time cannot express it. That tension is what settled the mapping, and what makes "index the paths that matter" harder than it sounds — a path here *is* a definition id, which means paths are per-account data.

## Decision

A **hybrid** model: relational for everything shared, JSONB for the per-user remainder.

### Built-in fields

**First-class relational columns** on the application (role, company reference, compensation amount + currency, location, work mode, posting URL, source/channel, application deadline, applied date, CV/cover-letter labels). Because they are columns, they are always filterable, sortable, and chartable — this is exactly why they are built in rather than "just" custom fields.

### Definitions

A relational table, account-scoped, with a label, data type (text, number, date, checkbox, single-select, multi-select, URL), and options for selects.

### Values

**One `jsonb` column on the application, holding an object keyed by definition id, mapped with a value converter.**

```json
{"0199…-a1":"Platform","0199…-b2":3,"0199…-c3":["Go","Rust"]}
```

- **Values are stored as the raw JSON scalar their type calls for** — a string, a number, a boolean, an array of strings — not wrapped in a shape of our own. Wrapping would push every query one level deeper and buy nothing, since the bag holds answers and the answer *is* the value.
- **Keyed by id, so a single field is addressable by path.** `custom_field_values -> '<id>'` is what filtering, sorting and indexing all rest on.
- **A value converter with an explicit `ValueComparer`.** The comparer compares the stored document rather than the reference, because every update reassigns the bag and reference equality would rewrite the column on every edit of an application — including edits that never touched a custom field.
- **The bag is immutable** and replaced wholesale. Nothing reaches into it, so the change tracker's snapshot can be the value itself.
- **The values are not typed in the domain.** The type of an answer lives on its definition, and the aggregate never interprets an answer — it records what came in and hands it back. Typing is enforced once, at the edge, against the definitions: JSON shape must match the field's type, selects must name a real option, dates must be `yyyy-MM-dd`, text is length-capped.

### Querying

- **Filtering is a containment test**, `custom_field_values @> '{"<id>": <value>}'`. Not a path comparison: containment is the operator a `jsonb_path_ops` index serves, and it means the right thing for every type — including multi-select, where containment of a one-element array is exactly "is this option among them".
- **The filter value is coerced to the field's JSON type before the probe is built.** A query string carries text, but containment compares JSON to JSON: `{"id":"3"}` does not contain-match a stored `3`. Without the coercion a client filtering a number field would get a confidently empty page instead of an answer. The definition is read first, and a value that cannot be its type is a **422** rather than an empty page — an empty page already means "no matches", and the two must not be spelled the same way.
- **One GIN index over the whole column, `USING GIN (custom_field_values jsonb_path_ops)`.** Not one per queried path: a path is a definition id, so indexing paths individually would mean DDL driven by what users create. `jsonb_path_ops` is the resolution rather than a compromise — smaller and faster than the default operator class precisely because it supports one operator, `@>`, the only one the filter uses.
- **Sorting by a custom field is deliberately unindexed.** GIN cannot order, so a custom-field sort is a scan and sort. At the size one person's job search reaches that is the right trade, and the alternative — an expression index per definition id — is the same unbounded DDL rejected above.
- **`EF.Functions.JsonContains` translates over the converted property.** Verified against real PostgreSQL before the slice was built, because the fallback (raw SQL fragments composed with `FromSql`) would have shaped the code very differently. The filter is therefore ordinary LINQ and composes with the existing keyset predicate and page builder untouched. Ordering has no expression-tree form and is written as SQL.

### Entitlement

The whole custom-field capability sits behind `Feature:CustomFields` (ADR-0005), applied per operation rather than per route:

- **Writing values is gated in the handler, not on the route.** Both tiers create and edit applications, so the entitlement covers one part of the payload rather than the call. Sending the bag without the entitlement is a **403**; leaving it off retains what is stored. For an entitled caller the bag replaces like every other field on a full-replace `PUT`, absent meaning cleared.
- **Filtering and sorting by a custom field are gated the same way**, on the list endpoint's optional parameters. Unlike values there is no retention argument: reading applications is unaffected, and searching by a custom field is the paid capability itself.
- **Reading values back is never gated.** An account that lost the entitlement must still be able to interpret its own applications.

## Consequences

- New custom fields need no schema migration — a user adds a field and stores values immediately.
- The relational-vs-JSONB line is a standing rule: anything that must be filtered/joined/aggregated across all users belongs in a column; the flexible per-user remainder belongs in JSONB.
- The JSON on disk is what a person would write by hand, which is also what the querying needs. `@> '{"<id>":"Platform"}'` is a containment probe `jsonb_path_ops` serves directly.
- **Filtering is equality only.** `@>` cannot express a range, so "priority above 3" or "follow-up before October" are not available; they would need a different index and a much larger query-parameter grammar. The specification asks to filter by any custom field without naming operators, and equality satisfies it. Ranges are a v2 conversation, not an oversight.
- **EF cannot model the bag's shape**, so nothing inside it is reachable by LINQ property access. One expression — `JsonContains` — translates, and its behaviour depends on the value converter's provider type staying as it is. Changing that would silently stop the filter translating, which is the kind of thing caught only because these tests run against a real database.
- The index is asserted by its definition in `pg_indexes`, not by watching the planner choose it. At test-data volumes a sequential scan is genuinely cheaper and Postgres is right to take it, so a plan assertion would fail for the wrong reason. What is worth pinning is the operator class, since the default one indexes considerably more than this needs.
- A filtered list still pages by the same cursor. The filter is a query parameter resent alongside the cursor rather than something baked into it — a cursor is a position, and it positions correctly in whatever list it is handed to.
- **"Retained read-only" is a property of the update path, not of storage.** Nothing about the column enforces it; the rule is that an unentitled caller's edit leaves the bag alone. That is the one behaviour here worth guarding with tests, and it is.
- `ErrorType.Forbidden` enters the kernel with this work. There was no way to say "known caller, understood request, not permitted" before, because every prior refusal was an authorization policy on a route.
- The definitions table is `custom_fields` and the value column is `custom_field_values` — close enough to confuse, distinct enough to compile. Named apart deliberately.

## Alternatives considered

- **EAV table** (one row per field value) — rejected; painful querying, weak typing, poor performance for charts.
- **A document database** for applications — rejected; PostgreSQL JSONB gives the document flexibility while keeping a single relational store, transactions, and joins for everything else.
- **An EF complex type or complex collection mapped with `ToJson()`** — rejected; see the revision history. A complex type's shape is fixed at model-build time, and a complex collection stores an array, which has no per-field path to filter, index or order by.
- **Wrapping each value in a typed envelope** (`{"text":"…"}`) — rejected. It makes the C# side marginally more typed and every path, index and containment probe one level deeper, in exchange for typing the definition already carries.
- **A `JsonElement`-free domain with a discriminated value union** — rejected for now. It would be real machinery serving no reader: nothing in the aggregate branches on a custom value, and validation against the definitions happens once at the edge either way.
- **`null` stored as an answer** — rejected. An absent answer and an answer of null are the same fact, and keeping both would make every consumer handle two spellings of "unanswered". A null clears the key.
- **A path comparison, `custom_field_values ->> '<id>' = …`** — rejected for filtering. It reads more naturally and no GIN index can serve it, so every filter would be a scan; containment gets the same answer and can use the index.
- **An expression index per definition id** — rejected. Paths are user data; this is unbounded DDL driven by what accounts create, and it would have to be created and dropped as fields come and go.
- **The default `jsonb_ops` GIN class** — rejected. It supports key-existence operators this design never uses, in exchange for a substantially larger index.
- **Raw SQL fragments for the filter** — rejected once the spike showed the translation works. It was the fallback, and it would have meant hand-writing a query LINQ expresses correctly.
- **Treating an untypeable filter value as "no matches"** — rejected. It makes a client mistake indistinguishable from a true empty result, which is the failure mode filters are notorious for.

## Revision history

- **2026-07-16 — original.** The hybrid model, and a mechanism named alongside it: values as an EF complex type mapped with `ToJson()`, with a GIN index over only the JSON paths actually queried.
- **2026-07-27 — the named mechanism could not hold** *(recorded separately at the time as ADR 0013)*. An EF complex type has a shape fixed when the model is built; this bag's keys are definition ids that exist only at runtime. The nearest thing EF can express is a complex *collection*, which stores an array — `[{"fieldId":"…","text":"Platform"}, …]` — satisfying the word "complex type" while defeating everything else this ADR asks for: an array has no per-field path, so there is nothing to put on the left of `->`, nothing to index for one field, and sorting by a single custom field would need a lateral join. The mechanism and the requirement were in conflict and the requirement won. A value-converted object keyed by definition id replaced it.
- **2026-07-27 — "GIN-index only the paths actually queried" is not executable** *(recorded separately at the time as ADR 0014)*. A path is a definition id, so paths are per-account data and per-path indexes would be DDL driven by user activity. One `jsonb_path_ops` index over the column replaced it, which is *narrower* than the original instruction in operator support and broader in coverage. Also settled here: `EF.Functions.JsonContains` does translate over the converted column, so the expectation recorded a day earlier — that all querying would have to be raw JSONB fragments — was wrong, and only ordering is written as SQL.
