# 0002 — Design tokens: three layers, two colour scales, one density floor

- **Status:** Accepted
- **Date:** 2026-08-08

## Context

The client targets WCAG 2.2 AA and ships light and dark themes. Colour carries domain meaning in
two places — the pipeline stage of an application and its terminal outcome — and those two are
different kinds of value. A palette settled by eye at this point would be re-litigated at every
review, and a palette hard-coded into components would make a later change a sweep across every
file.

The brand supplies three fixed points: teal `#007A79` for light grounds, `#5BC0BE` for dark, and
Prussian Blue `#0B132B` as the dark ground. The marks in `brand/` are drawn in them.

## Decision

### Three layers, enforced rather than agreed

**Primitives** are raw ramps in `:root`. They live *outside* Tailwind's `@theme`, so no utility
class is generated for them and a component cannot reach one. **Semantics** are the roles
components read — `--background`, `--stage-offer`, `--border-strong` — bridged to utilities in a
`@theme inline` block. **Component overrides** are the third layer and are currently empty; each
one that lands carries the reason it could not be a semantic token.

`@theme inline` rather than plain `@theme` is required, not stylistic: without `inline` a utility
references the theme variable instead of its value, and a token redefined inside the dark media
query resolves at the wrong level of the cascade.

**Tailwind's default palette is deleted** with `--color-*: initial`. `bg-blue-700` resolves to
nothing, so reaching past the semantic layer fails visibly instead of shipping quietly. `white` and
`black` are theme variables and are re-declared; `transparent`, `current` and `inherit` are
keywords and are unaffected.

### Two colour scales, because the domain has two

The four active stages are ordered, so they take a **sequential** blue ramp. The four outcomes are
not, so they take a **categorical** scale. One palette across all eight would assert a rank among
outcomes that the domain denies — see [0001](0001-board-holds-four-columns.md).

**The ramp direction reverses between themes.** On a light ground progression deepens; on a dark
ground deepening recedes into the background, so the same four steps run the other way and
progression reads as brightening. The invariant is *further along is visually heavier*, not a fixed
lightness direction.

Each stage and outcome carries three tokens, because one value cannot do all three jobs: an
**identity** colour for dots, chart marks and chip borders, clearing 3:1 against the page as a
non-text mark; a chip **surface**; and a **foreground** for text on that surface, clearing 4.5:1.
Stage surfaces deepen along the ramp; outcome surfaces all sit at one lightness, because a
lightness difference among unordered values implies a rank.

**Withdrawn is deliberately near-neutral.** Withdrawing is the user's own decision, and colouring it
like a failure editorialises.

**Colour never carries meaning alone.** Every chip shows its name; colour is redundant encoding.
Luminance gaps inside the outcome set run as tight as 1.09:1, so hue is doing the work and
greyscale removes it — which is why marks are labelled directly rather than keyed to a legend.

### Two border tokens

`--border` is a divider and exempt from WCAG 1.4.11. `--border-strong` clears 3:1 and is what
identifies a control. Using the subtle one on a text input is the most common way a design system
fails 1.4.11 while looking tidy. In dark mode this is load-bearing rather than decorative: the card
and page surfaces sit 1.22:1 apart, so a card rendered without a border has effectively no edge.

### Measured, not asserted

Every value was solved against a contrast target. Measured ratios:

| | light | dark |
|---|---|---|
| body text on page | 17.57 | 17.57 |
| muted text on page | 4.52 | 7.10 |
| primary on page | 4.94 | 8.52 |
| `--border-strong` on page | 3.00 | 3.07 |
| card vs page | 1.05 | 1.22 |
| stage identity vs page | 3.05 – 6.99 | 3.05 – 11.00 |
| outcome identity vs page | 3.29 – 6.52 | 5.47 – 10.68 |
| chip text on chip surface | 4.99 – 5.01 | 4.99 – 5.01 |
| tightest outcome pair, greyscale | 1.14 | 1.09 |

`e2e/tokens.spec.ts` asserts these floors in both themes, so a token edited to a nicer-looking
value that drops below one fails the build rather than shipping.

### Dark mode is system-driven

Tokens flip under `@media (prefers-color-scheme: dark)`. There is no toggle and no theme library:
a toggle needs a `.dark` block repeating the values and a blocking inline script to set the class
before first paint, and neither is worth carrying without a request for it. The `dark` variant
already matches `.dark` as well as the media query, so adding one later is additive.

### Density, and the target-size collision

The scale is the dashboard one, 8–32px. Tailwind derives every spacing utility from `--spacing`, so
the scale is continuous and the decision is which steps are in bounds: **2 3 4 5 6 8**. Anything
above `8` is the marketing scale.

The interesting part is a genuine conflict. A 44×44px touch target is WCAG 2.5.5, which is **AAA**;
the AA criterion is 2.5.8 at **24×24**. A 44px row is not a compact table, so on the densest screen
in the product the two pull in opposite directions.

**Resolution: the larger floor binds where the pointer is coarse.** `@media (pointer: coarse)`
raises the minimum interactive box to 44px and lifts the small and medium control heights to match;
fine pointers keep 32–36px controls with at least 8px between them, which clears 2.5.8 with room.

This is not machine-checkable. `axe-core`'s `target-size` rule is off unless the WCAG 2.2 ruleset is
requested by name, and even enabled it tests 24×24 — so the 44px floor depends on the manual
keyboard and pointer pass, not on the automated gate.

## Consequences

- A palette change is an edit to the semantic layer. No component holds a colour.
- The generated component library reads semantic tokens for free, since `:root` plus `@theme inline`
  is the shape it already expects.
- Two extra tokens per stage and outcome — 24 in total — in exchange for chips that are readable by
  construction rather than by inspection.
- Deleting the default palette means a generated component written against stock utilities renders
  unstyled. That is the intended signal, and it surfaces at review rather than in production.
- `--font-mono` is deliberately absent: there is no monospace anywhere in the product UI, and a
  token for it would be an invitation.
- The chart slots (`--chart-1` … `--chart-5`) are declared but **provisional**. They are settled
  against real data before the analytics screens are built.

## Alternatives considered

- **One categorical palette across all eight stages.** Rejected: it asserts an order among outcomes.
- **Keeping Tailwind's default palette and relying on review.** Rejected: the rule that every visual
  call lands as a token is only worth stating if something enforces it.
- **A saturated amber accent.** An earlier draft made `--accent` an amber a hair from the Ghosted
  outcome — about 3° of hue and 1.12:1 apart — and resolved the collision with a usage rule barring
  the accent from large fills next to an outcome chip. Replaced by a neutral hover tint, which means
  the collision cannot arise at all. Amber survives as Ghosted, where it carries meaning.
- **A runtime theme toggle.** Deferred; see above.
- **44px targets everywhere.** Rejected: it contradicts the dense table the product is built around,
  and it exceeds the AA criterion the client actually targets.
