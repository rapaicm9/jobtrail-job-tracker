# Jobspect

[![CI](https://github.com/rapaicm9/jobspect-job-tracker/actions/workflows/ci.yml/badge.svg)](https://github.com/rapaicm9/jobspect-job-tracker/actions/workflows/ci.yml)

A job-application tracker for people who are running a serious search. You record each
application yourself — there is no scraping and no external data source — move it through a
fixed pipeline, and hang the context around it: the company, the people you spoke to, the
interview rounds, the notes, the deadlines. On top of that sit trends and analytics,
reminders that fire in your own timezone, and fields you define yourself.

The backend is a .NET 10 modular monolith: five bounded contexts, one Postgres database with
one schema each, no module reading another's tables. It runs as two hosts (an HTTP API and a
background worker) orchestrated by Aspire locally and published as a Docker Compose topology.

---

## Contents

- [The product](#the-product)
- [Architecture](#architecture)
- [Repository layout](#repository-layout)
- [Stack](#stack)
- [API surface](#api-surface)
- [Cross-cutting behaviour](#cross-cutting-behaviour)
- [Getting started](#getting-started)
- [Development](#development)
- [Testing](#testing)
- [Deployment](#deployment)
- [Configuration reference](#configuration-reference)
- [Architecture decision records](#architecture-decision-records)
- [TO DO](#to-do)

---

## The product

### The pipeline

An application lives in exactly one stage. The first four are **active** and strictly
ordered; the last four are **terminal** outcomes with no order among themselves.

```
Applied → Screening → Interview → Offer     ┐
                                            ├→ Accepted | Rejected | Withdrawn | Ghosted
        (skips forward are allowed)         ┘
```

Moves are an explicit `POST /applications/{id}/transition`, never a patch of a stage field,
and every move is validated against the state machine inside the aggregate. Four kinds of
legal move are distinguished and logged as such: **advance** (to a strictly later active
stage), **terminal** (to an outcome), **reopen** (a closed application brought back to life),
and **reclassify** (correcting one outcome to another — the ghost that finally sends the
rejection). Anything else is a `422`.

### What you can record

| | |
| --- | --- |
| **Applications** | Role, company, compensation (amount + currency), location, work mode, posting URL, source/channel, application deadline, applied date, CV and cover-letter labels. All first-class columns, so all of them filter, sort and chart. |
| **Companies** | Created inline when you file an application, or picked from a type-ahead over the ones you already have. |
| **Contacts** | People at a company, with a role, reachable from the applications they relate to. |
| **Interviews** | Rounds with a scheduled instant, attached to an application. |
| **Activity** | Notes you write, plus an automatic log of every stage change — one merged timeline per application. |
| **Custom fields** | Your own fields: text, number, date, checkbox, single-select, multi-select, URL. Answers are filterable and sortable, and the chartable types can be charted. |
| **Campaigns** | Separate searches with separate figures. Every account gets one by default; more than one is a Pro capability. |
| **Analytics** | An overview, an insights breakdown, per-custom-field charts, and a weekly application goal. |
| **Reminders** | Interview alerts (the morning before, an hour before), application-deadline alerts (three days before, the morning of), offer-decision alerts (three days before, the day before, the morning of), and the Pro follow-up rule — "this has sat in Applied for *n* days with no answer". |

### Free and Pro

Freemium, no ads. Free is generous core tracking. Pro is a **one-time unlock** — the payment
provider is mocked behind an `IBillingProvider` seam and no real money moves — and covers five
named capabilities: `CustomFields`, `FullAnalytics`, `FollowUpRules`, `MultipleCampaigns`,
`Export`. The Billing module is showcase only, there are no plans to ever commercialize this app.

Entitlements are named for the feature, not the tier, and enforced as server-side
authorization policies (`Feature:*`) that resolve through a Billing contract, re-checked
inside the handler as defence in depth. They gate **acquisition, not possession**: an
entitlement gates the act that creates or grows what it pays for, never reading data the
account already holds, and never the operations that reduce or maintain it. An account that
lapses can always read, rename, archive and delete what it recorded. Account deletion and
data erasure are free and always available.

---

## Architecture

**One module = one bounded context = one Postgres schema = one `DbContext`.** No module reads
another module's tables; there are no cross-schema queries and no joins across a boundary.
Everything synchronous crosses through a module's `Contracts` assembly, where the
implementations are `internal`; everything asynchronous crosses as an event.

| Module | Schema | Owns |
| --- | --- | --- |
| **Identity** | `identity` | Accounts, credentials, the refresh-token store, timezone, account export and erasure. |
| **Applications** | `applications` | The core aggregate and its pipeline, plus campaigns, companies, contacts, interviews, activity and custom fields. |
| **Analytics** | `analytics` | A read model built from Applications' events. Consumer only. |
| **Notifications** | `notifications` | The reminder engine: arming, sweeping, retracting, the feed, the follow-up rule. Consumer only. |
| **Billing** | `billing` | Plans and entitlements, the `Feature:*` policies, the mocked provider. |

Inside a module the unit of work is a **vertical slice**: one folder per feature holding the
endpoint, its request and response, the handler and the validator. There is no MediatR and no
AutoMapper — handlers are plain DI-registered types and mapping is hand-written extension
methods.

### How modules talk

Nothing calls Analytics or Notifications; they consume. Applications publishes integration
events through a **transactional outbox** — written in the same transaction as the state
change, dispatched afterwards, marked processed only once every handler has succeeded, and
retried with backoff otherwise. Events whose loss has no consequence take the in-process path
instead; which path an event took is invisible to its handlers.

Delivery is **at-least-once and explicitly unordered**. Events therefore state whole facts
rather than deltas (a stage change carries both ends of the move), consumers dedupe on the
event's own id, and per-aggregate state applies last-write-wins on the domain instant the
publisher stamped rather than on arrival order.

The three synchronous cross-boundary reads are all narrow and all through `Contracts`:
Billing's entitlement query, Identity's profile query (the worker reads a timezone and
nothing else), and Applications' custom-field bucket query — which returns *aggregates*, so
individual answers never leave the module.

### Hosts

| Host | Role |
| --- | --- |
| `Jobspect.Api` | The HTTP surface. Composes all five modules, owns the cross-cutting middleware, runs the outbox dispatcher, issues and validates tokens. |
| `Jobspect.Worker` | Background work. Runs the Quartz schedule that fires reminders. No HTTP surface, no dispatcher — one table, one writer, one reader. |
| `Jobspect.MigrationService` | A one-shot that applies every module's migrations and exits. Both hosts wait for it, so neither can start against a database a migration behind. Nothing migrates on startup. |
| `Jobspect.AppHost` | Aspire orchestration for the dev loop, and the source of the published Compose topology. |

### Enforced, not aspirational

The boundary rules above are asserted by an ArchUnitNET suite that runs in CI: module
implementations stay internal, no module references another module's implementation
assembly, no `DbContext` reaches outside its schema, the banned libraries stay banned.
A change that appears to need a boundary broken needs the design revisited, not the test.

---

## Repository layout

```
Jobspect.AppHost/              Aspire orchestration + Compose publish target
docs/
  adr/                         Architecture decision records (see index below)
  openapi/openapi.yaml         The committed API contract
src/
  Jobspect.Api/                HTTP host: composition root and cross-cutting middleware
  Jobspect.Worker/             Background host: the reminder schedule
  Jobspect.MigrationService/   One-shot migration runner
  Jobspect.Infrastructure/     Event bus, outbox, shared persistence helpers
  Jobspect.SharedKernel/       Event abstractions, cursor + paged envelope
  Jobspect.ServiceDefaults/    OpenTelemetry, health checks, resilience, discovery
  Modules/<Module>/
    Jobspect.Modules.<Module>/           Domain, Features (vertical slices), Persistence
    Jobspect.Modules.<Module>.Contracts/ The only surface other modules may reference
tests/
  Jobspect.ArchitectureTests/  The boundary rules, executable
  Jobspect.IntegrationTests/   Real Postgres + Redis via Testcontainers
  Jobspect.Modules.*.Tests/    Per-module unit tests
  Jobspect.Infrastructure.Tests/, Jobspect.SharedKernel.Tests/
```

---

## Stack

| | |
| --- | --- |
| **Runtime** | .NET 10 (`net10.0`), C# 14. SDK pinned in `global.json`. |
| **Web** | ASP.NET Core 10 Minimal APIs — no controllers. `TypedResults` unions, built-in validation, URL-segment versioning, OpenAPI 3.1. |
| **Data** | EF Core 10 + Npgsql 10 over PostgreSQL 18. UUIDv7 keys generated DB-side; snake_case columns via `EFCore.NamingConventions`. |
| **Cache** | Redis (Valkey-compatible) via StackExchange.Redis — the idempotency replay cache and the Data Protection key ring today; Streams back the push-delivery queue when that channel arrives. |
| **Background** | Quartz.NET with a persistent ADO job store on PostgreSQL. |
| **Orchestration** | Aspire 13 AppHost; `aspire publish` renders Docker Compose from the graph, on demand rather than into the repository. |
| **Observability** | OpenTelemetry over OTLP; the Aspire dashboard locally and in the published topology. |
| **Testing** | xUnit v3, Shouldly, ArchUnitNET, Testcontainers. Never the EF in-memory provider. |

Licensing stance: **MIT / first-party only**.

The build is strict from commit one: nullable enabled, `TreatWarningsAsErrors`, analyzers at
`latest`, code style enforced in the build. That makes `CA1848` an error, which is why every
log call is a source-generated `[LoggerMessage]` partial method.

---

## API surface

Every route is versioned (`/api/v1/…`) and scoped to the calling account. **A resource owned
by another user is reported as absent, not as forbidden** — the ownership check lives inside
the query, so `404` is the honest answer and `403` would leak existence. Every error is
RFC 9457 ProblemDetails; stack traces and EF exceptions never leave the host.

| Resource | Endpoints |
| --- | --- |
| Identity | `POST /identity/register`, `/login`, `/refresh`, `/logout`, `/logout-all` |
| Account | `GET`/`PUT`/`DELETE /account`, `GET /account/export` |
| Billing | `GET /billing/plan`, `POST /billing/purchase` |
| Applications | `GET`/`POST /applications`, `GET`/`PUT /applications/{id}`, `POST /applications/{id}/transition` |
| Companies | `GET /companies` (type-ahead) |
| Contacts | `GET`/`POST /contacts`, `GET`/`PUT /contacts/{id}` |
| Interviews | `GET`/`POST /applications/{applicationId}/interviews`, `GET`/`PUT /…/interviews/{interviewId}` |
| Activity | `GET`/`POST /applications/{applicationId}/activity` |
| Custom fields | `GET`/`POST /custom-fields`, `GET`/`PUT /custom-fields/{id}` |
| Campaigns | `GET`/`POST /campaigns`, `GET`/`PUT`/`DELETE /campaigns/{id}` |
| Analytics | `GET /analytics/overview`, `/analytics/insights`, `/analytics/custom-fields/{definitionId}`, `GET`/`PUT`/`DELETE /analytics/goal` |
| Reminders | `GET /reminders`, `GET /reminders/unread-count`, `POST /reminders/{id}/dismiss`, `GET`/`PUT`/`DELETE /reminder-rule` |

### The contract

`docs/openapi/openapi.yaml` is tracked, and it is generated rather than written: the
integration suite boots the real host and writes what that host serves. A test then holds the
two together, so a contract change is a visible diff in a pull request and a forgotten
regeneration is a red build. Regenerate deliberately:

```bash
JOBSPECT_WRITE_OPENAPI=1 dotnet test tests/Jobspect.IntegrationTests
```

In Development the document is also served live at `/openapi/v1.json` and `/openapi/v1.yaml`,
with a reference UI at **`/scalar`** that drives the real API. Neither is mapped outside
Development — the document describes whatever the host maps, including developer-only
shortcuts, so there is no environment where an unauthenticated caller can read it.

Numbers are strict: unlike the ASP.NET Core web defaults, a quoted number in a JSON body is
refused. That keeps one type per field in the contract instead of widening every integer to
"integer or string" and propagating that into every generated client.

---

## Cross-cutting behaviour

**Authentication.** ASP.NET Core Identity for the user store and password hashing; self-issued
tokens for API access. The access token is a short-lived JWT signed with **ES256** so any
future service validates with the public key alone, carrying no PII — an opaque subject, a
token version and minimal scopes. The refresh token is an opaque 256-bit value stored hashed,
one row per device, rotated on every use; presenting a retired token revokes its whole family.
Per-device logout deletes a row; global logout bumps the token version. Password policy is
eight characters with upper, lower, digit and special.

**Entitlements.** `Feature:*` authorization policies resolved through Billing's contract and
re-checked in the handler. Never trusted from the client.

**Idempotency.** Any authenticated `POST` honours an `Idempotency-Key` header, backed by a
per-caller replay cache in Redis. The key is claimed atomically, so two simultaneous requests
cannot both proceed; the record carries a fingerprint of method, path and body, so a key
reused for a *different* request is refused rather than answered with the first one's result.
`PUT` and `DELETE` are idempotent by construction and take no key. If Redis is unreachable the
request fails rather than silently running without the guarantee it asked for.

**Pagination.** Keyset cursors, one envelope shared by every paged list. Each list sorts by
its natural key and then by id, so nothing is repeated or skipped at a page edge. The cursor
is opaque by contract, validated against the list it is used on, and backed by a composite
index on exactly the path it walks.

**Reminders.** A reminder row is one *armed instant*, computed in UTC from the owner's stored
IANA timezone. Arming and retraction are driven by the date-bearing events, and every row
carries the occurrence time of the event that decided it, so a redelivered or out-of-order
event cannot resurrect a reminder for a round that has moved or an application that has
closed. Handlers are idempotent, and the second delivery is refused by a database constraint
rather than by a check that might race.

**Edge hardening.** Forwarded headers first in the pipeline, so everything downstream sees the
real client address; strict security headers (`default-src 'none'`, with a single scoped
exception for the dev-only reference page); an exact-origin CORS allowlist; the Data
Protection key ring in Redis; no `Server` banner. Per-IP rate limits, with a stricter fixed
window on the auth surface, applied *before* authentication so a throttled request never pays
for a token validation.

**Privacy.** Tokens and raw request bodies are never logged — bodies carry users' free-text
notes. Account export gathers every module's data through its own contract; erasure is
recorded in an outbox in the same transaction that accepts it and delivered until every
module has confirmed, so it survives a crash mid-erasure.

**Observability and health.** OpenTelemetry traces, metrics and logs over OTLP from both
hosts. `/health/ready` and `/health/live` on the API, mapped in every environment for the
proxy and the orchestrator, answering with a bare status and no check detail.

---

## Getting started

### Prerequisites

- **.NET 10 SDK** — `10.0.301` or the latest patch of that band (pinned in `global.json`).
- **Docker** — Aspire provisions Postgres and Redis as containers, and the integration tests
  need a running daemon.
- Optionally the **Aspire CLI**, if you prefer `aspire run` to `dotnet run`.

### 1. Provide the token signing keypair

The API signs and validates access tokens with an ES256 (P-256) keypair. It is an input to a
deployment, never a build artefact and never committed, and the AppHost declares both halves
as secret parameters. Locally they resolve from the AppHost's own user-secrets store.

Mint a pair:

```bash
openssl ecparam -name prime256v1 -genkey -noout | openssl pkcs8 -topk8 -nocrypt -out jwt.key
openssl ec -in jwt.key -pubout -out jwt.pub
```

Store it (from the repository root):

```bash
dotnet user-secrets --project Jobspect.AppHost set "Parameters:identity-jwt-private-key" "$(cat jwt.key)"
dotnet user-secrets --project Jobspect.AppHost set "Parameters:identity-jwt-public-key"  "$(cat jwt.pub)"
rm jwt.key jwt.pub
```

The same thing in PowerShell, where the multi-line value needs reading as one string:

```powershell
dotnet user-secrets --project Jobspect.AppHost set "Parameters:identity-jwt-private-key" (Get-Content jwt.key -Raw)
dotnet user-secrets --project Jobspect.AppHost set "Parameters:identity-jwt-public-key"  (Get-Content jwt.pub -Raw)
Remove-Item jwt.key, jwt.pub
```

One pair per environment — a pair reused from another deployment would let its tokens
authenticate here. Rotating the pair invalidates every access token in circulation but no
refresh token, so clients recover on their next rotation without being logged out.

### 2. Run it

```bash
dotnet run --project Jobspect.AppHost
```

Aspire starts Postgres (with a data volume, so your dev data survives a restart) and Redis,
runs the migration service to completion, then launches the API and the worker. Postgres and
Redis credentials are generated and persisted to the AppHost user-secrets store — nothing
lands in a tracked file.

| | |
| --- | --- |
| Aspire dashboard | printed on startup |
| API | `https://localhost:7065` (and `http://localhost:5001`) |
| Reference UI | `https://localhost:7065/scalar` |
| Contract | `/openapi/v1.yaml`, `/openapi/v1.json` |

Register through `/api/v1/identity/register`, then exercise anything from the reference UI.
In Development, `POST /api/v1/billing/dev/grant-pro` unlocks the Pro capabilities without a
purchase — it is mapped in Development only, which is what keeps it out of production
entirely.

---

## Development

```bash
dotnet build                                # analyzers run here; warnings are errors
dotnet format                               # style is enforced, not suggested
dotnet format --verify-no-changes           # what CI checks
dotnet test                                 # unit + integration + architecture (Docker required)
```

Adding a migration — each module owns its own, in its own schema:

```bash
dotnet ef migrations add <Name> \
  --project src/Modules/<Module>/Jobspect.Modules.<Module> \
  --context <Module>DbContext \
  -o Persistence/Migrations
```

Migrations are applied by the migration service at deploy time, never by `Database.Migrate()`
on startup — two instances racing to alter one schema is the failure that shape exists to
prevent.

### CI

`.github/workflows/ci.yml` runs on every pull request and every push to `main`: restore,
build in Release, `dotnet format --verify-no-changes`, then the full test suite against real
containers. A failing test fails the gate. Coverage is information, not a gate.

---

## Testing

| Suite | What it covers |
| --- | --- |
| `Jobspect.Modules.*.Tests` | Domain and handler logic per module. The pipeline state machine is covered exhaustively — every legal and illegal transition, including the "Applied → terminal" jump people actually make. |
| `Jobspect.IntegrationTests` | The real host against real PostgreSQL and Redis through Testcontainers, including the contract gate and the event-dispatch recovery paths. |
| `Jobspect.ArchitectureTests` | The boundary rules, as executable assertions. |
| `Jobspect.Infrastructure.Tests`, `Jobspect.SharedKernel.Tests` | The outbox, the event bus, the cursor codec. |

No test makes a live external call — the push provider is a fake, and the billing provider is
mocked by design.

---

## Deployment

The published artefacts are containers. `aspire publish` renders the AppHost graph into
`Jobspect.AppHost/aspire-output/`: the dashboard, Postgres, Redis, the migration one-shot, the
API and the worker, wired together with the migration as a completion gate on both hosts.

**The topology is generated, not committed.** `AppHost.cs` is the source of truth and the output
directory is git-ignored — a checked-in copy would be a snapshot that goes stale the first time a
resource is added, with no gate to catch it. Publish before you deploy; don't hand-edit the result.

Publishing writes an `.env` beside the topology listing every variable it references, with the
values left empty. That file is the checklist: image references, the API port, the Postgres and
Redis passwords, and the ES256 keypair (minted as above — one pair per environment, never reused
between them). Fill it in on the deployment host, where it stays; it is git-ignored, and the
secrets in it belong to that host alone.

Both key values are multi-line PEM and Compose reads `.env` line by line, so either quote them
onto one line or supply the two variables to the Compose process from the host's own secret store.

The API expects to sit behind a reverse proxy: forwarded headers are enabled, and the
liveness and readiness endpoints are there for the proxy and the orchestrator to poll.

---

## Configuration reference

Both hosts read connection strings named `jobspect` (PostgreSQL) and `cache` (Redis); Aspire
injects them locally and Compose supplies them in a deployment.

| Key | Meaning |
| --- | --- |
| `ConnectionStrings__jobspect` | PostgreSQL. |
| `ConnectionStrings__cache` | Redis. |
| `Identity__Jwt__PrivateKeyPem` | ES256 private key, PEM. API only. |
| `Identity__Jwt__PublicKeyPem` | ES256 public key, PEM. API only. |
| `Cors__AllowedOrigins` | Exact-origin allowlist for the browser client. |
| `RateLimiting__GlobalPermitLimit` | Per-IP sliding window budget (default 100/min; raised in Development). |
| `RateLimiting__AuthPermitLimit` | Fixed window budget on the auth surface (default 10/min). |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Telemetry collector. |

---

## Architecture decision records

`docs/adr/` is the decision record, and the reasoning behind anything surprising above lives
there. One record per topic, amended in place; numbers are retired rather than reused, so gaps
are expected.

| | |
| --- | --- |
| [0000](docs/adr/0000-record-architecture-decisions.md) | Record architecture decisions |
| [0001](docs/adr/0001-modular-monolith-vertical-slices.md) | Modular monolith with vertical slices and enforced boundaries |
| [0002](docs/adr/0002-platform-and-library-baseline.md) | Platform and library baseline |
| [0003](docs/adr/0003-authentication-and-token-model.md) | Authentication and token model |
| [0004](docs/adr/0004-custom-field-storage.md) | Custom field storage and querying |
| [0005](docs/adr/0005-entitlements-and-mocked-billing.md) | Entitlements and mocked billing |
| [0006](docs/adr/0006-reminder-scheduling-and-delivery.md) | Reminder scheduling and delivery |
| [0007](docs/adr/0007-testing-stack.md) | Testing stack |
| [0008](docs/adr/0008-list-pagination.md) | List pagination |
| [0009](docs/adr/0009-durable-event-delivery.md) | Durable event delivery |
| [0011](docs/adr/0011-idempotent-mutations.md) | Idempotent mutations |
| [0016](docs/adr/0016-analytics-read-model.md) | Analytics read model |
| [0017](docs/adr/0017-custom-field-charts-across-the-boundary.md) | Custom-field charts across the module boundary |
| [0018](docs/adr/0018-the-committed-api-contract.md) | The committed API contract and client generation |

---

## TO DO

The backend is the whole repository today. Two first-party clients are next, in this order —
both generate from the committed contract rather than hand-rolling models against it.

- [ ] **Next.js web client — coming soon.** Types generated with `openapi-typescript`, calls
      through `openapi-fetch`. The browser holds no token: the Next.js server keeps them and
      the browser gets an HttpOnly, Secure, SameSite cookie.
- [ ] **Flutter mobile client — coming soon.** Tokens in the OS keystore, push delivery for
      reminders behind the existing provider seam. Its code generator is deliberately an open
      question until the client starts.

Also queued behind those:

- [ ] A push-delivery provider implementation — the seam exists, the mobile client is what
      gives it a consumer.
- [ ] Health endpoints exposed from the worker host; the checks are registered already, but
      the host has no HTTP listener yet.
- [ ] Passkeys (WebAuthn/FIDO2) as an additional factor, with recovery codes issued at
      registration.
