# 0015 — What an entitlement gates

- **Status:** Accepted
- **Date:** 2026-07-28
- **Refines:** ADR-0005 (entitlements and mocked billing)

## Context

ADR-0005 settled *how* a paid capability is enforced: a `Feature:<X>` authorization policy resolved through `IEntitlementQuery`, never trusted from the client. It did not settle *what* the policy covers, and that turns out to be a separate question with a different answer per feature.

Three features have now had to answer it, and the naive reading — "gate every route that touches the feature" — was wrong all three times:

- **Custom-field definitions.** Gating the whole `/custom-fields` group would stop an unentitled account from *reading* its own definitions. Its recorded values are kept and still returned, so a client that cannot fetch the definitions cannot label what it is showing: the answers become unlabelled numbers and strings.
- **Custom-field values.** They are one part of the application payload, and both tiers create and edit applications. There is no route to gate. Worse, if an unentitled edit had simply dropped the bag, an account would drain its own answers away one ordinary edit at a time.
- **Multiple campaigns.** Gating the campaign endpoints would leave an account holding campaigns it could not rename, could not read, and — the real trap — could not delete. Deleting a campaign reassigns its applications to the default, so it is the only way back to a single campaign. Gate it and the account is stuck in a shape it can no longer reduce.

The pattern behind all three is that the entitlement buys the capability to *acquire* something, and the data acquired under it outlives the entitlement.

## Decision

**An entitlement gates acquisition, not possession.** Concretely:

- **Gate the act that creates or grows what the entitlement pays for** — defining a field, recording an answer, opening a second campaign. That act is the capability.
- **Never gate reading.** Anything an account holds stays fully readable, on its own endpoints and inside every payload that already carried it. This is not a courtesy: without it a client cannot render what it is still storing.
- **Never gate the operations that reduce or maintain what is already held** — renaming, archiving, deleting, reassigning. An account must always be able to get back to a shape the free tier allows. A gate that traps data is worse than no gate.
- **Where the gated thing is part of a payload rather than a whole call, the check moves into the handler** and covers that part only. Sending it unentitled is a **403** (`ErrorType.Forbidden` — a known caller and an understood request that is not permitted); omitting it leaves what is stored untouched.
- **Absent means "unchanged", never "cleared", for a gated portion of a full-replace request.** This is the rule that makes retention real, and it means the entitlement is read on *every* such request — the absent case is precisely the one whose meaning depends on it.

Applied to the three features:

| Feature | Gated | Open |
| --- | --- | --- |
| Custom-field definitions | `POST`, `PUT` on `/custom-fields` | `GET` single and list |
| Custom-field values | writing the bag (handler), filtering/sorting a list by one | reading the bag on every application |
| Multiple campaigns | `POST /campaigns` | `GET`, `PUT`, `DELETE` on `/campaigns`; placing an application in one held |

Filtering and sorting a list by a custom field is gated, and is the one case that looks like a read but is not: searching by custom fields is the capability itself, and the ungated list still returns every application.

## Consequences

- **A downgrade is never destructive.** Nothing is deleted, hidden, or silently dropped when an entitlement goes away. The account keeps every definition, every answer and every campaign; what stops is adding to them.
- **Gates are per-endpoint, not per-group.** Route groups here mix gated and open verbs, so `RequireAuthorization("Feature:<X>")` goes on the individual endpoint. A gate on a group is a bug waiting to be found by an account that has downgraded.
- **Some handlers read the entitlement on paths that do not use it.** The update-application handler resolves the custom-fields entitlement on every edit, including ones that carry no custom fields, because that is what decides whether an absent bag means "unchanged". The cost is one budgeted call through `IEntitlementQuery`.
- **The 403s are not reachable in the shipping product** — v1 sells a one-time lifetime unlock, so nothing downgrades on its own. They are reachable in tests, which seed the state directly, and they are what makes a refund, a revocation or a future subscription model safe to add.
- **A new paid feature has one question to answer**, not a design to invent: what is the act of acquisition here, and what must an account still be able to do with what it already acquired?

## Alternatives considered

- **Gate every route that touches the feature** — rejected. It is the simplest rule and it strands data: unlabelled values, undeletable campaigns, an account that cannot reduce itself to the free shape.
- **Reclaim on downgrade** (delete the extra campaigns, drop the answers) — rejected outright. The user entered that data; losing it because a plan changed is the kind of thing that is never forgiven, and it makes any future subscription model hostile.
- **Read-only enforced by hiding** (return the data but refuse to serve the definitions that explain it) — rejected; it is the same stranding with extra steps.
- **A single "is Pro" check at the edge of every module** — rejected. It re-answers the question ADR-0005 already settled, and it cannot express a gate that covers one field of one payload.
