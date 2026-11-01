# Phase E delivery: 8.5 and 8.7 to 8.10

This is the description the pull request for these slices would have carried. The branch
`phase-e-donor` was tracking `origin/develop`, so the push on 2026-11-01 went straight to `develop`
and there was no pull request to write it in. The commits are:

| Commit | Slice |
|---|---|
| a6749ec | 8.5 Donor requests |
| 1308fab | 8.7 Assigning and answering |
| c9790f5 | 8.8 Texts and reminders |
| b002ff5 | 8.9 Screens |
| d9f1234 | 8.10 Decision log, plan, shell e2e spec |

Every check below was run on these commits before the push.

Phase E (`docs/plans/PHASE-E-RELIGIOUS-SERVICES.md`), slices 8.5 and 8.7 to 8.10. These are the parts of religious services that people use: a donor asks for a service, the institution assigns its leaders, a leader answers, and everyone gets texts and reminders. 8.1-8.4 and 8.6 (schema, roster, leave and the assignment engine) were merged in PR #12.

## What

**8.5 Donor requests.**
- An institution's active services appear on its public page, `GET /api/institutions/{slug}` (V25). This adds no new anonymous endpoint.
- `POST /api/appointments` creates a request. The donor sends a Colombo date and a start time, and `SriLankaTime.ToUtc` turns them into UTC (V28). Each request gets an `APT-2026-000123` number. It is refused when `SERVICES` is off for the institution (V26). Limit: 10 requests per donor per hour (V29).
- `GET /api/appointments/mine` lists the donor's requests. `POST /api/appointments/{no}/cancel` cancels one until the end of the day before (V13).
- `AppointmentCancelledHandler` frees the leaders' time through the outbox for every cancellation (V16, V27).

**8.7 Assigning and answering.**
- `InstitutionAppointmentEndpoints`:
  - the agenda, `GET /api/institution/appointments?view=upcoming|waiting|needs-leaders|history` (V32);
  - assigning and removing leaders;
  - answering for a leader who has no account;
  - reject, cancel and complete.
- A leader with an account uses `GET /api/institution/schedule` and answers through `POST .../schedule/{no}/respond` (V33).
- All changes go through `AppointmentDecisionHandler` (V30, V31). They take no Redis lock, because they only free time. Every change writes the appointment row, so two changes at once always meet on `RowVersion` and the loser gets 409 `stale`.

**8.8 Texts and reminders.**
- 27 templates in `11_NotifyTemplates.sql`: 9 texts, each in en, si and ta. The si and ta drafts are listed in `docs/translations-to-review.md`.
- `AppointmentSmsHandler` sends them. A leader without an account is texted at the roster's phone (`IUserTextMessages.SendToPhoneAsync`).
- The `appointment-reminders` Hangfire job runs at 18:00 Colombo the day before.
- Who is texted, and the `DedupKey`s, are V35-V39.

**8.9 Screens** (en/si/ta):
- On `/i/[slug]`: the services the place offers, and the request sheet. It has a date calendar, a start-time list, a choice of place, and an address field when the service is at home.
- `/services/mine` for the donor.
- For the institution:
  - `/services/types`;
  - `/services/leaders`: the roster, each leader's usual week and their leave;
  - `/services/appointments`: the agenda with its four views, the assign sheet and reasons.
- `/services/schedule` for a leader.
- The sidebar has new items under Giving and under the institution.
- The booking sheet's sign-in gate is now a shared `DonorSheetGate`.

**8.10.**
- The shell e2e spec now expects "My service requests" in the donor's menu.
- `docs/DECISIONS.md` gets a "Religious services (Phase E)" section (V1-V39 in summary, plus the cut list).
- The Plans table is updated. Plans 04 and 05 had no rows, so they are added.

## Why

Proposal v3 promises Phase E after the supervisor's feedback (S4). All six promises in plan §0a are now built, and nothing on the plan's cut order had to be cut. The overlap guarantee (V2) is the report's second concurrency pattern, beside the booking engine's counted inventory. A leader is never committed to two places at once: the primary key on `LeaderTimeBlocks` stops it even when the Redis lock is bypassed.

## How to verify

1. Publish the dacpac to the dev database (`docs/LOCAL_SETUP.md` §3). The new templates are seed MERGEs.
2. Make sure User Secrets have no `Sms:Provider` (Console SMS), then run `cd web && npm run e2e`.
3. By hand: approve a Buddhist temple and open `/services/types` to add a service and `/services/leaders` to add a leader. As a donor, open `/i/{slug}` and use "Ask for this service". As the owner, assign a leader on `/services/appointments`. The donor's `/services/mine` then says Confirmed. The texts appear in `notify.Notifications`.

## Tests

- **.NET integration, against real SQL Server and Redis:**
  - `DonorAppointmentEndpointsTests` (21): the cancel race is forced with `UPDLOCK`.
  - `InstitutionAppointmentEndpointsTests` (15): the cancel/decline race is forced with `UPDLOCK`.
  - `AppointmentSmsTests` (9).
  - `HangfireJobsTests`: the new job.
- **Domain:** `SriLankaTimeTests` (9).
- **Mutation runs:** D1-D22 (8.5), E1-E20 (8.7) and F1-F17 (8.8) are all killed. D10, D19, E13 and E14 survived at first, because RLS was what hid the rows. Tests in an institution session and in a platform session now kill them.
- **Web:** `lib/services/time` (8 unit tests) and 2 new `nav` tests. Typecheck and lint are clean.
- **e2e (Playwright):** `e2e/services/services.spec.ts` runs 5 scenarios; 3 of them also run at 320 px. 8/8 pass.
  - The scenarios cover the whole flow, a leader accepting and then declining, a reject with a reason, a donor cancelling, the usual week, and leave refused over an appointment.
  - The si/ta screens and the request form were checked for clipping, and the screenshots were read.
- **Full `dotnet test` on b002ff5:** 973/973 pass (Domain 227, Application 71, Integration 675; the integration run took 53 min). The commit after it, d9f1234, adds no C# code.
- **Full e2e:** 87/92 passed. The 5 failures were 3 shell tests (desktop and phone) that still counted 4 items in the donor's menu; "My service requests" is the fifth. The spec is updated, and `e2e/shell/shell.spec.ts` now passes 22/22.
- **Other checks:** `dotnet format --verify-no-changes`, `npm run lint`, `npm test` (196) and `npm run build` are clean. A second `SqlPackage /Action:Script` against the dev database shows seed MERGEs only.

## Known and left for later

- `READ_COMMITTED_SNAPSHOT ON` is still planned for after Phase E (see PR #31). The concurrency tests that assume it is off must be checked again then.
- On a fresh dev server, Next sometimes answers 500 with "Unexpected end of JSON input". It reads `prerender-manifest.json` while another first compile is writing it (`next-dev-server.js`). It is dev only and passes on a rerun.
- Two old gaps on the public page in Tamil: the theme switcher's "தானியங்கி" fills its pill, and the institution type label is still in English.
