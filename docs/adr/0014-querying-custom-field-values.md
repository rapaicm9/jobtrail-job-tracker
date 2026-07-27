# 0014 — Querying custom-field values

- **Status:** Accepted
- **Date:** 2026-07-27
- **Builds on:** ADR-0013 (custom-field value storage)

## Context

ADR-0013 settled the value bag as a `jsonb` object keyed by definition id, chosen precisely so a single field would be reachable by path. This is the work that shape was chosen for: filtering the application list by a custom-field answer, and sorting by one.

Two things about the arrangement had to be established rather than assumed. The column is **value-converted** — EF sees `CustomFieldValues` as a `string` with a `jsonb` store type — so whether the provider would translate JSON operators over it at all was an open question. And ADR-0004's instruction to "GIN-index only the paths actually queried" turns out not to be executable as written.

## Decision

**Containment for filtering, one `jsonb_path_ops` GIN index, and an accepted scan for sorting.**

- **Filtering is a containment test**, `custom_field_values @> '{"<id>": <value>}'`. Not a path comparison: containment is the operator a `jsonb_path_ops` index serves, and it means the right thing for every type — including multi-select, where containment of a one-element array is exactly "is this option among them".
- **The filter value is coerced to the field's JSON type before the probe is built.** A query string carries text, but containment compares JSON to JSON: `{"id":"3"}` does not contain-match a stored `3`. Without the coercion a client filtering a number field would get a confidently empty page instead of an answer. The definition is read first, and a value that cannot be its type is a **422** rather than an empty page — an empty page already means "no matches", and the two must not be spelled the same way.
- **One GIN index over the whole column, `USING GIN (custom_field_values jsonb_path_ops)`.** ADR-0004 said to index only the paths actually queried; here a path *is* a definition id, so the paths are per-account data and indexing them individually would mean DDL driven by what users create. `jsonb_path_ops` is the resolution rather than a compromise: it is smaller and faster than the default operator class precisely because it supports one operator, `@>` — the only one the filter uses.
- **Sorting by a custom field is deliberately unindexed.** GIN cannot order, so a custom-field sort is a scan and sort. At the size one person's job search reaches that is the right trade, and the alternative — an expression index per definition id — is the same unbounded DDL rejected above.
- **`EF.Functions.JsonContains` translates over the converted property.** Verified against real PostgreSQL before the slice was built, because the fallback (raw SQL fragments composed with `FromSql`) would have shaped the code very differently. The filter is therefore ordinary LINQ and composes with the existing keyset predicate and page builder untouched.
- **Filtering and sorting by custom field are Pro, checked in the handler.** The list endpoint serves both tiers, so the entitlement covers the optional parameters rather than the call — the same arrangement values use, and for the same reason. Unlike values there is no retention argument: reading applications is unaffected, and it is searching by a custom field that is the paid capability.

## Consequences

- Filtering is equality only. `@>` cannot express a range, so "priority above 3" or "follow-up before October" are not available; they would need a different index and a much larger query-parameter grammar. The specification asks to "filter by any custom field" without naming operators, and equality satisfies it. Ranges are a v2 conversation, not an oversight.
- The index is asserted by its definition in `pg_indexes`, not by watching the planner choose it. At test-data volumes a sequential scan is genuinely cheaper and Postgres is right to take it, so a plan assertion would fail for the wrong reason. What is worth pinning is the operator class, since the default one indexes considerably more than this needs.
- A filtered list still pages by the same cursor. The filter is a query parameter resent alongside the cursor rather than something baked into it — a cursor is a position, and it positions correctly in whatever list it is handed to.
- The provider now translates one expression whose behaviour depends on a value converter staying as it is. Changing the converter's provider type would silently stop the filter translating, which is the kind of thing a test catches only because the filter tests run against a real database.

## Alternatives considered

- **A path comparison, `custom_field_values ->> '<id>' = …`** — rejected for filtering. It reads more naturally and no GIN index can serve it, so every filter would be a scan; containment gets the same answer and can use the index.
- **An expression index per definition id** — rejected. Paths are user data; this is unbounded DDL driven by what accounts create, and it would have to be created and dropped as fields come and go.
- **The default `jsonb_ops` GIN class** — rejected. It supports key-existence operators this design never uses, in exchange for a substantially larger index.
- **Raw SQL fragments for the filter** — rejected once the spike showed the translation works. It was the fallback, and it would have meant hand-writing a query that LINQ expresses correctly.
- **Treating an untypeable filter value as "no matches"** — rejected. It makes a client mistake indistinguishable from a true empty result, which is the failure mode filters are notorious for.
