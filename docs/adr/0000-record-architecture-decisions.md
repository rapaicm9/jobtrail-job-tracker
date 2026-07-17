# 0000 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

This is a solo, long-lived project built to commercial standards. Decisions made early (boundaries, auth, storage shapes) are expensive to reverse later, and the reasoning behind them is easy to forget. I want the *why* to be discoverable next to the code, not living only in my head.

## Decision

Use lightweight Architecture Decision Records (Nygard style) under `docs/adr/`, one file per significant decision, numbered sequentially (`NNNN-title.md`).

- An ADR is **immutable once Accepted**. If a decision changes, write a new ADR that supersedes the old one and update the old one's status to `Superseded by NNNN` — never rewrite history.
- Statuses: `Proposed` → `Accepted` → `Superseded` / `Deprecated`.
- Keep them short: context, decision, consequences, and alternatives where the road not taken matters.
- Each significant decision gets its own ADR; together these records are the canonical, dated account of the architecture and how it evolved.

## Template

```
# NNNN — Title

- Status: Proposed | Accepted | Superseded by NNNN | Deprecated
- Date: YYYY-MM-DD

## Context
What forces are at play — technical, product, cost, licensing.

## Decision
The choice made, stated plainly.

## Consequences
What becomes easier, what becomes harder, what we now have to maintain.

## Alternatives considered
Options rejected and why.
```

## Consequences

A small amount of writing overhead per decision, in exchange for a durable, reviewable trail. Anyone reading the repo can reconstruct the reasoning without archaeology.
