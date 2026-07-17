# 0005 — Entitlements and mocked billing

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

The product is freemium (no ads): a generous Free tier and a Pro tier that unlocks depth (custom fields, full analytics, automated follow-ups, multiple campaigns, export). I want the entire freemium system — plan state, feature gates, the upgrade flow — to work end to end now, while deferring a real payment integration. The gates must be trustworthy (server-side), and swapping in a real provider later must touch as little as possible.

## Decision

- A **Billing** module owns plan state: `Free | Pro`, one row per user (a `Free` plan is created on `UserRegistered`), plus purchase records.
- **Entitlements are enforced as server-side authorization policies.** The Api defines feature policies (`Feature:CustomFields`, `Feature:FullAnalytics`, `Feature:FollowUpRules`, `Feature:MultipleCampaigns`, `Feature:Export`); a policy handler resolves the user's plan through `IEntitlementQuery` (Billing Contracts). Feature-gated commands re-check inside their handler as defence in depth. Entitlement is **never trusted from the client**.
- The payment charge sits behind an **`IBillingProvider`** interface. v1 ships a **mock implementation** plus a **dev-only "grant Pro" endpoint** that flips entitlement, so the full paid experience is testable without a payment processor.
- v1 monetization model: a **single one-time lifetime Pro unlock** — no tiers, no subscription, no trial.
- **Export** is Pro; **account deletion / data erasure** is Free and always available.

## Consequences

- The freemium experience is fully exercisable in development and tests today.
- Integrating a real provider (Stripe, RevenueCat, etc.) later is a single new `IBillingProvider` implementation plus a webhook that updates plan state — no changes to how features are gated.
- Because gates are policies over `IEntitlementQuery`, adding or moving a paid feature is a one-line policy change, and no module needs a direct reference to Billing internals.

## Alternatives considered

- **Client-side feature flags** — rejected; trivially bypassed and unsafe for anything that costs money.
- **Wiring a real payment provider up front** — rejected for v1; unnecessary for a non-commercial build and it would couple the whole app to a vendor before any of the product is proven.
