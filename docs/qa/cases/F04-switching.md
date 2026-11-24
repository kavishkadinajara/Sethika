# F04 — Institution switching and personal mode

Priority **P2** · Personas M1 (T1 manager + donor), O4, D1, a throwaway in two institutions ·
Spec `web/e2e/qa/F04-switching.spec.ts`. A session carries one institution or none; switching
rotates the refresh token onto the new one; only Active (or Onboarding) institutions can be entered.

| ID | QA-F04-01 |
|---|---|
| Title | A manager who is also a donor moves between T1 and their own account, in Sinhala |
| Persona | M1 · Priority P2 · Technique: state transition |
| Steps | Sign in; switch to personal; switch back (API); on `/si`: context card → My own account → T1 |
| Expected | Sign-in lands in T1; personal: staff API refused, My bookings 200; back in T1; the card reads "…: Sri Bodhi Rukkharama" / "…: Donor" (si) and fits 390/320 px |
| Automated | "QA-F04-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F04-02 |
|---|---|
| Title | Someone in two places chooses one at sign-in and sees only that place's people |
| Persona | throwaway: Staff of T1, Manager of T3 · Priority P2 · Technique: cross-tenant |
| Steps | Sign in (API + page); `/select-institution` → T3; `/access/members`; members API in T3 |
| Expected | No institution chosen for them; both offered; T3's owner listed, no T1 / T2 names on screen or in the JSON |
| Automated | "QA-F04-02" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F04-03 |
|---|---|
| Title | Permissions follow the switch at once |
| Persona | same throwaway · Priority P2 · Technique: state transition, role × action |
| Steps | T1 → members API; switch to T3 → members; back to T1 → members; reuse the rotated T3 token |
| Expected | 403, 200, 403; the rotated token is refused |
| Automated | "QA-F04-03" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F04-04 |
|---|---|
| Title | A session never moves into a place the person is not in, or one that is suspended |
| Persona | D1, O4 (owner of suspended T4) · Priority P2 · Technique: error guessing, state (Suspended) |
| Steps | D1 switches to T1 and to a made-up slug; O4 signs in, switches to T4, calls a staff API; O4 opens `/select-institution` |
| Expected | 403 `institution_not_available`; O4 gets no institution and T4 is not offered; staff API refused; the page says "You aren't a member of any institution…" |
| Automated | "QA-F04-04" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F04-05 |
|---|---|
| Title | Deep links to staff and platform screens from the wrong session show nothing and the API refuses |
| Persona | M1 in personal mode, D1 · Priority P2 · Technique: role × action |
| Steps | Staff and platform APIs from M1's own account; D1 opens `/access/members`, `/admin/registrations`, `/bookings/calendar` |
| Expected | APIs refused; no T1 people, no application numbers, no approve buttons on the screens; no browser error or 5xx |
| Automated | "QA-F04-05" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F04-06 |
|---|---|
| Title | Taken out of a place mid-session, the person loses it at the next request and cannot switch back |
| Persona | throwaway, O3 · Priority P2 · Technique: state transition |
| Steps | Throwaway in T3 calls members (200); O3 removes their Manager role (step-up); the live token calls again; sign in; switch to T3 |
| Expected | 403 on the live token; T3 no longer offered; switch → 403 `institution_not_available` |
| Automated | "QA-F04-06" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |
