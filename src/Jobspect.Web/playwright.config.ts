import { defineConfig, devices } from "@playwright/test";

const PORT = Number(process.env.PORT ?? 3100);
const baseURL = `http://127.0.0.1:${PORT}`;

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? "github" : "list",
  use: {
    baseURL,
    trace: "on-first-retry",
  },
  projects: [
    { name: "chromium", use: { ...devices["Desktop Chrome"] } },
    // The token file flips on prefers-color-scheme, so dark is a second pass
    // over the same specs rather than a separate suite.
    {
      name: "chromium-dark",
      use: { ...devices["Desktop Chrome"], colorScheme: "dark" },
    },
  ],
  webServer: {
    // A production build, never the dev server: dev writes into the project
    // directory and a test run must not touch the working tree. It also means
    // the suite measures what actually ships.
    command: "pnpm build && pnpm start",
    url: baseURL,
    reuseExistingServer: !process.env.CI,
    timeout: 180_000,
    env: { PORT: String(PORT) },
  },
});
