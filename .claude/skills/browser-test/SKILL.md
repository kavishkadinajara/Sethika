---
name: browser-test
description: Test Sethika's web screens in a real browser with Playwright - run the e2e suite, write new scenarios, and look at the screenshots. Use after building or changing any screen in web/, before calling a screen done, or when asked to "test in the browser", check the phone layout, or try many scenarios.
---

# Browser tests (Playwright)

The suite drives the real web app against the real API, local SQL Server and Redis: nothing
mocked, the same rule as the API's integration tests. Claude runs it and reads the screenshots
itself; a person does not have to click through every case.

## Run

```bash
cd web
npm run e2e                                  # whole suite, desktop + 320 px phone
npx playwright test e2e/onboarding/apply.spec.ts         # one file
npx playwright test -g "withdrawn" --project desktop     # one test, one project
npm run e2e:report                           # the HTML report of the last run
```

- Servers: if `dotnet run --project src/Sethika.Api --launch-profile http` (:5182) and
  `npm run dev` (:3000) are already running they are reused; otherwise Playwright starts both
  and stops them after. The API builds into `src/Sethika.Api/bin`, so **do not run the suite
  while `dotnet test` or a mutation run is building** - wait for it to finish.
- Database: the development database `Sethika` (override with `E2E_SQL_SERVER`,
  `E2E_SQL_DATABASE`); Redis db 3 (`E2E_REDIS_HOST`, `E2E_REDIS_PORT`, `E2E_REDIS_DB`). It must be
  published from the current dacpac (docs/LOCAL_SETUP.md section 3) after any schema change.
- Run it in the background (`run_in_background`) - a full run takes minutes.

## Look at the result

- Failures: `web/e2e/.results/<test>/` holds `test-failed-1.png` and `trace.zip`. Read the PNG
  with the Read tool; `npx playwright show-trace <trace.zip>` for the step-by-step.
- Screens: `languages.spec.ts` saves full-page screenshots to `web/e2e/.screens/` (kept between runs, unlike `.results`)
  (`<project>-<locale>-<screen>.png`). After changing a screen, read the phone ones: wrapping,
  overlap, cut-off Sinhala or Tamil text, touch targets, contrast against the brand colours.
- Every test also fails on an uncaught exception, a console error (React, next-intl's missing
  messages) or a 5xx from the API (`e2e/support/test.ts`). 4xx are expected answers and are not
  counted.

## Write a scenario

- Import `test`, `expect`, `pdf`, `scrollsSideways` from `e2e/support/test.ts`.
- `applicant` fixture: a brand-new donor, signed in through the login page. One account per
  test, so tests run in parallel and in any order.
- `createAccount(baseURL, "E2E ...")` signs someone up through the API (OTP read back from
  `notify.Notifications`, where the development SMS sender records every text);
  `grantPlatformRole(account, "PLATFORM_REVIEWER" | "PLATFORM_ADMIN")` makes them staff;
  `apiAs(baseURL, account)` is an API client signed in as them, for steps that have no screen.
- `submittedApplication(api)` makes a submitted application through the applicant API, for
  scenarios that start after the form.
- Step-up (approve, reject, anything `RequiresMfa`): `const before = lastTextId(account.phone)`
  before the click, then `await stepUp(page, account, before)` answers the dialog with the new code.
- Accounts made through the API go to it directly, each with its own `X-Forwarded-For` address
  (loopback is a trusted proxy locally), so a run is not stopped by the per-client signup limit.
  Sign-ins through the screens all come from 127.0.0.1.
- Playwright matches names by substring and ignores case: "Accept" also finds "Do not accept".
  Use `exact: true` for buttons whose words appear inside others'. A toast's text appears twice
  (the toast and its screen-reader copy): take `.first()`.
- Signing in lands a person in their account's language and, with one institution, in that
  institution (A11, B8); make the account in the language under test.
- `field(scope, "Label")` finds a control by its exact label, `(required)` or not.
- Prefer roles and labels (`getByRole`, `field`) over CSS: if a test cannot find a control by
  its name, a screen reader cannot either - fix the screen, not the selector.
- Tag a test `@phone` in its title to also run it at 320 px.
- Name people and institutions `E2E ...`: the global setup clears earlier runs' applications by
  that prefix.
- Cover the unhappy paths a person hits: empty submit, wrong file, a refusal from the API, a
  second tab, going back. Assert what the screen says, in the person's words.

## What the setup does (e2e/support/global-setup.ts)

1. Clears the development Redis rate-limit counters (`rl:*`): every test comes from 127.0.0.1,
   so run after run the suite would otherwise fail on 429s. The limits themselves are covered by
   the API's integration tests.
2. Deletes applications left by earlier runs' `E2E ` accounts (not approved ones, which created
   an institution), with their uploads, outbox and audit rows.

## Not for

- Business rules and security: those are the API's integration tests, mutation-tested. A browser
  test proves a screen uses the API correctly, not that the API is right.
- Pure logic in `web/lib`: Vitest (`npm test`).
