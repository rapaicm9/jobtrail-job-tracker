# 0007 — Testing stack

- **Status:** Accepted
- **Date:** 2026-07-17

## Context

The test suite needs to be pinned before the first tests are written, and two of the choices carry licensing or maintenance risk rather than being matters of taste. The .NET testing ecosystem moved in 2025–2026: FluentAssertions v8 adopted a paid commercial licence, and the architecture-testing library I would otherwise have reached for first has not shipped a release since 2023 — while the runtime it would police is .NET 10.

Architecture tests carry more weight here than a normal test suite. They are what makes the module boundaries in ADR-0001 executable rather than aspirational, so the library underneath them is a load-bearing dependency, not a convenience.

## Decision

- **Test framework: xUnit v3** (`xunit.v3`). Test projects are standalone executables, which is why they set `OutputType=Exe`. Mature tooling and the safest bet for a codebase intended to live to the next LTS.
- **Assertions: Shouldly** (BSD-3-Clause). Simple, well-maintained, good failure messages.
- **Architecture tests: ArchUnitNET** (`TngTech.ArchUnitNET` + `TngTech.ArchUnitNET.xUnitV3`, Apache-2.0), actively maintained, with a first-party adapter for xUnit v3.
- **Integration tests: Testcontainers** against real PostgreSQL and Redis, never the EF in-memory provider. Adopted when the first integration test lands.
- Licences re-verified at adoption per ADR-0002: Apache-2.0, BSD-3-Clause and MIT only. **FluentAssertions ≥ 8 is not used**, on any tier.

### Two properties of the rule engine that shape how rules are written

Both were established by experiment before relying on them, and both are easy to get wrong in a way that produces a green suite that checks nothing.

1. **The engine only sees types from assemblies explicitly loaded into the architecture.** A rule of the form "X must not depend on *&lt;external library&gt;*" matches zero types and therefore passes — and keeps passing after the library is adopted, because it still is not loaded. Verified directly: a rule asserting that ArchUnitNET must not depend on Newtonsoft.Json passes, while ArchUnitNET plainly does depend on it. **Consequence:** rules naming an external library are written in the change that adopts that library, where its assembly can be loaded and the rule demonstrated to fail against a real violation. Writing them earlier would bank false confidence.

2. **The engine requires positive evaluation.** A rule whose source set is empty fails rather than passing, unless `WithoutRequiringPositiveResults()` is used to suppress it. This is the correct default and is deliberately left on.

### The suite is red on arrival, and that is intended

The modules are empty at the time these rules land, so 31 of 36 rules have no types to evaluate and fail on the positive-evaluation guard. The alternative was to disable the guard, which would turn the suite green by making it assert nothing — and an opt-out added "temporarily" is an opt-out that survives, silently covering for a predicate that later stops matching for a genuine reason such as a renamed assembly.

The rules go green as each module gains real types, which is the same moment they start doing their job. Until then a red suite is the accurate signal.

**Update (2026-07-19):** the first module now carries real types, so the suite is green and CI gates on it. A "must-not-depend" rule whose subject assembly is still empty is skipped rather than failed — guarded on the subject resolving to at least one type, evaluated through the same predicate the rule uses. This is not the rejected global opt-out: the positive-evaluation guard stays **on** for every populated assembly, so a rule with types to check still fails if it stops matching them (a renamed assembly throws at load, before any rule runs). An empty module skips vacuously today and its rule turns live, unweakened, the moment that module carries its first type.

## Consequences

- Assembly loading is part of each rule's correctness, not just plumbing. Assemblies are loaded by explicit name rather than by scanning the output directory — the test assembly references every module in order to load them and would otherwise trip the very rules it asserts.
- The rules that name EF Core — Contracts must not expose it, Domain must not depend on it — are deferred to the change that adds EF Core, for the reason in (1) above.
- Enforcing "CI must be green before merge" was deferred until the suite was green, which could not be before the modules carried types; as of the 2026-07-19 update above, the suite is green and CI runs it as a gate.
- No licence exposure: nothing in the test stack can be relicensed out from under the project without an obvious fork available.

## Alternatives considered

- **NetArchTest.Rules** — rejected: no release since 2023, and it would sit underneath the suite the boundary rules depend on, on a runtime published well after its last release.
- **NetArchTest.eNhancedEdition** — a maintained community fork with a near-identical API; a reasonable fallback, but ArchUnitNET has a richer rule model and a first-party xUnit v3 adapter.
- **TUnit** — genuinely attractive (source-generated discovery, AOT-capable, explicit parallelism), but younger tooling is a poor trade for a solo project that cannot absorb toolchain surprises.
- **AwesomeAssertions** — the Apache-2.0 community fork of FluentAssertions v7, with a commitment never to change licence. A sound choice; Shouldly wins on simplicity for assertions that are mostly straightforward.
- **FluentAssertions ≥ 8** — rejected outright on licensing.
- **Disabling the positive-evaluation guard to get a green suite now** — rejected; see above.
