# 0004 — Custom field storage

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

Applications carry a fixed set of fields everyone needs, plus a Pro feature letting each user define their own fields (of varying types) that then appear on every application. The per-user fields are heterogeneous and unknown at schema-design time, but Pro users must still be able to filter, sort, and chart on them. I want flexibility without an entity-attribute-value swamp or a second database.

## Decision

A **hybrid** model:

- **Built-in fields are first-class relational columns** on the application (role, company reference, compensation amount + currency, location, work mode, posting URL, source/channel, application deadline, applied date, CV/cover-letter labels). Because they are columns, they are always filterable, sortable, and chartable — this is exactly why they are built in rather than "just" custom fields.
- **User-defined custom fields:**
  - *Definitions* live in a relational table, account-scoped, with a label, data type (text, number, date, checkbox, single-select, multi-select, URL), and options for selects.
  - *Values* live in a **JSONB column** on the application, mapped as an EF Core 10 complex type via `ToJson()`, keyed by field-definition id.
- Filtering/sorting/charting on custom fields (Pro) uses native PostgreSQL JSONB operators (`->`, `->>`, `@>`); **GIN-index only the JSON paths actually queried**, not the whole document.
- The whole custom-field capability is gated behind the `Feature:CustomFields` entitlement (ADR-0005).

## Consequences

- New custom fields need no schema migration — a user adds a field and stores values immediately.
- The relational-vs-JSONB line is a standing rule: anything that must be filtered/joined/aggregated across all users belongs in a column; the flexible per-user remainder belongs in JSONB.
- Indexing is a deliberate act; unindexed JSONB path queries are acceptable only at small scale.

## Alternatives considered

- **EAV table** (one row per field value) — rejected; painful querying, weak typing, poor performance for charts.
- **A document database** for applications — rejected; PostgreSQL JSONB gives the document flexibility while keeping a single relational store, transactions, and joins for everything else.
