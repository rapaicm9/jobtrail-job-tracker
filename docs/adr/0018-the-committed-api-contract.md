# 0018 — The committed API contract and client generation

- **Status:** Accepted
- **Date:** 2026-08-05
- **Amended:** 2026-08-07 — the document now describes its enums, its failures and its operation ids. See *Revision history*.

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

- **Enums are described as their members, on the way out only.** Responses carry the enum types themselves and a string-enum converter writes their names — which is what they always were on the wire, since the responses previously stringified them by hand. The difference is that the description can now state the member set instead of an unconstrained string, so a client stops carrying its own copy of the vocabulary. No naming policy, so the names travel verbatim.

  **Requests keep every such field as a string**, and this is the rule rather than an oversight. An enum-typed request property hands the refusal of an unknown value to the model binder, which answers a bare 400 naming no field, in place of the field-keyed 422 every other bad value in this API gets. The handlers parse leniently and their validators key the failure to the property. A test over the document holds the rule, because since the responses gained real enums the mistake no longer shows up as integers on the wire — it works, and only the refusal degrades.

- **The failures are described, and derived rather than annotated.** Every endpoint returns a `TypedResults` union ending in a bare problem result, which carries no status, so the generator reported none — leaving a generated client with an error branch typed as nothing at all, against an API whose entire error protocol is a stable `code` in the body. Two schemas are registered once: the RFC 9457 members plus the `code` and `traceId` this host adds to all of them, and the same again with the field-keyed `errors` a 422 may carry instead of a code. One schema for 422 rather than a choice between two, because a client has to read whichever member is present anyway.

  The per-operation responses are read from metadata the endpoints already carry. Two hundred–odd `ProducesProblem` lines are not something anyone maintains, and a stale one is worse than none. **What is described is what an endpoint's shape implies** — the authorization challenge, an entitlement policy, the rate limiter, the replay middleware, a validated body, an id in the path, a cursor it issued. **What its subject matter does is not**: a wrong password, a taken campaign name, a chart whose dependency is down have no metadata, share these same two shapes, and are told apart by the `code`. So the described statuses are a floor and the code stays the thing to branch on.

- **Every operation carries an id, hand-chosen.** `openapi-fetch` does not need one — it keys off path and method — but a generator that has none synthesizes a method name from the path, so a route rename becomes a rename of every call site in that client. Deriving the id from the path in a transformer would have reproduced exactly that churn, so the ids are set per endpoint instead, from the feature folders' own names.

- **Operations are tagged by resource, taken from the path.** Generators name client classes after tags, and the default tag is the name of the class declaring the endpoint — which, in a codebase of one endpoint per class, would produce a client of forty-odd single-method services instead of one per resource.

- **The document names no server.** The generator fills `servers` from the request that fetched it, so the same contract would describe a different origin on every host, and — the local port being assigned per run — on every run. Empty means "wherever this document came from", which is true everywhere and is what makes the artefact reproducible enough to diff.

- **The web client generates types with `openapi-typescript` and calls through `openapi-fetch`.** Both MIT. Types-only generation means there is no generated runtime to review, and the pair reads OpenAPI 3.1 natively, so the committed document needs no post-processing step between the server and the client.

  This suits the token model: the browser never holds a token, the Next.js server does (ADR 0003), so what the client needs is a typed `fetch` inside a route handler rather than a framework-shaped SDK. The mobile client's generator is a separate decision, deliberately not made here — it arrives with that client, and nothing in this record depends on it.

- **A human reads the same document through a reference UI at `/scalar`**, mapped in Development beside the document itself. It is how the API is exercised by hand while no client exists, and it drives the real host — so what it demonstrates is what the contract claims.

  **It is the only HTML this host ever serves, and therefore the only exception to a content-security policy built for JSON.** That policy is `default-src 'none'`, which would render the page blank. The exception is carried on endpoint metadata and applied in the security-headers middleware, so both policies are stated in one file and an auditor finds the exception where they look for the rule — rather than discovering that a page somewhere overrode the header on its way out.

  The widened policy still names no host but this origin: the package serves its own bundle, and the webfonts it would otherwise fetch from a CDN are turned off, which also keeps the dev loop working offline. `connect-src 'self'` is what lets the page call this API and nothing else. What it does concede is `'unsafe-inline'`, for the page's bootstrap script and the styles the UI injects at runtime; a per-request nonce would remove the first and is the upgrade path if this page ever ships anywhere but a developer's machine.

