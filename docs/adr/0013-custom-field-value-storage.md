# 0013 — Custom-field value storage

- **Status:** Accepted
- **Date:** 2026-07-27
- **Refines:** ADR-0004 (custom field storage)

## Context

ADR-0004 settled where custom-field values live: a JSONB column on the application, keyed by field-definition id, queried with native PostgreSQL operators (`->`, `->>`, `@>`), with a GIN index over only the paths actually queried. It also named a mechanism — an EF Core complex type mapped with `ToJson()`.

Building it showed those two halves cannot both hold. An EF complex type has a shape fixed when the model is built; this bag's keys are definition ids that only exist at runtime. The nearest thing EF can express is a complex *collection*, which would store an array:

```json
[{"fieldId":"…","text":"Platform"},{"fieldId":"…","number":3}]
```

That satisfies the word "complex type" and defeats everything else the ADR asked for. An array has no per-field path: nothing to put on the left of `->`, nothing to index for one field, and sorting by a single custom field needs a lateral join rather than an expression. The mechanism and the requirement were in conflict, and the requirement is the part that matters.

## Decision

**An object keyed by definition id, in one `jsonb` column, mapped with a value converter.**

```json
{"0199…-a1":"Platform","0199…-b2":3,"0199…-c3":["Go","Rust"]}
```

- **Values are stored as the raw JSON scalar their type calls for** — a string, a number, a boolean, an array of strings — not wrapped in a shape of our own. Wrapping would push every query one level deeper and buy nothing, since the bag holds answers and the answer *is* the value.
- **Keyed by id, so a single field is addressable by path.** `custom_field_values -> '<id>'` is what filtering, sorting and a targeted GIN index all rest on, and it is what ADR-0004 meant by "keyed by field-definition id".
- **A value converter with an explicit `ValueComparer`.** The comparer compares the stored document rather than the reference, because every update reassigns the bag and reference equality would rewrite the column on every edit of an application.
- **The bag is immutable** and replaced wholesale. Nothing reaches into it, so the change tracker's snapshot can be the value itself.
- **The values are not typed in the domain.** The type of an answer lives on its definition, and the aggregate never interprets an answer — it records what came in and hands it back. Typing is enforced once, at the edge, against the definitions: JSON shape must match the field's type, selects must name a real option, dates must be `yyyy-MM-dd`, text is length-capped.
- **Writing is gated in the handler, not on the route.** Both tiers create and edit applications, so the entitlement covers one part of the payload rather than the call. Sending the bag without the entitlement is a **403**; leaving it off retains what is stored. For an entitled caller the bag replaces like every other field on a full-replace `PUT`, absent meaning cleared.

## Consequences

- The JSON on disk is what a person would write by hand, which is also what the filtering and sorting work needs. `@> '{"<id>":"Platform"}'` is a containment probe `jsonb_path_ops` serves directly.
- **EF cannot see into the bag in LINQ.** Queries against it will be raw JSONB fragments rather than translated predicates. That is the arrangement ADR-0004 assumed ("native PostgreSQL JSONB operators"), and it is the cost of a shape the provider does not model.
- `ErrorType.Forbidden` enters the kernel. There was no way to say "known caller, understood request, not permitted" before, because every prior refusal was an authorization policy on a route. All three modules' ProblemDetails mappers map it.
- **"Retained read-only" is a property of the update path, not of storage.** Nothing about the column enforces it; the rule is that an unentitled caller's edit leaves the bag alone. That is the one behaviour here worth guarding with tests, and it is.
- The definitions table is `custom_fields` and this column is `custom_field_values` — close enough to confuse, distinct enough to compile. Named apart deliberately.

## Alternatives considered

- **A complex collection via `ToJson()`, honouring ADR-0004 literally** — rejected above: it trades every query property the same ADR asks for in exchange for using the mechanism it named.
- **Wrapping each value in a typed envelope** (`{"text":"…"}`) — rejected. It makes the C# side marginally more typed and every path, index and containment probe one level deeper, in exchange for typing that the definition already carries.
- **A `JsonElement`-free domain with a discriminated value union** — rejected for now. It would be real machinery serving no reader: nothing in the aggregate branches on a custom value, and validation against the definitions happens once at the edge either way.
- **`null` stored as an answer** — rejected. An absent answer and an answer of null are the same fact, and keeping both would make every consumer handle two spellings of "unanswered". A null clears the key.
