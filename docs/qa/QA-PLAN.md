# Sethika — System QA Test Plan

| | |
|---|---|
| Version | 1.0 (2026-11-22) |
| Build under test | `develop` at the start of each cycle (commit recorded in every run) |
| Goal | Confirm, before the final viva on **2026-12-03**, that every feature works for the people it was built for, on web and on the Android app |
| Owner | Kavishka Dinajara (developer + QA for this project) |

## 1. Why this plan exists

The code already has a large automated suite (counts from the 2026-11-07 full run): .NET 1,599 (domain
420, application 99, integration 1,080 against real SQL Server and Redis), web vitest about 280, mobile
Jest 629, and 172 Playwright e2e runs, with mutation checks on every slice. Those
tests were written **slice by slice by the person who built each slice**. They prove that each piece does
what its plan said. They do not prove that:

- a real person in a real role can finish their whole job from start to end, across modules;
- the features still work **together** on one clean database, in the order a real institution uses them;
- what one role does is correct from the other side too (a donor books → staff see it → reminder goes);
- every role is refused everything it must not do, in every module;
- the Android app works as a **release build on a phone**, not only in Expo Go.

This plan adds **persona-based system testing**: test cases written from each user's point of view,
run on a clean QA database, with evidence kept for every case.

## 2. Scope

### In scope
All features listed in section 6 (F01–F26), on:
- **Web**: Chromium desktop (1366×768), phone width (390×844 and 320×640), light and dark, en / si / ta.
- **Android**: release APK (EAS `preview` profile) on a real phone, through the HTTPS tunnel.
- **API**: permission and tenant checks are tested through the API directly as well as through the UI,
  because hiding a button is UX only.

### Out of scope (and why)
| Item | Reason |
|---|---|
| iOS app | no Apple developer account; Android is the course target |
| Real SMS delivery | Text.lk delivery was proven on 2026-11-07 (`SethikaWalk` DB); QA uses the Console provider so no real person is texted |
| Live PayHere payments | merchant is sandbox only; sandbox is in scope |
| Load / performance testing | one-machine dev setup, low RAM; only a smoke check of response times |
| Azure / Vercel production | not deployed; tested locally through the tunnel |
| Translation **quality** | needs native si/ta reviewers (`docs/translations-to-review.md`); QA checks only that no English or raw keys leak and nothing overflows |

## 3. Approach

**Risk-based.** Every feature gets a priority (section 6). P1 features are tested first and most deeply,
and are re-run in the final regression. Highest risk = money, overbooking, tenant isolation, permissions,
onboarding approval.

**Test design techniques used (named in every test case):**
| Technique | Used for |
|---|---|
| Equivalence partitions + boundary values | capacity (MaxSponsors, last place), dates (today, past, booking horizon, blocked day), amounts (0, 1, max), text lengths, phone formats |
| State transition | booking, registration request, appointment, pledge, donation (void), online payment, standing sponsorship, account (active / suspended) |
| Decision table | effective permissions: role grant × module enabled × grant override × deny override (deny wins), step-up needed or not |
| Role × action matrix | every persona tried against every screen and every API route of its module (allowed and refused) |
| Concurrency | two donors on the last place; double-click submit; two tabs refreshing the session |
| Cross-actor flow | one action by role A checked from role B's screen (and SMS / push / live update) |
| Error guessing / exploratory | 20-minute charter per P1 feature, notes kept in the run log |

**Levels.** Existing unit, integration and e2e tests stay as they are. This plan adds:
1. **QA Playwright suite** `web/e2e/qa/` — one spec per feature, IDs match the test cases, runs on the
   QA database only, screenshots kept as evidence.
2. **API negative checks** inside the same specs (`apiAs(...)`) for refusals and cross-tenant reads.
3. **Manual mobile cases** on the APK, with screenshots from the phone.

## 4. Test environment

| Part | Setting |
|---|---|
| Database | **`Sethika_QA`** on local SQL Server 2022, published fresh from `src/Sethika.Database` (dacpac + seeds). Reset = re-publish with `CreateNewDatabase`. Never the dev `Sethika` DB |
| Redis | WSL2 Redis, its own logical db (not the dev or test db), flushed on reset |
| API | `Sethika.Api` with `ConnectionStrings` and Redis pointed at the QA db by environment variables; user-secrets untouched |
| SMS | `Sms:Provider = Console` — **checked before every run**. Codes read from `notify` rows by `latestCode()` |
| Push | Expo push for the APK (only when the FCM key is set, see section 10); Console gateway otherwise |
| Payments | PayHere **sandbox** through the ngrok static domain (notify callback must reach the API) |
| Web | `next dev` on :3000, `--workers=1` (low RAM; 2 workers is a known flake, see section 11) |
| Tunnel | `https://smock-eccentric-relatable.ngrok-free.dev` → web :3000 → `/api/*` rewrite → API |
| Mobile | release APK built with `EXPO_PUBLIC_API_BASE_URL` = the tunnel URL |
| Clock | Asia/Colombo dates; jobs (reminders, recurring, standing, memorial) triggered from the Hangfire dashboard instead of waiting |

