import { defineConfig, devices } from "@playwright/test";
import qa from "../scripts/qa/qa-env.json";

/*  The QA suite (docs/qa/QA-PLAN.md): persona-based system tests on the Sethika_QA database, never
    the development one. The servers are not started from here: scripts/qa/qa.ps1 runs the API and
    the web app pointed at the QA database, and `qa.ps1 check` proves it before a run. The e2e
    helpers (support/*.ts) read their database, Redis db and API address from these variables, so
    the same helpers serve both suites. Set before any spec imports them; workers inherit them.  */
process.env.E2E_SQL_SERVER = qa.sqlServer;
process.env.E2E_SQL_DATABASE = qa.database;
process.env.E2E_REDIS_HOST = qa.redisHost;
process.env.E2E_REDIS_PORT = String(qa.redisPort);
process.env.E2E_REDIS_DB = String(qa.redisDb);
process.env.E2E_API_URL = `http://localhost:${qa.apiPort}`;

const webUrl = `http://localhost:${qa.webPort}`;

export default defineConfig({
  testDir: "./e2e/qa",
  outputDir: "./e2e/qa/.results",
  globalSetup: "./e2e/qa/support/global-setup.ts",
  // One worker: low RAM, and the plan's known 2-worker "Loading" flake (QA-PLAN section 11).
  fullyParallel: false,
  workers: 1,
  retries: 0,
  timeout: 180_000,
  expect: { timeout: 15_000 },
  reporter: [["list"], ["html", { outputFolder: "./e2e/qa/.report", open: "never" }]],
  use: {
    channel: "chromium",
    baseURL: webUrl,
    locale: "en-GB",
    timezoneId: "Asia/Colombo",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  projects: [
    // Builds T1-T6 and the personas once per reset; `qa.ps1 personas`.
    { name: "setup", testMatch: /setup[\\/]personas\.ts$/, timeout: 900_000 },
    // QA-PLAN section 2: 1366 x 768 desktop; cases tagged @phone also at 390 x 844 and 320 x 640.
    { name: "qa", testMatch: /F\d\d-.*\.spec\.ts$/, use: { ...devices["Desktop Chrome"], viewport: { width: 1366, height: 768 } } },
    {
      name: "qa-390",
      testMatch: /F\d\d-.*\.spec\.ts$/,
      grep: /@phone/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 390, height: 844 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
    },
    {
      name: "qa-320",
      testMatch: /F\d\d-.*\.spec\.ts$/,
      grep: /@phone/,
      use: { ...devices["Desktop Chrome"], viewport: { width: 320, height: 640 }, deviceScaleFactor: 2, isMobile: true, hasTouch: true },
    },
  ],
});
