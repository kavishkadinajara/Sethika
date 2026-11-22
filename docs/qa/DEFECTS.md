# QA defect log

Severity scale and flow: [`QA-PLAN.md` §8](QA-PLAN.md). A defect is fixed only after it is agreed;
every fix gets a regression test in the normal suites and a mutant, and its QA case is re-run.

| ID | Feature / case | Severity | Status | Summary |
|---|---|---|---|---|
| DEF-001 | F19 / F08 · found by the persona setup | **S2 Major** | Fixed — agreed 2026-11-22, re-run passed | No one in an institution can give a person the `LEADER` role, so a religious leader with an account can never reach their own schedule |
| DEF-002 | F01 · QA-F01-12 | **S3 Minor** | Fixed — agreed 2026-11-22, re-run passed | Sign-up (and every code request) accepts Sri Lankan landlines and pays to text them a code that can never arrive |
| DEF-003 | F03 · QA-F03-08 | **S3 Minor** | Fixed — agreed 2026-11-22, re-run passed | Re-opening the step-up check within a minute shows a red "too many tries, wait 1:00" beside "code sent" and a 0:4x countdown |
| DEF-004 | F05 · QA-F05-11 | **S3 Minor** | Fixed — agreed 2026-11-22, re-run passed | Changing an application's registration number, while answering a question, to one a live application or institution holds answers 500 instead of "already open" |
| DEF-005 | F08 · QA-F08-04 (X1) | **S4 Trivial** | Fixed — agreed 2026-11-22, re-run passed | `GET /api/me/permissions` returns the internal `org.Institutions.Id` (`"institutionId": 1`) |

