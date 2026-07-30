# 0005 — Entitlements and mocked billing

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-07-30 — "never gate reading" was too broad as written; a capability may be expressed as a read. See *Revision history*.

## Context

The product is freemium (no ads): a generous Free tier and a Pro tier that unlocks depth (custom fields, full analytics, automated follow-ups, multiple campaigns, export). I want the entire freemium system — plan state, feature gates, the upgrade flow — to work end to end now, while deferring a real payment integration. The gates must be trustworthy (server-side), and swapping in a real provider later must touch as little as possible.

*How* to enforce a gate is the easy half. *What* the gate covers is a separate question, and the naive reading — "gate every route that touches the feature" — turned out to be wrong every time it was tried:

- **Custom-field definitions.** Gating the whole `/custom-fields` group would stop an unentitled account from *reading* its own definitions. Its recorded values are kept and still returned, so a client that cannot fetch the definitions cannot label what it is showing: the answers become unlabelled numbers and strings.
- **Custom-field values.** They are one part of the application payload, and both tiers create and edit applications. There is no route to gate. Worse, if an unentitled edit had simply dropped the bag, an account would drain its own answers away one ordinary edit at a time.
- **Multiple campaigns.** Gating the campaign endpoints would leave an account holding campaigns it could not rename, could not read, and — the real trap — could not delete. Deleting a campaign reassigns its applications to the default, so it is the only way back to a single campaign. Gate it and the account is stuck in a shape it can no longer reduce.

The pattern behind all three is that the entitlement buys the capability to *acquire* something, and the data acquired under it outlives the entitlement.

## Decision

### Plan state and enforcement

- A **Billing** module owns plan state: `Free | Pro`, one row per user (a `Free` plan is created on `UserRegistered`), plus purchase records.
- **Entitlements are enforced as server-side authorization policies.** The Api defines feature policies (`Feature:CustomFields`, `Feature:FullAnalytics`, `Feature:FollowUpRules`, `Feature:MultipleCampaigns`, `Feature:Export`); a policy handler resolves the user's plan through `IEntitlementQuery` (Billing Contracts). Feature-gated commands re-check inside their handler as defence in depth. Entitlement is **never trusted from the client**.
- The payment charge sits behind an **`IBillingProvider`** interface. v1 ships a **mock implementation** plus a **dev-only "grant Pro" endpoint** that flips entitlement, so the full paid experience is testable without a payment processor.
- v1 monetization model: a **single one-time lifetime Pro unlock** — no tiers, no subscription, no trial.
- **Export** is Pro; **account deletion / data erasure** is Free and always available.

### What a gate covers

**An entitlement gates acquisition, not possession.** Concretely:

- **Gate the act that creates or grows what the entitlement pays for** — defining a field, recording an answer, opening a second campaign. That act is the capability.
- **Never gate reading data the account holds.** Anything an account has recorded stays fully readable, on its own endpoints and inside every payload that already carried it. This is not a courtesy: without it a client cannot render what it is still storing. What may be gated is a *capability* that happens to be expressed as a read — the distinction is whether refusing it withholds the user's own record, or only work performed over it.
- **Never gate the operations that reduce or maintain what is already held** — renaming, archiving, deleting, reassigning. An account must always be able to get back to a shape the free tier allows. A gate that traps data is worse than no gate.
- **Where the gated thing is part of a payload rather than a whole call, the check moves into the handler** and covers that part only. Sending it unentitled is a **403** (`ErrorType.Forbidden` — a known caller and an understood request that is not permitted); omitting it leaves what is stored untouched.
- **Absent means "unchanged", never "cleared", for a gated portion of a full-replace request.** This is the rule that makes retention real, and it means the entitlement is read on *every* such request — the absent case is precisely the one whose meaning depends on it.

Applied to the features that exist:

| Feature | Gated | Open |
| --- | --- | --- |
| Custom-field definitions | `POST`, `PUT` on `/custom-fields` | `GET` single and list |
| Custom-field values | writing the bag (handler), filtering/sorting a list by one | reading the bag on every application |
| Multiple campaigns | `POST /campaigns` | `GET`, `PUT`, `DELETE` on `/campaigns`; placing an application in one held |
| Full analytics | `GET /analytics/insights`, `GET /analytics/custom-fields/{id}` | `GET /analytics/overview`; every application endpoint; reading custom-field definitions |

