# 0001 — Modular monolith with vertical slices and enforced boundaries

- **Status:** Accepted
- **Date:** 2026-07-16

## Context

Greenfield backend, one developer, no traffic yet. I want the low operational cost of a monolith (one deploy pipeline, no distributed-systems tax) without painting myself into a corner if one part ever needs to scale or be extracted independently. Starting with microservices would mean spending the early weeks on contracts, pipelines, and network boundaries instead of on the product.

## Decision

Build a **modular monolith** with **vertical-slice** feature organization and **enforced module boundaries**, split into exactly two processes:

- `JobTrail.Api` — the ASP.NET Core host composing all modules in-process.
- `JobTrail.Worker` — a separate process for time-driven and delivery work (reminders, push).

Modules are bounded contexts, each owning its own PostgreSQL **schema** and `DbContext`:

- **Identity**, **Applications** (the core: aggregate + pipeline, campaigns, companies, contacts, interviews, custom fields), **Analytics** (read-model), **Notifications** (reminders), **Billing** (entitlements).

Rules that make the boundaries real:

- No module queries another module's tables — no cross-schema joins, not even reads.
- Inter-module calls go only through a small `*.Contracts` project (interfaces + DTOs); implementations are `internal`.
- Async coupling is via domain/integration events; read-side modules (Analytics, Notifications) consume events rather than being called.
- Inside a module, organize by feature (endpoint + request/response + handler + validator per slice), not by technical layer.
- **Architecture tests** assert all of the above, so the rules are executable rather than aspirational.

## Consequences

- Boundary discipline costs a little ceremony now (Contracts projects, events) and saves a lot later: a module can be extracted to its own service as a mostly-mechanical operation because its schema, contracts, and events already isolate it.
- The architecture-test suite must be kept green; a boundary violation is a failed build, not a review nit.
- One deploy, one database, shared observability — cheap to run on a single VPS.

## Alternatives considered

- **Microservices from day one** — rejected as premature; all cost, no benefit at this scale.
- **Layered monolith** (Domain/Application/Infrastructure across the whole app) — rejected; weak isolation between features, and refactors ripple across layers instead of staying in a slice.