Observations (not defects — behaviour as designed, recorded for the report's "known limits"):

| ID | Feature | Observation |
|---|---|---|
| OBS-01 | F02 | After a password reset, a password change or "Sign out on all devices", other devices' refresh tokens die at once, but an access token already issued keeps working until it expires (≤ 15 min). Designed (`JwtOptions.AccessTokenLifetime`: "the security stamp is checked on refresh, not on every request"). Suspension is immediate because it bumps `PermissionsVersion`. |
| OBS-02 | setup | No product path for: choosing a plan (T1 Plus), suspending an institution (T4), user-level grant/deny overrides (C1). All deferred in DECISIONS; the plan's F07 "suspend / reactivate tenant" and F08 "plan staff limit" cases will be **N/A**. `org.Plans.MaxStaffUsers` / `MaxMealSlots` are not read by any code, so plan limits are not enforced anywhere. |
| OBS-03 | setup | A custom role at STAFF level cannot hold reports, export or void (`outside_ceiling`), so the plan's "Treasurer" had to be a Manager-level role. Correct by the level ceilings; worth a line in the user guide. |
| OBS-05 | F17 (donor book) · seen while fixing DEF-002 | A standing sponsor in the donor book may have a landline (staff phone an elderly sponsor) and is texted only with text consent — but consent can be ticked on a landline, so reminders would be paid for and never arrive. **Fixed 2026-11-22 at the user's request:** a landline is kept (staff call the sponsor), but ticking "agreed to receive texts" on one is refused (`400 invalid_details ["textConsent"]`, shown under the switch: "A landline cannot receive texts. Untick this, or enter a mobile number."), on add and on edit. The form's hint and phone error no longer say "mobile". Tests: `DonorBookEndpointsTests.ALandlineIsKeptButCannotAgreeToTexts`; e2e `sponsors/donor-book.spec.ts` (landline step); new strings in `docs/translations-to-review.md`. |
| OBS-06 | F05 / F06 | The applicant's texts (submitted, question, approved, not approved) go with the outbox's once-a-minute run, so they arrive up to ~60 s after the action; sign-in codes and bookings wake the outbox and go at once. Acceptable for news about an application; worth one line in the report. |
| OBS-07 | F07 / X5 | The platform's own changes (support entry, account created / suspended / reactivated, platform role given / taken) are recorded only as `PlatformAdministration` rows in `core.OutboxMessages` (marked processed, kept), while approvals, rejections and template syncs write `audit.AuditLogs`. Designed (DECISIONS, access audit: "the audit row is committed first") and complete, but an auditor must read two tables. Option for later: a handler that copies them into `audit.AuditLogs`. |
| OBS-08 | F05 | Every institution type requires the same four DRAFT document types (`org.InstitutionTypeRequiredDocuments`): the per-type rule is tested as a mechanism, not as real requirements (CLAUDE.md open item: confirm with real institutions). |
| OBS-04 | F07 / X1 (to check there) | `auth.vw_EffectivePermissions` returns 41 T1 rows for P1 (platform admin, not a T1 member). Probably the platform's own permissions repeated per institution; to be confirmed in the X1 matrix. |

---

## DEF-001 — No one in an institution can hand out the LEADER role

| | |
|---|---|
| Feature / case | F19 (religious services: leader accepts / declines from their own schedule), F08 (roles); found while building L1 in `web/e2e/qa/setup/personas.ts` |
| Severity | **S2 Major** — a main task cannot be finished: a leader with an account can never open "My schedule" or answer an assignment. Workaround: an administrator answers on the leader's behalf (`RespondedOnBehalf`), which is meant for leaders *without* an account |
| Steps | 1. O1 (T1 Owner) signs in 2. `POST /api/institution/invitations { phone: L1, role: "LEADER" }` (with step-up) |
| Expected | 201: Phase E design V1, "Leaders who do log in get the `LEADER` role in that institution" |
| Actual | `403 {"code":"not_held","permissions":["SERVICES.MYSCHEDULE.EDIT","SERVICES.MYSCHEDULE.VIEW"]}`. The OWNER and MANAGER templates exclude `SERVICES.MYSCHEDULE` (`10_RbacCatalogue.sql` lines 422/424), and anti-escalation lets a person hand out only what they hold, so nobody in an institution can ever invite or assign a leader. Linking a leader to an account (`linkAccount: true`) gives no role either. The e2e specs never met this because they grant LEADER in SQL (`grantInstitutionRole`). |
| Evidence | setup run 2026-11-22 (QA world built at `cb21bdd`); the refusal is recorded by the setup script, which fails if inviting a leader ever starts to work |
| Fix options (for agreement) | (a) Linking a leader record to an account grants that account the institution's LEADER role (and unlinking / deactivating takes it away), inside the leader handler's transaction — matches V1 and needs no anti-escalation exception. (b) Allow owners to hand out LEADER as a named exception to "only what you hold". (a) recommended. |
| Agreed | (a), all three defects, 2026-11-22 |
| Fix | `ServiceSetupHandler.MoveLeaderRoleAsync`: when a leader record's account changes, the old account's LEADER row is deactivated and the new account gets one (a lapsed row reopened), each with a `PermissionsVersion` bump and a `RolePermissionsChanged` outbox row; all tracked, so it saves or fails with the leader (a stale version or `account_already_linked` changes no role). **Changed from option (a):** taking a leader off the roster keeps the role — they may still hold appointments to answer for (V30); only unlinking or relinking removes it. Inviting someone as LEADER stays refused (`not_held`), by design. DECISIONS Q1. |
| Tests | `ServiceSetupEndpointsTests.LinkingALeaderGivesTheirAccountTheirScheduleAndUnlinkingTakesItBack` (link → schedule 200; off roster keeps it; stale save changes nothing; relink moves it; unlink → 403; versions and outbox), `AnAccountAlreadyLinkedGetsNoSecondRole`; e2e `services/services.spec.ts` no longer grants LEADER in SQL; the QA setup gives L1 its role through the link and checks L1's schedule answers 200 |
| Status | Fixed |

## DEF-002 — Landlines accepted for texted codes

| | |
|---|---|
| Feature / case | F01 · QA-F01-12 |
| Severity | **S3 Minor** — no one is harmed and sign-up cannot complete with a landline, but each request is a paid SMS that cannot arrive, and the screen promises "a Sri Lankan mobile number" |
| Steps | `POST /api/auth/otp/request { phoneNumber: "0112345678", purpose: "signup" }` (also `0812234567`, `+94 11 234 5678`) |
| Expected | 400 `invalid_phone_number`, nothing sent |
| Actual | 202 and an SMS row to `+94112345678` / `+94812234567` |
| Cause | `PhoneNumberNormalizer.TryNormalize` checks only `IsValidNumber`, which is true for fixed lines. Its own remark says the check exists to refuse numbers that "can never receive a text message … rather than after paying to send to it". |
| Fix (for agreement) | Also require `GetNumberType` ∈ { MOBILE, FIXED_LINE_OR_MOBILE } (keeps diaspora mobiles, which are by design). The normalizer is shared, so the donor book, leaders and invitations would refuse landlines too — check that no screen needs a landline (the application's *office phone* is a landline and must keep using its own validation). |
| Fix | A second method, `IPhoneNumberNormalizer.TryNormalizeMobile` (MOBILE or FIXED_LINE_OR_MOBILE), used wherever the number is texted: the OTP request (every purpose), sign-up, bootstrap, the platform's "create account", and a leader's phone. `TryNormalize` keeps meaning "a real number, any line": the application's office and contact phones, walk-in donors (never texted) and the donor book keep taking landlines — the donor book refuses text consent on one (OBS-05). DECISIONS Q2. |
| Tests | `PhoneNumberNormalizerTests` (landline valid / not textable, AU, UK and Toronto mobiles textable); `AuthEndpointsTests.ALandlineIsRefusedBeforeACodeIsPaidFor` (signup, reset, login: 400 and no SMS); `SignUpDonorHandlerTests.RefusesALandline_EvenWithATicketForIt`; bootstrap theory +1; admin create-account +1; leader form +1 |
| Status | Fixed |

## DEF-003 — Step-up shows "too many tries" for an ordinary resend cooldown

| | |
|---|---|
| Feature / case | F03 · QA-F03-08 (every RequiresMfa action uses the same dialog) |
| Severity | **S3 Minor** — wrong message; the action still completes with the code sent a moment ago |
| Steps | O1: Staff → Invite someone → Send invitation → the check opens (code sent) → Cancel → Send invitation again within 60 s |
| Expected | The check says a code was just sent and can be used, with the countdown to a new one |
| Actual | Red error "Too many tries. Please wait 1:00 and try again." (si) beside "Code sent…" and a "0:46" countdown — two contradicting messages and two different times |
| Cause | `StepUpForm` asks for a code on every opening; the API's 60 s cooldown answers 429, and `report()` turns any 429 into the form error while the "sent" line stays |
| Evidence | `docs/qa/evidence/F03/QA-F03-08-qa.png` |
| Fix (for agreement) | On opening, treat a 429 whose `Retry-After` ≤ the resend cooldown as "a code is already on its way": no form error, start the countdown, keep the "sent" line. Keep the error for the hourly cap (longer `Retry-After`). |
| Fix | **Sharper than proposed:** the API already tells the two apart (`OtpSendOutcome.CooldownActive` vs `RateLimited`), and the cooldown is read off the live code's own TTL, so in it the earlier code still works. `POST /api/auth/stepup/request` now answers the cooldown `429 code_already_sent` (+ `Retry-After`) and the hourly cap `429 too_many_requests` as before; `StepUpForm` (via `lib/auth/step-up.ts`) shows no error for the first, only the countdown. Guessing from the length of `Retry-After` was avoided: near the end of the hour the cap's wait is also under a minute. The anonymous `/api/auth/otp/request` is unchanged (its answers are kept identical for unknown numbers). DECISIONS Q3. |
| Tests | `StepUpEndpointsTests.AskedAgainInTheCooldownItSaysTheCodeIsAlreadySent_AndThatCodeStillWorks`, `PastTheHourlyCapItIsTooManyRequests_NotACodeAlreadySent`; vitest `step-up.test.ts` (6) |
| Status | Fixed |

## DEF-004 — A registration number changed during clarification crashes the save

| | |
|---|---|
| Feature / case | F05 · QA-F05-11 (the applicant may change details while a question is open, plan 02, 5.3) |
| Severity | **S3 Minor** — the change is refused anyway and nothing is saved (the transaction rolls back), but the applicant sees a server error instead of "An application for an institution with this registration number is already open.", and the API logs an unhandled exception |
| Steps | 1. A temple application is submitted 2. P2 starts the review and asks a question (InfoRequested) 3. The applicant `PUT /api/onboarding/applications/{no}` with `officialRegistrationNo` = T1's (`QA/T1/2011/101`, TEMPLE, Approved) |
| Expected | `409 registration_no_in_use`, as submit answers for the same clash |
| Actual | `500` |
| Cause | `UX_RegReq_ActiveRegNo` is filtered on `Submitted, UnderReview, InfoRequested, Approved`, so an edit in InfoRequested meets it. `ApplicationHandler.SubmitAsync` catches the `DuplicateKeyException`; `UpdateDetailsAsync` (through `ChangeAsync`) does not. The same applies to changing the institution type into a clash |
| Evidence | QA-F05-11 run 2026-11-22: `[500, undefined]` |
| Fix (for agreement) | Catch `DuplicateKeyException` with `KeyName == UX_RegReq_ActiveRegNo` in `UpdateDetailsAsync` → `registration_no_in_use` (the web already has the message). Regression test in `ApplicationEndpointsTests` (InfoRequested edit into a clash → 409, details unchanged) + mutant |
| Agreed | 2026-11-22, as proposed |
| Fix | `ApplicationHandler.UpdateDetailsAsync` wraps its save as `SubmitAsync` does and answers `registration_no_in_use` for that index. DECISIONS Q4 |
| Tests | `ApplicationEndpointsTests.ANumberChangedWhileAnsweringAQuestionToALiveOnesIsRefused_AndNothingChanges` (InfoRequested edit onto a live number → 409, number and status unchanged; a free number still saves). Mutant (the catch's index name changed) killed. QA-F05-11 re-run: pass |
| Status | Fixed |

## DEF-005 — The permissions answer carries the internal institution Id

| | |
|---|---|
| Feature / case | F08 · QA-F08-04; X1 ("internal Id never in URLs or JSON") |
| Severity | **S4 Trivial** — breaks the project rule "never expose internal Id in public APIs" (CLAUDE.md); it tells a staff member only their own institution's row number (and so roughly how many institutions exist). No access follows from it: every route takes slugs and the token carries the institution |
| Steps | Any institution session: `GET /api/me/permissions` |
| Expected | `{ userId, isPlatform, permissions }` — or the institution's slug, if the client needs one |
| Actual | `{"userId":"…","institutionId":1,"isPlatform":false,"permissions":[…]}` |
| Cause | `MeEndpoints.GetPermissionsAsync` returns the cached `PermissionSet` as it is. The web stores it (`PermissionProvider`, `lib/permissions/types.ts`) but nothing reads it |
| Fix (for agreement) | Drop `institutionId` from the response (or send the slug) and from the web type; check the mobile app's type; a test asserting the JSON has no `institutionId` |
| Agreed | 2026-11-22: dropped (no client needs the slug from here) |
| Fix | `MeEndpoints.GetPermissionsAsync` answers `{ userId, isPlatform, permissions }`; `institutionId` removed from `web/lib/permissions/types.ts` and `PermissionProvider`. The mobile app never read it. DECISIONS Q5 |
| Tests | `JwtTokenServiceEndpointTests.TheApiAcceptsAnIssuedTokenAndReadsBackTheSameSessionContext` and `…TheApiReadsThePlatformFlagAndNoInstitution` assert the field is absent (the institution reaching the database is still shown by Alpha's permissions under RLS). Mutant (the field put back) killed by both. Web `tsc` clean, vitest 296/296. QA-F08-04 re-run: pass |
| Status | Fixed |
