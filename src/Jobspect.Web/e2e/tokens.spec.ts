import { expect, test } from "@playwright/test";

// The palette was solved against contrast targets rather than picked by eye,
// so the targets are worth asserting: a token edited to a nicer-looking value
// that quietly drops below 4.5:1 is exactly the regression this catches.
//
// Colours are resolved through a canvas rather than parsed from
// getComputedStyle, because the browser reports whatever notation the author
// wrote and only the painted sRGB bytes are comparable.
async function readTokens(page: import("@playwright/test").Page, names: string[]) {
  return page.evaluate((tokenNames) => {
    const canvas = document.createElement("canvas");
    canvas.width = canvas.height = 1;
    const ctx = canvas.getContext("2d", { willReadFrequently: true })!;
    const styles = getComputedStyle(document.documentElement);

    const toRgb = (value: string): [number, number, number] => {
      ctx.clearRect(0, 0, 1, 1);
      ctx.fillStyle = value.trim();
      ctx.fillRect(0, 0, 1, 1);
      const [r, g, b] = ctx.getImageData(0, 0, 1, 1).data;
      return [r, g, b];
    };

    return Object.fromEntries(
      tokenNames.map((name) => [name, toRgb(styles.getPropertyValue(name))]),
    ) as Record<string, [number, number, number]>;
  }, names);
}

function contrast(a: [number, number, number], b: [number, number, number]) {
  const lum = ([r, g, b]: [number, number, number]) => {
    const [R, G, B] = [r, g, b].map((v) => {
      const s = v / 255;
      return s <= 0.04045 ? s / 12.92 : Math.pow((s + 0.055) / 1.055, 2.4);
    });
    return 0.2126 * R + 0.7152 * G + 0.0722 * B;
  };
  const [hi, lo] = [lum(a), lum(b)].sort((x, y) => y - x);
  return (hi + 0.05) / (lo + 0.05);
}

const STAGES = ["applied", "screening", "interview", "offer"] as const;
const OUTCOMES = ["accepted", "rejected", "withdrawn", "ghosted"] as const;

const NAMES = [
  "--background",
  "--foreground",
  "--card",
  "--muted-foreground",
  "--primary",
  "--primary-foreground",
  "--border",
  "--border-strong",
  ...STAGES.flatMap((s) => [`--stage-${s}`, `--stage-${s}-surface`, `--stage-${s}-foreground`]),
  ...OUTCOMES.flatMap((o) => [
    `--outcome-${o}`,
    `--outcome-${o}-surface`,
    `--outcome-${o}-foreground`,
  ]),
];

test("body text and UI boundaries clear their WCAG floors", async ({ page }) => {
  await page.goto("/");
  const t = await readTokens(page, NAMES);

  expect(contrast(t["--foreground"], t["--background"])).toBeGreaterThanOrEqual(4.5);
  expect(contrast(t["--muted-foreground"], t["--background"])).toBeGreaterThanOrEqual(4.5);
  expect(contrast(t["--primary"], t["--background"])).toBeGreaterThanOrEqual(4.5);
  expect(contrast(t["--primary-foreground"], t["--primary"])).toBeGreaterThanOrEqual(4.5);

  // --border is a divider and exempt from 1.4.11; --border-strong identifies a
  // control and is not.
  expect(contrast(t["--border-strong"], t["--background"])).toBeGreaterThanOrEqual(3);
});

test("every chip reads against its own surface", async ({ page }) => {
  await page.goto("/");
  const t = await readTokens(page, NAMES);

  for (const name of [...STAGES.map((s) => `stage-${s}`), ...OUTCOMES.map((o) => `outcome-${o}`)]) {
    expect(
      contrast(t[`--${name}-foreground`], t[`--${name}-surface`]),
      `${name} chip text`,
    ).toBeGreaterThanOrEqual(4.5);

    // The identity colour is a non-text mark: dots, chart series, chip borders.
    expect(contrast(t[`--${name}`], t["--background"]), `${name} mark`).toBeGreaterThanOrEqual(3);
  }
});

test("the stage ramp is ordered and the outcome scale is not", async ({ page }) => {
  await page.goto("/");
  const t = await readTokens(page, NAMES);

  // Further along is visually heavier. Which direction that runs depends on the
  // theme, so the assertion is monotonicity against the page, not lightness.
  const weights = STAGES.map((s) => contrast(t[`--stage-${s}`], t["--background"]));
  for (let i = 1; i < weights.length; i++) {
    expect(weights[i], `${STAGES[i]} vs ${STAGES[i - 1]}`).toBeGreaterThan(weights[i - 1]);
  }

  // Outcomes are unordered, so their chip surfaces must not imply a rank.
  const surfaces = OUTCOMES.map((o) => contrast(t[`--outcome-${o}-surface`], t["--background"]));
  expect(Math.max(...surfaces) - Math.min(...surfaces)).toBeLessThan(0.1);
});

test("the four stages stay distinguishable in greyscale", async ({ page }) => {
  await page.goto("/");
  const t = await readTokens(page, NAMES);

  // Colour is redundant encoding — every chip shows its name — but a pair that
  // collapses entirely in greyscale is still worth knowing about.
  for (const group of [STAGES.map((s) => `stage-${s}`), OUTCOMES.map((o) => `outcome-${o}`)]) {
    for (let i = 0; i < group.length; i++) {
      for (let j = i + 1; j < group.length; j++) {
        expect(
          contrast(t[`--${group[i]}`], t[`--${group[j]}`]),
          `${group[i]} vs ${group[j]}`,
        ).toBeGreaterThanOrEqual(1.06);
      }
    }
  }
});
