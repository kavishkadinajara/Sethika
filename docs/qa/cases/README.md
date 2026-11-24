# QA test cases — index and how to run

Plan: [`../QA-PLAN.md`](../QA-PLAN.md). Defects: [`../DEFECTS.md`](../DEFECTS.md). Evidence: `../evidence/Fxx/`.

## Running a cycle

```powershell
./scripts/qa/qa.ps1 reset       # dacpac build + Sethika_QA published fresh + Redis db 5 flushed + QA uploads cleared
./scripts/qa/qa.ps1 api         # own terminal: API on :5190 -> Sethika_QA, Redis db 5, Sms:Provider=Console
./scripts/qa/qa.ps1 web         # own terminal: next dev on :3000, /api proxied to :5190
./scripts/qa/qa.ps1 check       # entry criteria: API and web both write to Sethika_QA; no SMS row from a real gateway
./scripts/qa/qa.ps1 personas    # T1-T6 and every persona, through the product (~50 s)
./scripts/qa/qa.ps1 test F01    # QA specs at 1366 px; cases tagged @phone also at 390 and 320 px
```

Settings live in `scripts/qa/qa-env.json`. Environment variables override the API's user-secrets, so the
developer's own settings are never edited. The QA Playwright suite (`web/playwright.qa.config.ts`) runs
one worker and checks the entry criteria again in its global setup before every run.

## The QA world (built by `web/e2e/qa/setup/personas.ts`)

| Key | Tenant | Type | State after setup |
|---|---|---|---|
| T1 | Sri Bodhi Rukkharama (Kandy) | Buddhist Temple | Active, Plus plan requested |
| T2 | Sivan Kovil Jaffna | Hindu Kovil | Active |
| T3 | Sahana Elders' Home (Colombo) | Elders' Home | Active |
| T4 | St. Anne's Children's Home (Galle) | Children's Home | **Suspended** |
| T5 | Al-Noor Mosque (Puttalam) | Mosque | application **Rejected** (NOT_VERIFIABLE) |
| T6 | St. Mary's Church Negombo | Church | application **InfoRequested** (one question) |

People: phones `07701000NN` (P1 01, P2 02, O1 11, M1 12, S1 13, C1 14, L1 15, L2 16, O2 21, O3 31, O4 41,
A1 51, D1 61 … D6 66). The setup prints the full table. Every QA account shares the e2e password.

How each was made: P1 by the `bootstrap` command; everyone else signs up with an OTP; P2 is made a
reviewer by P1 on the platform API (with step-up); T1–T5 apply, P2 reviews (documents, checklist),
P1 approves T1–T4 and rejects T5 (with step-up); T6 applies after T5 and P2 asks it a question;
O1 creates the custom TREASURER role (Manager level) and invites M1, S1, C1, who accept; O1 adds the
leaders L1 (linked to L1's account, which gives L1 T1's LEADER role — the DEF-001 fix — and the setup
checks L1's schedule answers) and L2; M1 adds D6 to the donor book.

**Done in SQL because the product has no path for it** (each one is a finding, listed in the report):

| What | Why there is no product path |
|---|---|
| T1's Plus plan (`RequestedPlanId` set before approval) | Plan selection is deferred (DECISIONS, onboarding "not built") |
| T4's suspension (+ status history row) | Suspending an institution is deferred (same list) |
| C1's deny override on `GIVING.DONATIONS.DELETE` | `auth.UserPermissionOverrides` has no writer (hardening W-list, "document only") |

Cases that lock, reset or end sessions use throwaway accounts named `QA tmp …` (phones `07702…`,
hand-typed sign-ups `07703…`), so the personas stay usable for the later features.

## Cases

| Feature | File | Spec | Cases |
|---|---|---|---|
| F01 Sign-up with phone OTP (P1) | [F01-signup.md](F01-signup.md) | `web/e2e/qa/F01-signup.spec.ts` | 12 |
| F02 Sign-in, OTP sign-in, passwords, sessions (P1) | [F02-signin-sessions.md](F02-signin-sessions.md) | `web/e2e/qa/F02-signin-sessions.spec.ts` | 9 |
| F03 Step-up verification (P1) | [F03-stepup.md](F03-stepup.md) | `web/e2e/qa/F03-stepup.spec.ts` | 8 |
| F04 Institution switching and personal mode (P2) | [F04-switching.md](F04-switching.md) | `web/e2e/qa/F04-switching.spec.ts` | 6 |
| F05 Institution application (P1) | [F05-application.md](F05-application.md) | `web/e2e/qa/F05-application.spec.ts` | 11 |
| F06 Registration review and approval (P1) | [F06-review.md](F06-review.md) | `web/e2e/qa/F06-review.spec.ts` | 9 |
| F07 Platform: institutions, users, support access, template sync (P2) | [F07-platform.md](F07-platform.md) | `web/e2e/qa/F07-platform.spec.ts` | 8 (+1 N/A) |
| F08 Roles, members, invitations, effective permissions (P1) | [F08-access.md](F08-access.md) | `web/e2e/qa/F08-access.spec.ts` | 9 (+1 N/A) |
| F09 Institution profile and public page (P2) | [F09-profile-public.md](F09-profile-public.md) | `web/e2e/qa/F09-profile-public.spec.ts` | 5 (profile edit N/A, OBS-10) |
| F10 Meal slots and blocked dates (P1) | [F10-slots.md](F10-slots.md) | `web/e2e/qa/F10-slots.spec.ts` | 8 (+1 N/A) |
| F11 Donor booking and My bookings (P1) | [F11-booking.md](F11-booking.md) | `web/e2e/qa/F11-booking.spec.ts` | 7 (+1 moved to F18) |
| F12 Booking calendar and live updates (P1) | [F12-calendar.md](F12-calendar.md) | `web/e2e/qa/F12-calendar.spec.ts` | 7 |

F05–F08 add their own applications and accounts on every run: each approval in QA-F06-03 / 04 makes a
new institution named "QA F06 … Home", and the applicants are `QA tmp applicant …` throwaways. A1's
own applications are closed by the case that opened them, so A1 stays under the limit of three. A
reset (`qa.ps1 reset` + `personas`) clears it all.

F09–F12 make their own meals in T1 (and one in T2 / T4), named `QA Fxx … <stamp>`, through
`web/e2e/qa/support/booking.ts`. Each case cancels the bookings still open on them (as the
institution, with a reason), switches the meals off, removes its closed days and puts T1's listing
back as it found it. A meal can only be switched off (OBS-11), so the switched-off ones stay on T1's
slots screen; the donors' cancelled bookings stay in their My bookings. The longest-text edges are
booked by throwaways, never by a persona, so the personas' screens stay readable (DEF-009). Days that
depend on the clock (cancel window, served / did not happen) are reached by moving the slot-day in SQL
(`moveSlotDay`), as the plan triggers jobs instead of waiting.

Run of F09–F12 on 2026-11-24 (`develop` f93f710 + this QA suite): 39 runs (27 at 1366 px, 12 @phone);
final state 32 pass, 7 fail, all on DEF-006 to DEF-009 — DEF-006 (1), DEF-007 (3 widths), DEF-008 (1), DEF-009 (2).
After the fixes (same day) the 7 failing runs were re-run (F10-01 ×3 widths, F10-02, F10-03, F11-04, F12-06 with the whole F12 file): all pass.
Full-page shots of the long slots / My bookings pages (up to 150,000 px tall at phone width, ~20 MB) are
kept cropped to their top 6,000 px, where the case's screen state is.
