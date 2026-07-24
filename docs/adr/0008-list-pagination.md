# 0008 — List pagination

- **Status:** Accepted
- **Date:** 2026-07-24

## Context

Every list a client reads — applications, contacts, interview rounds, an application's activity timeline — has so far returned every matching row. That is fine while a user has a dozen applications and dishonest as a contract: nothing in the API says where the ceiling is, so a client cannot be written to handle one arriving. Two clients (Next.js, then Flutter) will codegen against this contract, and mobile in particular reads over links where a large response is expensive.

The lists also differ in what they are sorted by — an applied date, a name, a scheduled instant, a creation instant — so whatever paging scheme is chosen has to work for all four without each list inventing its own.

## Decision

**Keyset (cursor) pagination, with one envelope shared by every paged list.**

A list takes `limit` (default 25, maximum 100) and `cursor`, and returns:

```json
{ "items": [ … ], "nextCursor": "<opaque>" }
```

`nextCursor` is null when the feed is exhausted; that is the only signal a client needs to stop.

- **The cursor is a position, not an offset.** It carries the sort value of the last row returned plus that row's id, base64url-encoded into one opaque string. The next page is "everything ordered after this point", which is a range scan against the index rather than a count-and-skip.
- **Every paged list has a total order.** Each one sorts by its natural key *and then by id* (a UUIDv7, so time-ordered). Without that tiebreak, rows sharing a sort value — applications sent on the same day, two contacts with the same name — could be repeated or skipped at a page edge.
- **The cursor is opaque by contract.** Clients echo it back and never parse it, which leaves the encoding free to change without a version bump.
- **Cursors are not signed or encrypted.** A cursor carries only the caller's own row key, and every paged query is owner-scoped independently, so a forged or borrowed cursor can do nothing but reposition a user inside their own data. Signing would add key management to protect nothing.
- **A cursor is validated against the list it is used on.** It must decode, and its sort key must be the kind that list orders by, so a cursor from another list is a 422 rather than a silent restart from the top — a client looping forever on page one is worse than an error.
- **The envelope and the cursor codec are shared**, not copied per module, precisely because the value of a consistent envelope is that it cannot drift.
- **Indexes follow the order.** Each paged list has a composite index on `(owner or parent, sort key, id)` — the exact path the cursor walks.
- **No total count.** A keyset page is cheap because it never looks at the rows behind it; a count would put that cost back on every request.

The company type-ahead (`GET /companies?query=`) is deliberately excluded. It is a search affordance that returns a capped shortlist, not a collection being enumerated, and paging a picker is not a thing users do.

## Consequences

- Rows inserted or deleted while a client pages cannot shift a page under it — the common failure of offset paging, where a new row at the head makes page 2 repeat a row from page 1. This matters most on the activity timeline, which grows at the head while it is read.
- Pages can only be walked forward from a given position. There is no "jump to page 7", and providing one would mean offsets again. No screen in the product asks for it.
- Adding a paged list means choosing its sort key and giving it the matching composite index; the arithmetic of building a page is not rewritten each time.
- Changing a list's sort order invalidates cursors already in flight. They fail as invalid cursors rather than returning wrong rows.

## Alternatives considered

- **Offset/limit** (`?page=3&pageSize=25`) — rejected. Cheap to write and correct only for data that never changes underneath the reader; it degrades as the offset grows, since the database must walk and discard every skipped row.
- **A cursor that is just the last id** — rejected. It works only where the sort order matches the id order, which is true for the timeline and false for every other list here.
- **Signed or encrypted cursors** — rejected as protecting nothing, given the ownership check is in the query and the payload is the caller's own data.
- **Envelope with a total count** — rejected. It re-imposes the scan that keyset paging exists to avoid, for a number no current screen displays.
