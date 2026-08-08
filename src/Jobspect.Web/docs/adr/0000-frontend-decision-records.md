# 0000 — Frontend decision records

- **Status:** Accepted
- **Date:** 2026-08-08

## Context

The repository already keeps architecture decision records at `docs/adr/`, covering the .NET
backend. The web client makes decisions the backend has no opinion on — rendering strategy, the
session model, how the design system is layered — and it inherits constraints from decisions the
backend already took.

Filing both in one directory would mean one number sequence covering two codebases that ship
independently and are read by people asking different questions. Filing the frontend's decisions
nowhere would leave its reasoning in commit messages.

## Decision

The web client keeps its own log at `src/Jobspect.Web/docs/adr/`, numbered from `0001` and
independent of the backend's sequence.

- **Conventions are the backend's**, defined in `docs/adr/0000-record-architecture-decisions.md`:
  Nygard style, one record per topic, amended in place rather than superseded by a new file, a
  dated *Revision history* at the foot, numbers retired rather than reused.
- **A record in the other log is always named in prose as "backend ADR NNNN"**, never as a bare
  number. The two sequences overlap, so `0004` alone is ambiguous and a reader who resolves it
  against the wrong log lands on an unrelated topic.
- Records here stand alone. A reader who clones the repository has the code, the two ADR logs and
  the OpenAPI document, and every claim a record makes has to be checkable against those.

## Consequences

- Two sequences to keep straight, mitigated by the naming rule.
- A frontend decision that contradicts a backend one is visible as a contradiction between two
  records rather than hidden in a single renumbered sequence.
- The client can be extracted from this repository without its decision history being entangled
  with the backend's.

## Alternatives considered

- **One log at the repository root, `web-` prefixed.** One place to look, at the cost of a mixed
  directory and a filename convention half the records do not follow.
- **No separate log; fold frontend decisions into the backend's.** Rejected: the sequence exists to
  answer "what is the current design of X", and X would mean two different things depending on the
  record.
