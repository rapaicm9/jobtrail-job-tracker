# 0002 — Platform and library baseline

- **Status:** Accepted
- **Date:** 2026-07-16

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
- **Background jobs:** Hangfire (LGPL v3, used unmodified) with PostgreSQL storage, plus Worker Service consumers.
- **Observability:** Serilog + OpenTelemetry over OTLP.

Re-verify versions and licences before each is first added.

## Consequences

- A little more hand-written boilerplate (manual handlers/mapping) in exchange for zero licensing exposure as the project grows.
- One version table via Central Package Management; nullable + `TreatWarningsAsErrors` + latest analyzers enforced from day one.
- PostgreSQL 18 specifically unlocks DB-side `uuidv7()` key generation and virtual generated columns, both of which we use.

## Alternatives considered

- **Mediator + AutoMapper defaults** — rejected on licensing and on the value of explicit, dependency-free handlers for a small codebase.
- **MongoDB / a document DB** for flexible data — rejected; PostgreSQL JSONB (see ADR-0004) covers the semi-structured need without a second datastore.