## 5. Test data — personas

All data is **fictional** and created **through the product** (sign-up with OTP, application, platform
approval), not by SQL inserts, so the setup itself is the first end-to-end test. One setup script
`web/e2e/qa/setup/personas.ts` builds them in this order and prints the table below with real phone numbers.
Password for every QA account: kept in the script only.

### Institutions (tenants)
| Key | Institution | Type | Plan | Purpose |
|---|---|---|---|---|
| T1 | Sri Bodhi Rukkharama (fictional) | Buddhist Temple | Plus | main tenant: meals, services, giving, standing sponsors |
| T2 | Sivan Kovil Jaffna (fictional) | Hindu Kovil | Free | **second tenant**, for isolation tests; Tamil-first staff |
| T3 | Sahana Elders' Home (fictional) | Elders' Home | Free | care home without religious services; needs board |
| T4 | St. Anne's Children's Home (fictional) | Children's Home | Free | **suspended** after approval, to test what a suspended tenant can and cannot do |
| T5 | Al-Noor Mosque (fictional) | Mosque | — | application **rejected**; T6 church application left **in clarification** |

### People
| Key | Persona | Account / role | Language | Thinks like |
|---|---|---|---|---|
| V1 | Visitor | not signed in | en | "I just want to see where I can give dana this Poya" |
| D1 | Nimal Perera | donor, books often, has memorial plan | si | regular temple donor, uses web on a laptop |
| D2 | Fathima Rizvi | donor, mobile app only | ta | books from her phone, wants reminders |
| D3 | Ruwan Silva | **new** — signs up during the test | en | first-time user, makes mistakes in forms |
| D4 | Kamal Jayasinghe | donor who is **suspended** mid-test | en | tries to keep using a stale session |
| D5 | Malini Fernando | co-sponsor / shared-link receiver | en | gets a share link from D1 |
| D6 | Saman Dias | standing sponsor added by T1 staff **without an account**, links later | si | only gets SMS until he signs up |
| O1 | Ven. Ananda (owner of T1) | T1 Owner | si | approves everything, sets up roles |
| M1 | Dilani (manager of T1) | T1 Manager | si | runs the calendar and donations day to day |
| S1 | Kasun (staff of T1) | T1 Staff | si | kitchen helper; can see, cannot approve |
| C1 | Treasurer of T1 | **custom role** "Treasurer": giving only, + deny override on void | en | maker-checker partner of M1 |
| L1, L2 | Ven. Sumana, Ven. Dhamma | religious leaders of T1 (L1 has an account) | si | accept / decline assignments, take leave |
| O2 | Selvam (owner of T2) | T2 Owner | ta | other tenant: must never see T1 data |
| O3 | Owner of T3 | T3 Owner | en | needs and pledges |
| O4 | Owner of T4 | T4 Owner (suspended tenant) | en | tries to invite staff / take bookings |
| A1 | Applicant for T5 / T6 | donor who applies | en | fills the long form, uploads documents |
| P1 | Platform admin | PLATFORM_ADMIN | en | approves, suspends, support access |
| P2 | Platform reviewer | PLATFORM_REVIEWER | en | reviews but **cannot approve** |

## 6. Features under test

Priority: **P1** = money, capacity, security, onboarding approval (any failure blocks the viva demo);
**P2** = core daily use; **P3** = secondary. Every feature gets its own case file
`docs/qa/cases/Fxx-*.md` and spec `web/e2e/qa/Fxx-*.spec.ts`.

