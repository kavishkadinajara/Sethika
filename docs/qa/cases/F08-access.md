# F08 — Roles, members, invitations, effective permissions

Priority **P1** · Personas O1 (Owner), M1 (Manager), S1 (Staff), C1 (custom TREASURER at Manager level,
deny override on `GIVING.DONATIONS.DELETE`), L1 (Leader) of T1; O2, O3, O4 of other tenants; throwaways
for every membership change · Spec `web/e2e/qa/F08-access.spec.ts`. Effective permissions =
(role grants ∩ enabled modules) + grant overrides − deny overrides; deny wins.

| ID | QA-F08-01 |
|---|---|
| Title | O1 sees T1's roles and people on the access screens, in Sinhala, at every width |
| Persona | O1 · Priority P1 · Technique: happy path, X2 responsive |
| Steps | `/si/access/members`; `/si/access/roles` |
| Expected | M1, S1, C1, L1 listed, no T2 / T3 people or donors; roles හිමිකරු, කළමනාකරු, කාර්ය මණ්ඩලය, ආගමික නායකයා and "Treasurer" (the name O1 gave it); no sideways scroll at 1366 / 390 / 320 px |
| Automated | "QA-F08-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 |

| ID | QA-F08-02 |
|---|---|
| Title | Anti-escalation: a role is made only below one's rank, inside its level, and of permissions one holds |
| Persona | M1, O1 · Priority P1 · Technique: decision table (level rank × ceiling × held) |
| Steps | M1 creates at MANAGER / OWNER; STAFF with REPORTS.VIEW (outside STAFF), MYSCHEDULE.VIEW (inside, not held), a platform permission, an unknown one; unknown level; bad code; TREASURER again; blank name; then a good STAFF role, and widens it (without, then with a step-up); O1 tries a platform permission |
| Expected | `rank_not_junior` ×2, `outside_ceiling` (+ the permission named), `not_held` (+ named), `outside_ceiling`, `400 unknown_permission`, `unknown_level`, `invalid_role_code`, `409 role_exists`, `invalid_role_name`; no role made; the good one 201 with exactly its two permissions; widening `stepup_required` then `not_held`; O1 403 |
| Automated | "QA-F08-02" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F08-03 |
|---|---|
| Title | Rank: nobody hands out, or takes away, a role at or above their own, nor their own roles |
| Persona | M1, O1, a throwaway helper (STAFF) · Priority P1 · Technique: role × action, rank |
| Steps | M1 gives MANAGER / TREASURER, takes O1's OWNER / C1's TREASURER / own MANAGER, gives self LEADER, gives D1 STAFF, takes an unheld role, uses an unknown role, invites as MANAGER, takes STAFF from the helper; O1 takes own OWNER |
| Expected | `rank_not_junior` ×4, `own_roles` ×2, `member_not_found`, `assignment_not_found`, `role_not_found`, invite `rank_not_junior`; taking STAFF from the junior 204; O1 `own_roles`; O1 still the Owner |
| Automated | "QA-F08-03" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F08-04 |
|---|---|
| Title | Effective permissions as a decision table: role grant × module enabled × deny override — deny wins |
| Persona | O1, C1, O3, S1 · Priority P1 · Technique: decision table |
| Steps | O1 permissions + service types; C1's role vs C1's permissions, void (after step-up), donations list; O3 (T3 has no services module) service types; S1 members; the `/api/me/permissions` answer itself |
| Expected | Row grant+module → allowed (200); grant+deny → DELETE absent, void 403 (not `stepup_required`), the rest 200; grant+no module → no SERVICES permissions, 403; no grant → 403. Grant overrides: N/A (no writer, OBS-02). The answer carries no internal Id |
| Automated | "QA-F08-04" |
| Result | Fail — DEF-005 (the answer carried `"institutionId": 1`; the decision table itself passed) — run 2026-11-22; **Pass after the fix** — re-run 2026-11-22 |

| ID | QA-F08-05 |
|---|---|
| Title | A role change reaches a live session at its next request, both ways; a stale edit is refused |
| Persona | O1, C1 · Priority P1 · Technique: state transition, concurrency (RowVersion) |
| Steps | C1's live token reads the monthly report; O1 takes `GIVING.REPORTS.VIEW` off TREASURER; C1 again; O1 saves with the old version, with none; O1 puts it back; C1 again |
| Expected | 200 → 403 → 200 with the same token (permission-cache version bump); `409 changed_meanwhile`, `400 invalid_version`; the change in `RolePermissionsHistory` |
| Automated | "QA-F08-05" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F08-06 |
|---|---|
| Title | An invitation's life: one open per person, only theirs to answer, closed for good once answered, cancelled or expired |
| Persona | O1, D1, throwaway invitee · Priority P1 · Technique: state transition (Pending → Declined / Revoked / Expired / Accepted), concurrency |
| Steps | Invite M1, an unknown number, a landline, "12345", an unknown role, LEADER; invite twice at once; again; D1 accepts it; decline then accept; invite, O1 cancels, accept; invite, expire it (ExpiresAt moved back in SQL), accept; invite again; accept |
| Expected | `already_member`, `account_not_found` (a landline is looked up; no account can hold one), `invalid_phone_number`, `role_not_found`, `not_held` (DEF-001: LEADER comes only through a leader link); 201 + 409; `invitation_pending`; D1 `404 invitation_not_found`; `invitation_closed` ×2; `invitation_expired`, and the expired one does not block a new one; accepted → STAFF's permissions, no ACCESS; nothing left Pending |
| Automated | "QA-F08-06" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F08-07 |
|---|---|
| Title | An invitation into an institution suspended after it was sent is not shown, cannot be accepted, and stays open |
| Persona | O4 (T4), throwaway · Priority P1 · Technique: state transition (institution Suspended) |
| Steps | T4 set Active in SQL (no product path, OBS-02); O4 invites; T4 suspended again; the invitee lists invitations; accepts by its id |
| Expected | 201; the list is empty; `409 institution_not_active`; the invitation still Pending; no T4 role given |
| Automated | "QA-F08-07" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F08-08 |
|---|---|
| Title | Plan staff limit |
| Result | **N/A** — `org.Plans.MaxStaffUsers` is read by no code (OBS-02) |

| ID | QA-F08-09 |
|---|---|
| Title | Role × action on the access routes: each T1 persona gets exactly what the templates give |
| Persona | O1, M1, S1, C1, L1 · Priority P1 · Technique: role × action matrix (5 personas × 5 reads + 3 writes) |
| Steps | GET roles, role-levels, permission-catalogue, members, invitations; S1 / C1 / L1 (stepped up) create a role, invite, revoke |
| Expected | O1, M1 200; S1, C1, L1 403 on every read and write; S1 still Staff |
| Automated | "QA-F08-09" |
| Result | Pass (1366 px) — run 2026-11-22 |

| ID | QA-F08-10 |
|---|---|
| Title | Another tenant's owner cannot reach T1's roles, people or invitations by their ids |
| Persona | O2 (T2), O1 · Priority P1 · Technique: cross-tenant (X1) |
| Steps | O1 invites a throwaway; O2 lists members and roles; takes M1's MANAGER, gives M1 STAFF, edits TREASURER, cancels T1's invitation — with T1's real ids |
| Expected | No T1 names or roles in O2's lists; 404 for each; T1's role assignments unchanged; the invitation still Pending |
| Automated | "QA-F08-10" |
| Result | Pass (1366 px) — run 2026-11-22 |
