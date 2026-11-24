import { defineConfig, devices } from "@playwright/test";

/*  Browser tests: the real web app, the real API, the local SQL Server and Redis - nothing
    mocked, the same rule as the API's integration tests. Servers already running (npm run dev,
    dotnet run) are reused; otherwise they are started and stopped around the run.

    Two projects: a desktop window, and a 320 px phone - the narrowest the screens promise.
    Tests tagged @phone run on both; the rest on desktop only, to keep a run short.          */

const webUrl = process.env.E2E_WEB_URL ?? "http://localhost:3000";
const apiUrl = process.env.E2E_API_URL ?? "http://localhost:5182";

export default defineConfig({
  testDir: "./e2e",
  // The QA suite runs on its own database with its own config (playwright.qa.config.ts).
  testIgnore: ["qa/**"],
  outputDir: "./e2e/.results",
  globalSetup: "./e2e/support/global-setup.ts",
  fullyParallel: true,
  workers: 2,
  retries: 0,
  timeout: 120_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { outputFolder: "./e2e/.report", open: "never" }]],
  use: {
    // Full Chromium in its new headless mode: closer to the Chrome people use than the
    // stripped-down headless shell, and one browser download instead of two.
    channel: "chromium",
    baseURL: webUrl,
    locale: "en-GB",
    timezoneId: "Asia/Colombo",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    { name: "desktop", use: { ...devices["Desktop Chrome"] } },
    {
      name: "phone",
      grep: /@phone/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 320, height: 640 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
    },
  ],
  webServer: [
    {
      command: "dotnet run --project ../src/Sethika.Api --launch-profile http",
      url: `${apiUrl}/health`,
      reuseExistingServer: true,
      timeout: 240_000,
    },
    {
      command: "npm run dev",
      url: webUrl,
      reuseExistingServer: true,
      timeout: 240_000,
    },
  ],
});
