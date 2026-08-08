# Jobspect brand assets

Two marks, chosen for different jobs. **Aperture** (a viewfinder framing a rising trend) is the
brand mark — it engages the name rather than the category, and it is what appears on the landing
page and in the product header. **Monogram** (a solid geometric J) is the icon — it says nothing
about the product but it is flawless at 16px, which is where the brand mark thins out.

Swapping to a simpler mark below a size threshold is ordinary practice for a responsive logo
system. Both are in the same teal, so the pair reads as one brand.

## Files

| File                               | Mark                | Use                                                       |
| ---------------------------------- | ------------------- | --------------------------------------------------------- |
| `jobspect-mark.svg`                | Aperture            | Inline in JSX. Inherits `currentColor` — see below        |
| `jobspect-mark-light.svg`          | Aperture            | `#007A79`. For `<img>`, email, anywhere on a light ground |
| `jobspect-mark-dark.svg`           | Aperture            | `#5BC0BE`. For `<img>` on a dark ground                   |
| `jobspect-lockup-horizontal.svg`   | Aperture + wordmark | Header, email signature. 189 × 44                         |
| `jobspect-lockup-stacked.svg`      | Aperture + wordmark | Landing hero, splash. 127 × 93                            |
| `jobspect-icon.svg`                | Monogram            | `currentColor`. Inline use of the icon                    |
| `favicon.svg`                      | Monogram            | Primary favicon. Theme-aware — see below                  |
| `favicon-16.png`, `favicon-32.png` | Monogram            | Legacy raster fallback, `#007A79`                         |
| `apple-touch-icon.png`             | Monogram            | 180 × 180 iOS tile. Opaque by requirement                 |
| `apple-touch-icon.svg`             | Monogram            | Source for the tile above                                 |

## Colour

The brand teal needs two variants because one cannot do both grounds:

- **`#5BC0BE`** on dark grounds — 8.52:1 on Prussian Blue `#0B132B`
- **`#007A79`** on light grounds — 5.17:1 with white text, 4.87:1 against the `slate-50` page

`#5BC0BE` on white is 2.16:1, short of even the 3:1 large-text floor. Never put the light-ground
mark on a light ground.

The PNG fallbacks are `#007A79` because it is the only one of the two that clears 3:1 against
_both_ a white tab bar (5.17:1) and a dark one (3.11:1). A filled tile was the alternative and
turned out to be unnecessary.

## `currentColor` — an inlining-only feature

`jobspect-mark.svg` and `jobspect-icon.svg` use `currentColor`, so they take their colour from the
CSS `color` of whatever contains them. **This only works when the SVG is inlined into the DOM** —
as a React component, or via an SVG loader. An SVG referenced through `<img src>`, `background-image`
or CSS `mask` is an isolated document and will not see your page's `color`; it will fall back to
black. That is what the `-light` and `-dark` variants are for.

## Wiring the favicon

```html
<link rel="icon" href="/favicon.svg" type="image/svg+xml" />
<link rel="icon" href="/favicon-32.png" sizes="32x32" />
<link rel="apple-touch-icon" href="/apple-touch-icon.png" />
```

`favicon.svg` carries a `prefers-color-scheme` block, so it renders `#007A79` on a light tab bar
and `#5BC0BE` on a dark one. Chrome and Firefox both honour this; Safari takes the PNG.

## The wordmark is outlined

"Jobspect" in both lockups is path data, not a `<text>` element — converted from **Geist SemiBold
1.7.2**, shaped through HarfBuzz so the kerning matches what a browser would have produced. The
lockups therefore render identically everywhere and carry no font dependency: nothing to install,
nothing to fall back to, no CSP `font-src` exception.

The consequence is that the wordmark is no longer editable as text. To change the word, re-run the
conversion rather than editing the path:

```python
# needs: pip install fonttools brotli uharfbuzz  +  Geist-SemiBold.ttf
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.misc.transform import Transform
import uharfbuzz as hb
# shape with hb at upm scale, then draw each glyph through
# TransformPen(SVGPathPen(gs), Transform(s, 0, 0, -s, pen_x, baseline))
```

Sizes baked in: horizontal is 31px with -1.0 letter-spacing, stacked is 28px with -0.9. Both are
optically centred on cap height rather than on the full ink box, so the descender of the _p_ does
not pull the word off-centre against the mark.

Geist itself is **SIL OFL 1.1**, not MIT. The logo no longer depends on it, but the product's UI
typography does — so if CI gates dependency licences against an MIT/Apache allowlist, `OFL-1.1`
still has to be added or the build fails on first install. Outlined glyph data in a logo is a
normal OFL use and carries no attribution requirement in the rendered artwork.

## Regenerating the rasters

The PNGs were rendered from the SVGs with headless Chrome. If you change a mark:

```bash
chrome --headless --disable-gpu --default-background-color=00000000 \
  --screenshot=favicon-32.png --window-size=32,32 favicon-32.svg
```

The source SVG must carry explicit `width`/`height` matching the target — with only a `viewBox`,
Chrome renders at the intrinsic size and pads the rest of the canvas. Windows below about 32px are
unreliable in headless; render larger and downscale if you need a true 16px.
