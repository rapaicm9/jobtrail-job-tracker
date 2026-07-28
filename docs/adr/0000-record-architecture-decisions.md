# 0000 — Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-07-29 — one record per *topic*, amended in place, rather than one per decision-event. See *Revision history*.

## Context

This is a solo, long-lived project built to commercial standards. Decisions made early (boundaries, auth, storage shapes) are expensive to reverse later, and the reasoning behind them is easy to forget. I want the *why* to be discoverable next to the code, not living only in my head.

These records have one reader, arriving with one question: *what is the current design of X, and why is it that?* An account that answers it by making them assemble four files in the right order — where the third overturns a mechanism the first named — is an account that will be read wrong.

## Decision

Use lightweight Architecture Decision Records (Nygard style) under `docs/adr/`, numbered sequentially (`NNNN-title.md`), **one record per topic**.

- **An accepted ADR is amended in place when a later decision refines or extends it.** The Decision section always states the design as it stands now; a **Revision history** at the foot records what changed, when, and what it overturned. The chronology stays visible without the current truth being split across files.
- **Supersession is different from refinement, and keeps the old rule.** A decision that replaces an ADR wholesale gets its own number, and the old record's status becomes `Superseded by NNNN`. Refinement means the topic survives and the reasoning grows; supersession means the topic was decided differently.
- **Numbers are never reused, and gaps are expected.** A record folded into another leaves its number retired, and the surviving record's revision history names it — so a reference to a retired number found in an old commit message still leads somewhere.
- Statuses: `Proposed` → `Accepted` → `Superseded` / `Deprecated`.
- Keep them short: context, decision, consequences, and alternatives where the road not taken matters.

## Template

```
# NNNN — Title

- Status: Proposed | Accepted | Superseded by NNNN | Deprecated
- Date: YYYY-MM-DD
- Amended: YYYY-MM-DD — what changed (omit until there is one)

## Context
What forces are at play — technical, product, cost, licensing.

## Decision
The choice made, stated plainly, as it stands today.

## Consequences
What becomes easier, what becomes harder, what we now have to maintain.

## Alternatives considered
Options rejected and why.

## Revision history
Dated entries: what changed and what it overturned (omit until there is one).
```

## Consequences

- A small amount of writing overhead per decision, in exchange for a durable, reviewable trail. Anyone reading the repo can reconstruct the reasoning without archaeology.
- **Amending costs more than appending.** Folding a refinement in means re-reading the record it lands in and correcting anything it contradicts, rather than filing a new document beside it. That is the work being paid for: the contradiction gets resolved once, by the person who understands it, instead of by every future reader.
- Git history remains the immutable record. Nothing is lost by amending a file that is version-controlled — `git log -p` on an ADR is the decision-by-decision account the old rule was trying to preserve.

## Revision history

- **2026-07-16 — original.** One ADR per decision, immutable once accepted, changes recorded as new superseding records.
- **2026-07-29 — one record per topic, amended in place.** By this point three chains had formed: custom-field storage across three records, entitlements across two, durable delivery across three. Each chain's later entries refined the first rather than replacing it, so the immutability rule was producing exactly the outcome it was meant to prevent — a reader who consults the ADR for a topic and finds an account that a later file quietly overturns. The chains were folded into their root records (0004, 0005, 0009) and 0010, 0012, 0013, 0014 and 0015 retired.

