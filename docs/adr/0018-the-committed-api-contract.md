# 0018 — The committed API contract and client generation

- **Status:** Accepted
- **Date:** 2026-08-05

## Context

Two first-party clients are coming — a Next.js web app, then a Flutter mobile app — and both need to know the shape of this API. Hand-written client models drift from the server the first time a response gains a field, and the drift is discovered by a user rather than by a build.

The obvious answer is to generate the clients from an OpenAPI description, which raises three questions that have to be answered together: where the description comes from, what stops it going stale, and what generates from it. The third constrains the first, because a generator that cannot read the description this stack produces would force the description to be produced differently.

The description is generated from a live endpoint graph. That is the property that makes it trustworthy — it cannot describe a route that isn't mapped — and also the property that makes committing it awkward: a generated artefact under version control is a copy, and copies rot.

## Decision

**`docs/openapi/openapi.yaml` is tracked, produced from the running host by the integration suite, and held to the host by a test.**

- **The document is served in Development only**, from the built-in generator, at `/openapi/{documentName}.json` and `.yaml`. It reports whatever the host maps, including the developer-only shortcuts, so there is no environment where an unauthenticated caller can read it.

- **One document per API version.** The routes carry the version as a route parameter (`/api/v{version:apiVersion}`), which the generator alone renders as a literal `{version}` path — a path no client can call. The API explorer resolves it to the concrete version, so the description reads `/api/v1/…` as a caller would type it.

- **The committed copy is written by a test in the integration suite**, which already boots the real host against real containers. `JOBSPECT_WRITE_OPENAPI=1` rewrites the file; without it the test asserts the committed copy equals what the host serves. The gate therefore costs no new infrastructure — CI runs this suite already — and the artefact comes from the same endpoint the clients read, not from a second code path that could describe something else.

  Build-time generation was the alternative and does not fit: .NET 10 emits YAML only from the served endpoint, and its build-time generator boots the host under a mock server, which for this app means composing five modules with no connection strings present.

- **Integers are described as integers.** ASP.NET Core's web defaults accept a quoted number in a JSON body, which the description faithfully renders as `type: [integer, string]` — and from there into every generated client, at every numeric field. `NumberHandling` is set to `Strict` instead: the API refuses the quoted form and the contract states one type per field. Query and route values are bound before serialization and are unaffected.

- **Operations are tagged by resource, taken from the path.** Generators name client classes after tags, and the default tag is the name of the class declaring the endpoint — which, in a codebase of one endpoint per class, would produce a client of forty-odd single-method services instead of one per resource.

- **The document names no server.** The generator fills `servers` from the request that fetched it, so the same contract would describe a different origin on every host, and — the local port being assigned per run — on every run. Empty means "wherever this document came from", which is true everywhere and is what makes the artefact reproducible enough to diff.

- **The web client generates types with `openapi-typescript` and calls through `openapi-fetch`.** Both MIT. Types-only generation means there is no generated runtime to review, and the pair reads OpenAPI 3.1 natively, so the committed document needs no post-processing step between the server and the client.

  This suits the token model: the browser never holds a token, the Next.js server does (ADR 0003), so what the client needs is a typed `fetch` inside a route handler rather than a framework-shaped SDK. The mobile client's generator is a separate decision, deliberately not made here — it arrives with that client, and nothing in this record depends on it.

## Consequences

- **A contract change is a visible diff in a pull request.** That is the point, and it cuts both ways: renaming a field or narrowing a response shows up as a red build until the file is regenerated, which is a prompt to notice that clients will see it.
- **The gate needs Docker**, like the rest of the integration suite. A contributor without it cannot regenerate the contract — the same constraint the suite already imposes.
- **Refusing a quoted number is a narrowing.** Nothing consumes the API yet, so it costs nothing now; a client that later sends `"5"` for a number gets a 400 and a clear reason, which is better than a contract that admits both and a client that picks one at random.
- **The description is only as good as the endpoints' metadata.** Response types come from the `TypedResults` unions the endpoints already return, so they are accurate today; an endpoint that returns a bare `IResult` would describe nothing, and the document would not complain.

## Alternatives considered

- **Build-time generation via `Microsoft.Extensions.ApiDescription.Server`** — rejected on two counts: it emits JSON only in .NET 10, and it boots `Program.cs` under a mock server, which would need entry-assembly guards to survive composing five modules with no database.
- **A `dotnet run` plus a curl script** — needs the stack running anyway, which is exactly what the test suite already arranges, and it would have to be remembered rather than enforced.
- **Not committing the document, and generating clients from a running server** — makes every client build depend on a live backend, and removes the pull-request diff that makes a breaking change visible.
- **Emitting OpenAPI 3.0 for wider tool compatibility** — deferred rather than rejected. 3.1 is what this stack produces and what the chosen generator reads; the question genuinely reopens when the mobile client picks a Dart generator, and pinning the version down then is a one-line change with a visible diff.
