# 0001 — The board holds four columns, not eight

- **Status:** Accepted
- **Date:** 2026-08-08

## Context

An application moves through a fixed pipeline. Four stages are _active_ and strictly ordered —
Applied, Screening, Interview, Offer — and four are _terminal_ outcomes with no order among
themselves: Accepted, Rejected, Withdrawn, Ghosted. The API models both as members of one stage
enum, and a transition to a terminal stage uses the same endpoint as an advance.

That shared representation invites a board with one column per stage. It is the wrong reading of
the domain.

## Decision

The board shows **four columns: Applied, Screening, Interview, Offer.**

- Closing an application is a **different gesture** from advancing it: a "Close out" drop zone that
  opens an outcome picker, or the transition menu on the card. A terminal move is not a fifth
  column to the right.
- **Closed applications live in the Applications list**, behind an outcome filter. The board header
  carries a chip counting them, linking to that filtered list.
- **Skips are legal.** The API permits a jump forward, so a drop must not be restricted to the
  adjacent column.
- **An unrecognised stage renders in a fifth, muted column labelled with the raw value.** It never
  throws, and the mapper logs it once so a contract change surfaces in telemetry rather than in a
  user's face.

Three arguments fix this, in order of weight:

1. **Eight columns put unordered values on an axis that means order.** Left-to-right on a board is
   read as progression. Placing Accepted, Rejected, Withdrawn and Ghosted along it asserts a
   sequence among them that does not exist, and no amount of styling removes the implication.
2. **The terminal columns grow without bound while the active ones stay small.** Over a job search
   almost every application ends up terminal. A board whose four rightmost columns accumulate
   hundreds of cards while the working columns hold a handful is a board nobody scrolls.
3. **It matches how the API distinguishes the two.** Giving a close-out its own gesture makes the
   distinction visible rather than incidental, and an outcome picker is where the choice among four
   unordered values actually belongs.

## Consequences

- The board route renders four columns; the drag-and-drop target model has four ordered targets
  plus one close-out zone, and the zone's drop handler opens a picker rather than committing a
  transition.
- Reaching a closed application is a filter on the list, so the list's outcome filter is not
  optional polish — it is the only path to that data.
- Stage colour must split into two token families, since the four active stages want a sequential
  scale and the four outcomes want a categorical one. That is [0002](0002-design-token-architecture.md).
- The client's model of the state machine stays a convenience. The server's is the truth, so a
  refused move still has to be handled on a `422` even though the UI offered it.

## Alternatives considered

- **Eight columns.** Rejected for the three reasons above.
- **Four columns plus a single collapsed "Closed" column.** Better than eight, but a column implies
  droppability, and dropping onto it would still need a picker to choose among four outcomes — so
  it buys a worse affordance for the same interaction.
- **A separate archive route.** Rejected as a second place to look for applications; the list
  already filters, and a filter is one concept rather than two screens.