| ID | Feature | Pri | Main personas | Key risks to attack |
|---|---|---|---|---|
| F01 | Sign-up with phone OTP | P1 | D3, V1 | wrong / expired / reused code, rate limit, duplicate phone, phone formats (07x, +947x, spaces) |
| F02 | Sign-in, OTP sign-in, forgot / change password, sessions | P1 | D1, D4, M1 | wrong password lockout, refresh rotation, reuse detection, logout-all, suspended account with live session |
| F03 | Step-up verification (RequiresMfa permissions) | P1 | O1, P1, C1 | every MFA permission asks; code expiry; step-up not reusable across users |
| F04 | Institution switching and personal mode | P2 | M1 (staff of T1 + donor), O2 | stale permissions after switch, wrong tenant in header, deep links |
| F05 | Institution application (apply, documents, declaration, withdraw, clarifications) | P1 | A1 | required documents per type, file type / size, draft save, submit twice, withdraw, answer a clarification |
| F06 | Registration review and approval | P1 | P1, P2 | checklist, clarification loop, approve = one transaction (roles cloned, owner added, modules), reject, reviewer cannot approve |
| F07 | Platform: institutions, users, support access, template sync | P2 | P1 | suspend / reactivate tenant and user, support access needs step-up and is audited, create account for someone without a phone app |
| F08 | Roles, members, invitations, effective permissions | P1 | O1, M1, C1 | anti-escalation, rank (cannot suspend a senior), deny wins, cache version bump, invitation to suspended institution, plan staff limit |
| F09 | Institution profile and public page `/i/[slug]` | P2 | O1, V1 | edit reflected publicly, unlisted / suspended not public, slug not Id |
| F10 | Meal slots and blocked dates | P1 | M1 | MaxSponsors boundaries, plan slot limit, block a day that has bookings, edit with RowVersion conflict |
| F11 | Donor booking and My bookings | P1 | D1, D2, D3 | last place race (two donors), past / blocked / beyond-horizon dates, cancel rules, regular-day refusal, double submit |
| F12 | Booking calendar (staff) and live updates | P1 | M1, S1 | approve / reject / complete / no-show / cancel by permission, S1 refused, SignalR update in a second browser |
| F13 | Yearly (recurring) bookings | P2 | D1 | generation job, clash with a full day next year, cancel one year vs all |
| F14 | Co-sponsors and share links | P3 | D1, D5 | token guessing, expired / revoked link, co-sponsor counts against capacity |
| F15 | Memorial dana planner and reminders | P2 | D1 | day-of-passing maths (7th day, 3 months, 1 year), reminder job dedup, archive |
| F16 | My places (favourites) | P3 | D1 | only own rows (RLS fn_Owner) |
| F17 | Discovery: home, `/find`, near me, map | P2 | V1, D2 | GEO radius, filters by type / district / date, empty results, unlisted hidden, map pins match list |
| F18 | Standing sponsors (donor book, patterns, day requests, skip / swap / block, linking) | P2 | M1, D6 | pattern engine dates, nightly job, block cancels only standing days, own skip, link at sign-up |
| F19 | Religious services (types, leaders, hours, leave, requests, assignment, accept / decline, schedule) | P2 | D1, M1, L1, L2 | conflict-free assignment, leader on leave, decline → reassign, T3 has no services module |
| F20 | Donations, receipts, void (maker-checker), export | P1 | M1, C1 | voider ≠ recorder, void needs step-up, receipt numbers never reused, never deleted, CSV injection in export |
| F21 | Needs board and pledges | P2 | O3, D1 | pledge more than needed, receive / cancel pledge, closed need |
| F22 | Online giving (PayHere sandbox) and merchant settings | P1 | D1, O1 | signature check on notify, amount tamper, replayed notify, return page states, merchant secret needs step-up |
| F23 | Monthly report and dashboards | P2 | O1, C1 | totals match the donations, voided excluded, export / print, other tenant's numbers never shown |
| F24 | Notifications (SMS templates, reminders, outbox, push) | P1 | D1, D2, D6 | sent once (DedupKey), right language, failed SMS resent, no SMS for cancelled booking |
| F25 | Languages en / si / ta | P2 | all | switch keeps the page, no raw keys, no overflow at 320 px, dates and numbers formatted |
| F26 | Android app (release APK) | P1 | D2, S1 | sign-in, explore, book, my bookings, near me + map, push, Today tab "mark served", profile, passwords, offline / bad network |