**Two things look like reads and are not**, and both are gated:

- **Filtering and sorting a list by a custom field.** Searching by custom fields is the capability itself, and the ungated list still returns every application.
- **The paid dashboard.** The funnel, the rates, the time metrics, the trend and the breakdowns are analysis performed over the account's record, not the record itself. Refusing it withholds no application: `/applications` returns everything, and `/analytics/overview` still gives the pipeline snapshot and the total. What the paid tier sells is the work, and a capability priced as work is gated wherever it is invoked from.

The test both cases pass and the rule turns on: **can the account still see everything it recorded?** If yes, gating the computation is a price on the analysis. If no, the gate has become a lock on the user's own data, and the rule above forbids it.

## Consequences

- The freemium experience is fully exercisable in development and tests today.
- Integrating a real provider (Stripe, RevenueCat, etc.) later is a single new `IBillingProvider` implementation plus a webhook that updates plan state — no changes to how features are gated.
- Because gates are policies over `IEntitlementQuery`, adding or moving a paid feature is a one-line policy change, and no module needs a direct reference to Billing internals.
- **A downgrade is never destructive.** Nothing is deleted, hidden, or silently dropped when an entitlement goes away. The account keeps every definition, every answer and every campaign; what stops is adding to them.
- **Gates are per-endpoint, not per-group.** Route groups here mix gated and open verbs, so `RequireAuthorization("Feature:<X>")` goes on the individual endpoint. A gate on a group is a bug waiting to be found by an account that has downgraded.
- **Some handlers read the entitlement on paths that do not use it.** The update-application handler resolves the custom-fields entitlement on every edit, including ones that carry no custom fields, because that is what decides whether an absent bag means "unchanged". The cost is one budgeted call through `IEntitlementQuery`.
- **The 403s are not reachable in the shipping product** — v1 sells a one-time lifetime unlock, so nothing downgrades on its own. They are reachable in tests, which seed the state directly, and they are what makes a refund, a revocation or a future subscription model safe to add.
- **A new paid feature has one question to answer**, not a design to invent: what is the act of acquisition here, and what must an account still be able to do with what it already acquired?

## Alternatives considered

- **Client-side feature flags** — rejected; trivially bypassed and unsafe for anything that costs money.
- **Wiring a real payment provider up front** — rejected for v1; unnecessary for a non-commercial build and it would couple the whole app to a vendor before any of the product is proven.
- **Gate every route that touches the feature** — rejected. It is the simplest rule and it strands data: unlabelled values, undeletable campaigns, an account that cannot reduce itself to the free shape.
- **Reclaim on downgrade** (delete the extra campaigns, drop the answers) — rejected outright. The user entered that data; losing it because a plan changed is the kind of thing that is never forgiven, and it makes any future subscription model hostile.
- **Read-only enforced by hiding** (return the data but refuse to serve the definitions that explain it) — rejected; it is the same stranding with extra steps.
- **A single "is Pro" check at the edge of every module** — rejected. It cannot express a gate that covers one field of one payload, and it would put the plan model in front of every module instead of behind `IEntitlementQuery`.

## Revision history

- **2026-07-16 — original.** Plan state, the policy mechanism, the mocked provider.
- **2026-07-28 — what a gate covers** *(recorded separately at the time as ADR 0015)*. Added after custom-field definitions, custom-field values and multiple campaigns had each answered the question differently and each answer had to be argued from scratch. The acquisition-not-possession rule, and the table above, are what those three arguments have in common.
- **2026-07-30 — "never gate reading" was too broad, and the paid dashboard is what showed it.** As written, the rule forbade gating the Pro analytics endpoints — which contradicts the tier the product sells and would leave `Feature:FullAnalytics` with nothing to do. The record already carried the resolution as a footnote about custom-field filtering, described there as "the one case that looks like a read but is not"; full analytics makes it two, which is enough to promote it from an exception to a rule. Restated: an entitlement never gates reading *data the account holds*, but may gate a capability that happens to be expressed as a read. The test is whether refusing it withholds the user's own record — `/applications` and `/analytics/overview` stay open, so what the gate prices is the analysis rather than the data.