## Consequences

- **A contract change is a visible diff in a pull request.** That is the point, and it cuts both ways: renaming a field or narrowing a response shows up as a red build until the file is regenerated, which is a prompt to notice that clients will see it.
- **The gate needs Docker**, like the rest of the integration suite. A contributor without it cannot regenerate the contract — the same constraint the suite already imposes.
- **Refusing a quoted number is a narrowing.** Nothing consumes the API yet, so it costs nothing now; a client that later sends `"5"` for a number gets a 400 and a clear reason, which is better than a contract that admits both and a client that picks one at random.
- **The description is only as good as the endpoints' metadata.** Success types come from the `TypedResults` unions the endpoints already return, so they are accurate today; an endpoint that returns a bare `IResult` would describe nothing, and the document would not complain. The failures are derived from metadata for the same reason, which is what makes an endpoint mapped later described because of how it was mapped — and also what bounds it: no metadata says which domain errors a handler can raise.
- **A handler can answer a status the document does not list.** An account with no plan row is a 404 on a path holding no id; a chart whose dependency is down is a 503 on a read. Both have the shape the document already describes at that operation, so a generated client's error branch is typed correctly either way; what is missing is the status in the list. That is the accepted price of deriving rather than annotating, and it is survivable precisely because the client is told to branch on `code`.

## Alternatives considered

- **Build-time generation via `Microsoft.Extensions.ApiDescription.Server`** — rejected on two counts: it emits JSON only in .NET 10, and it boots `Program.cs` under a mock server, which would need entry-assembly guards to survive composing five modules with no database.
- **A `dotnet run` plus a curl script** — needs the stack running anyway, which is exactly what the test suite already arranges, and it would have to be remembered rather than enforced.
- **Not committing the document, and generating clients from a running server** — makes every client build depend on a live backend, and removes the pull-request diff that makes a breaking change visible.
- **Emitting OpenAPI 3.0 for wider tool compatibility** — deferred rather than rejected. 3.1 is what this stack produces and what the chosen generator reads; the question genuinely reopens when the mobile client picks a Dart generator, and pinning the version down then is a one-line change with a visible diff.
- **Loosening the host's content-security policy for every response, so the reference page works** — rejected. The policy is one of the few things about this host that is uniformly strict, and a JSON API that allows scripts everywhere to accommodate one dev-only page has traded a real property for a convenience. Scoping the exception to a single endpoint costs a metadata marker.

## Revision history

- **2026-08-07 — the document describes its enums, its failures and its operation ids.** The contract froze before the first client was written, and reading it as that client's author found three things it did not say. It carried no `enum:` anywhere, so the eleven vocabularies existed only in C# and a client would have had to be handed a copy of each. It described no failure at all — every operation listed its success and stopped — which for a generated client means an error branch typed as nothing, on an API where a stable `code` in the body *is* the error protocol. And it carried no `operationId`, which the web client does not need and the mobile one will.

  What needed deciding, and is recorded above, is not that these should be described but where the line falls. On enums: responses only, because typing a request property as an enum moves the refusal from a validator to the model binder and turns a field-keyed 422 into a bare 400. On failures: derived from endpoint metadata rather than annotated per endpoint, and therefore describing what an endpoint's shape implies rather than what its subject matter does — a distinction the *Consequences* now state as a limit. On ids: hand-chosen, because an id derived from the path churns when the path churns, which is the churn an id exists to absorb.

  Nothing about provenance, the gate or the codegen choice changed. Two behaviours did, both narrowings that cost nothing while no client exists: the enum member sets are now closed, and the described failures are a claim the document did not previously make.
- **2026-08-05 — the reference UI, and its content-security exception.** The document was readable by machines from the moment it existed and by nobody else; a reference over it is what makes the API exercisable while no client is written. The part that needed deciding, and is recorded above, is not the UI but the policy: this host's `default-src 'none'` would render it blank, so something had to give, and the choice was between widening the policy for every response and scoping the exception to the one endpoint that renders. Nothing about the contract, its provenance or the codegen choice changed.