### Cross-cutting suites (run against every feature, results recorded per feature)
| ID | Suite | What is checked |
|---|---|---|
| X1 | Security | role × action matrix through API; cross-tenant IDs (another tenant's `PublicId` / `BookingNo`); internal `Id` never in URLs or JSON; security headers; rate limits |
| X2 | Responsive | 1366, 390, 320 px: no horizontal scroll, sheets usable, tap targets |
| X3 | Dark mode | contrast of text, badges and calendar states |
| X4 | Accessibility (basic) | keyboard-only path for the main task, labels on inputs, focus visible, axe scan with no serious issues |
| X5 | Audit and data integrity | audit rows written for sensitive actions, temporal history kept, money rows never deleted |
| X6 | Resilience | API restarted mid-flow, Redis unavailable at booking (must refuse, not overbook), slow network on mobile |

## 7. Test case format

Each case in `docs/qa/cases/Fxx-*.md`:

```
| ID | QA-F11-07 |
| Title | Two donors take the last place at the same time |
| Persona | D1 + D2 |  Priority | P1 |  Technique | Concurrency / boundary |
| Pre-conditions | T1 slot "Morning dana" MaxSponsors = 2, one place taken for <date> |
| Steps | 1. D1 and D2 open the day  2. both press Book within 100 ms |
| Expected | exactly one succeeds; other sees "This day is full"; BookedSponsors = 2; one SMS each side |
| Automated | web/e2e/qa/F11-booking.spec.ts › "QA-F11-07" |
| Result | Pass / Fail (DEF-xxx) / Blocked — run date, commit |
```

## 8. Defects

Logged in `docs/qa/DEFECTS.md` as `DEF-001…` with: feature, case ID, steps, expected, actual, evidence,
severity, status.

| Severity | Meaning |
|---|---|
| **S1 Critical** | wrong money, overbooking, data of another tenant visible, permission bypass, data loss |
| **S2 Major** | a main task cannot be finished, wrong data shown, SMS / reminder wrong or missing |
| **S3 Minor** | works with a workaround; wrong message; layout broken on one width or language |
| **S4 Trivial** | cosmetic, wording |

Flow (agreed 2026-11-22): **feature by feature** — run the feature's cases, show the defects with
severity, fix only after agreement, re-test the fix (and its mutant), then move to the next feature.
Every fix gets a regression test in the normal suites, not only in `qa/`.

## 9. Entry and exit criteria

**Entry (per cycle):** `develop` builds; `Sethika_QA` published fresh; `Sms:Provider = Console` confirmed;
persona setup script passes.

**Exit (for the viva):**
- every P1 and P2 case executed; P3 at least happy path;
- **no open S1 or S2** defect;
- S3/S4 defects either fixed or listed in the QA report as known;
- final regression of all P1 cases on the last commit: 100 % pass;
- APK passes F26 on a real phone;
- QA report `docs/qa/QA-REPORT.md` written (cases run / passed / failed, defects by severity, known limits).

## 10. Android APK

| Step | Detail |
|---|---|
| Account | free Expo account; `npx eas-cli@latest login` (run by the owner, interactive) |
| Project | `eas init` writes `extra.eas.projectId` into `app.json` |
| Profile | `eas.json` → `preview`: `android.buildType = "apk"`, `distribution = "internal"`, env `EXPO_PUBLIC_API_BASE_URL` = tunnel URL (EAS does not upload the git-ignored `.env.local`) |
| Build | `npx eas-cli@latest build -p android --profile preview` (cloud build, free queue) |
| Push (optional) | `google-services.json` + FCM V1 key uploaded to Expo; without them the APK works but push is untested (F24/F26 push cases → Blocked) |
| Demo condition | laptop runs API + web + ngrok; the phone needs internet only |

## 11. Risks and known limits

| Risk | Effect | Handling |
|---|---|---|
| Low free RAM (~3 GB) | dev server OOM, SQL Server login timeouts under load | `--workers=1`, close other apps, build `-m:1` |
| 2-worker e2e "Loading" flake (open since 11-07) | false failures | run QA at 1 worker; investigate as its own defect if it appears at 1 |
| All traffic through the Next proxy | API sees one client IP → rate limits shared by all personas | `clearRateLimits()` between features; recorded as a deploy-time item (A18) |
| ngrok free domain | interstitial page for browser user agents | mobile sends its own User-Agent; web demo uses localhost |
| No real institution reference data | memorial rules, special days are DRAFT | cases check behaviour, not the religious correctness of the drafts |
| Time | 11 days to the viva, other final items open | P1 first; P3 can drop to happy path |

## 12. Schedule

| Date | Work |
|---|---|
| 11-22 | this plan; APK setup; `Sethika_QA` + persona setup script |
| 11-23 | F01–F04 (identity, sessions, step-up) + X1 matrix tooling |
| 11-24 | F05–F08 (onboarding, approval, platform, access) |
| 11-25 | F09–F12 (profile, slots, booking, calendar, live) |
| 11-26 | F13–F18 (recurring, sharing, memorial, places, discovery, standing) |
| 11-27 | F19–F23 (services, giving, payments, report) |
| 11-28 | F24–F26 (notifications, languages, APK on phone) |
| 11-29 | defect fixing buffer |
| 11-30 | full P1 regression on the final commit; full `dotnet test` + e2e |
| 12-01 | `QA-REPORT.md`; merge to `main` |
| 12-02 | freeze; demo rehearsal with the personas |
| 12-03 | viva |

## 13. Deliverables

- `docs/qa/QA-PLAN.md` (this file)
- `docs/qa/cases/Fxx-*.md` — test cases per feature
- `web/e2e/qa/` — persona setup + one spec per feature (QA database only)
- `docs/qa/DEFECTS.md` — defect log
- `docs/qa/evidence/` — screenshots (phone screenshots for F26)
- `docs/qa/QA-REPORT.md` — summary for the report and the viva
