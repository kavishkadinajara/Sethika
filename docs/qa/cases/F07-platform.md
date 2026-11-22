# F07 — Platform: institutions, users, support access, template sync

Priority **P2** · Personas P1 (Anjali Herath, PLATFORM_ADMIN), P2 (Tharindu Bandara, PLATFORM_REVIEWER),
institution personas for the refusals, throwaways for every account change ·
Spec `web/e2e/qa/F07-platform.spec.ts`. The platform's own changes are recorded as
`PlatformAdministration` outbox rows (OBS-07), which is where these cases read them.

| ID | QA-F07-01 |
|---|---|
| Title | P1 sees every institution with its status and owner; the filters hold; applications are not institutions |
| Persona | P1, P2 · Priority P2 · Technique: equivalence partitions (filters), boundary (page size 101, page 0, a 31-character type) |
| Steps | API list, `status=Suspended`, `district=KDY`, bad filters; P2 lists; screen: search "Sri Bodhi" |
| Expected | T1–T4 listed, T5 / T6 not; Suspended holds only T4; Kandy holds T1 not T2; bad filters 400; no numeric `id` in the JSON; P2 200; the card reads "Owner: Ven. Ananda"; nothing clipped or scrolling sideways at 1366 / 390 / 320 px |
| Automated | "QA-F07-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 |

| ID | QA-F07-02 |
|---|---|
| Title | P1 enters T1 as platform support only after a step-up; it is recorded, shown, and ends on Leave |
| Persona | P1, P2 · Priority P2 · Technique: state transition, role × action |
| Steps | Institutions → Open as platform support → step-up dialog → members → `/admin/users` → Leave; P2 (stepped up) asks for a ticket |
| Expected | The gold banner names T1; one `SupportTicketIssued` record; T1's people visible; platform screens closed inside; Leave returns to `/admin/institutions` with no banner; P2 403 and no new record |
| Automated | "QA-F07-02" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F07-03 |
|---|---|
| Title | A support ticket is one-use, for one institution, live ones only, and nothing without it |
| Persona | P1 · Priority P2 · Technique: state transition (ticket: none → issued → spent), error guessing |
| Steps | Switch into T1 with no ticket; tickets for suspended T4 and a made-up slug; a T1 ticket; switch into T3 with it; switch into T1; members; out to personal; into T1 again |
| Expected | 403 `institution_not_available`; 404, 404; 204; T3 refused; T1 200 and members 200; after leaving, the spent ticket is refused (403) |
| Automated | "QA-F07-03" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F07-04 |
|---|---|
| Title | P1 makes an account for someone who cannot sign up; it signs in with a texted code |
| Persona | P1, a new person ("QA Sunil Gamage") · Priority P2 · Technique: equivalence partitions (name, phone, language), state transition (PendingVerification → Active) |
| Steps | Create without a step-up; with a blank name, a landline, "12345", language "fr", D1's number; with `+94 7x xxxxxxx`; the same number again; they sign in at `/login/otp` |
| Expected | `stepup_required`; `invalid_full_name`, `invalid_phone_number` ×2, `unsupported_language`, `409 already_registered`; 201 with no numeric id; `already_registered`; stored `PendingVerification`, `si`, E.164; one `UserCreated` record; after the code, Active |
| Automated | "QA-F07-04" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F07-05 |
|---|---|
| Title | Suspending an account ends its live session at the next request and its sign-in; reactivating restores both |
| Persona | P1, throwaway · Priority P2 · Technique: state transition (Active → Suspended → Active) |
| Steps | Throwaway signed in calls My bookings; P1 suspends (step-up), an unknown id, their own account; the live token, its refresh token, a new sign-in; P1 reactivates; sign-in |
| Expected | 200 before; `stepup_required`, 204, 404, `403 own_account`; afterwards the live token, the refresh and sign-in all refused; one `UserSuspended` record; reactivated → sign-in 200, one `UserReactivated` record |
| Automated | "QA-F07-05" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F07-06 |
|---|---|
| Title | Platform roles: P1 hands out and takes back only what is junior to their own; P2 hands out nothing |
| Persona | P1, P2, throwaways · Priority P2 · Technique: role × action, rank |
| Steps | P1 grants PLATFORM_ADMIN, an unknown role, PLATFORM_REVIEWER; the new reviewer opens the queue; P2 (stepped up) grants, suspends, creates; P1 revokes while the reviewer's token is live; P1 suspends a peer administrator |
| Expected | `rank_not_junior`, `404 role_not_found`, 204; queue 200; P2 403 ×3; revoked → the live token 403 at once; peer `403 rank_not_junior` |
| Automated | "QA-F07-06" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F07-07 |
|---|---|
| Title | Template sync adds what an institution is missing, and nothing twice |
| Persona | P1, P2 · Priority P3 · Technique: idempotence |
| Steps | Sync T1 without a step-up; sync T2 twice; a made-up slug; P2 syncs |
| Expected | `stepup_required`; `{modules: 0, roles: 0, grants: 0}` both times, T2's roles and grants unchanged; 404; P2 403 |
| Automated | "QA-F07-07" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F07-08 |
|---|---|
| Title | Suspend / reactivate an institution |
| Result | **N/A** — no product path (deferred, DECISIONS; OBS-02). T4 is suspended in SQL by the setup; what a suspended tenant can and cannot do is covered by QA-F04-04 and QA-F08-07 |

| ID | QA-F07-09 |
|---|---|
| Title | No one outside the platform reaches its routes |
| Persona | O1, M1, D1, O2, A1, anonymous · Priority P2 · Technique: role × action matrix (5 personas × 6 routes) |
| Steps | Institutions, statuses, users, support, sync, create user; O1 opens `/si/admin/users` |
| Expected | 403 each, 401 anonymous; the screen shows no accounts |
| Automated | "QA-F07-09" |
| Result | Pass (1366 px) — run 2026-11-22 |
