# 0002 — Platform and library baseline

- **Status:** Accepted
- **Date:** 2026-07-16
- **Amended:** 2026-07-31 — background jobs are Quartz.NET, not Hangfire; the stated licensing stance and the job library had contradicted each other since this record was written. See *Revision history*.

## Context

The stack needs to stay supported for years, avoid recurring cost while pre-revenue, and dodge the wave of .NET libraries that moved to commercial licensing in 2025–2026. Library choices made casually now become licensing liabilities at scale.

## Decision

- **Runtime:** .NET 10 (`net10.0`), C# 14 — LTS, supported to Nov 2028.
- **Web:** ASP.NET Core 10 **Minimal APIs** (no controllers), built-in validation, OpenAPI 3.1, `TypedResults`.
- **Data:** EF Core 10 + `Npgsql.EntityFrameworkCore.PostgreSQL` 10 over **PostgreSQL 18**; Redis (Valkey-compatible) for cache, dedup, rate limiting, and the worker queue.
- **Orchestration:** Aspire 13 AppHost for the dev loop and Docker Compose publishing.
- **Licensing stance — MIT / first-party only.** Explicitly **do not** take a dependency on MediatR, AutoMapper, MassTransit v9, or FluentAssertions ≥ 8. Instead:
  - request dispatch = plain, DI-registered handler classes;
  - mapping = hand-written extension methods;
  - messaging (MVP) = Redis Streams + a transactional outbox; if a broker is ever needed, Wolverine (MIT) then Rebus (MIT);
  - assertions = Shouldly / AwesomeAssertions.
- **Background jobs:** **Quartz.NET (Apache 2.0)** with a persistent ADO job store on PostgreSQL, plus Worker Service consumers. Originally Hangfire, revised 2026-07-31 — see *Revision history*.
- **Observability:** Serilog + OpenTelemetry over OTLP.

Re-verify versions and licences before each is first added.

## Consequences

- A little more hand-written boilerplate (manual handlers/mapping) in exchange for zero licensing exposure as the project grows.
- One version table via Central Package Management; nullable + `TreatWarningsAsErrors` + latest analyzers enforced from day one.
- PostgreSQL 18 specifically unlocks DB-side `uuidv7()` key generation and virtual generated columns, both of which we use.

## Alternatives considered

- **Mediator + AutoMapper defaults** — rejected on licensing and on the value of explicit, dependency-free handlers for a small codebase.
- **MongoDB / a document DB** for flexible data — rejected; PostgreSQL JSONB (see ADR-0004) covers the semi-structured need without a second datastore.
- **Hangfire for background jobs** — the original choice, rejected 2026-07-31 before it was ever added. Its core and its PostgreSQL storage provider are both LGPL v3 (or a paid commercial subscription), which made it the only copyleft dependency in a stack whose stance bullet above reads "MIT / first-party only" and whose stated consequence is "zero licensing exposure". This record carried that contradiction as an explicit carve-out — *"LGPL v3, used unmodified"* — which is legally sound for a NuGet reference and is still an exception this stack does not otherwise make. Quartz.NET is Apache 2.0 and needs no carve-out. Its dashboard is the real loss; see ADR 0006.

## Revision history

- **2026-07-16 — original.** The pinned runtime, web, data and orchestration stack; the MIT/first-party licensing stance and the four libraries it rules out.
- **2026-07-31 — background jobs are Quartz.NET.** Triggered by this record's own instruction to *re-verify versions and licences before each is first added*, at the point Sprint 10 was about to add one. Hangfire's core and its PostgreSQL storage are LGPL v3; the stance bullet two lines above says MIT/first-party only. The carve-out was deliberate when written and is simply unnecessary — Apache 2.0 buys the same capability with no exception to explain. Nothing else in the stack moved.
