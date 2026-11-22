# F03 — Step-up verification (RequiresMfa permissions)

Priority **P1** · Personas O1, P1, P2, S1, C1, O2 · Spec `web/e2e/qa/F03-stepup.spec.ts`
Rules under test: a `RequiresMfa = 1` permission needs a texted code on the current access token;
the grant lives 15 min in Redis (`stepup:{user}:{jti}`); refused as `403 stepup_required` until then.
Every call aims at a record that does not exist, so a wrongly allowed call still changes nothing, and
the answer after the step-up (400/404 instead of 403) proves the check was passed.

**Decision table** (QA-F03-01, -02):

| Holds permission? | Deny override? | Stepped up? | Expected |
|---|---|---|---|
| no | – | no / yes | 403, no `stepup_required` (S1, P2) |
| yes (role) | yes | no / yes | 403, no `stepup_required` — deny wins (C1) |
| yes | no | no | 403 `stepup_required` (O1, P1 × 10 permissions) |
| yes | no | yes | passes the check (400 / 404 on the missing record) |

| ID | QA-F03-01 |
|---|---|
| Title | Every RequiresMfa permission asks for a step-up, and passes once it is given |
| Persona | O1 (4 institution permissions), P1 (6 platform permissions) · Priority P1 · Technique: decision table, role × action |
| Steps | Compare the list with `auth.Permissions WHERE RequiresMfa = 1`; call each endpoint; step up; call again; call a non-MFA endpoint |
| Expected | The 10 database permissions are exactly the 10 covered; before → 403 `stepup_required`; after → not 403; non-MFA → 200 without a step-up |
| Automated | "QA-F03-01" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-02 |
|---|---|
| Title | No permission or a deny override is a plain refusal, never an offer to step up |
| Persona | S1, C1, P2, O2 · Priority P1 · Technique: decision table |
| Steps | S1 cancels an invitation; C1 voids a receipt; P2 approves a request; O2 voids a T1 receipt; S1/C1/P2 step up and try again |
| Expected | 403 without `stepup_required`; still 403 after a step-up |
| Automated | "QA-F03-02" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-03 |
|---|---|
| Title | The owner's invitation asks for the texted code on screen; cancelling it changes nothing |
| Persona | O1 (in Sinhala) · Priority P1 · Technique: state transition, UI |
| Steps | `/si/access/members` → invite a number as Staff → the check opens → Cancel → Send again → type the code |
| Expected | Cancel: "Nothing was changed…" (si) and no invitation row; with the code: one Pending invitation; dialog fits 390/320 px |
| Automated | "QA-F03-03" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-04 |
|---|---|
| Title | A step-up code dies after 5 minutes and after 5 wrong tries |
| Persona | P1 · Priority P1 · Technique: boundary |
| Steps | Request, expire the code's TTL, verify; request, 5 wrong codes, the right one; approve |
| Expected | `otp_not_found`; `otp_incorrect` ×4 then `otp_attempts_exhausted`; the right code refused; approve still `stepup_required` |
| Automated | "QA-F03-04" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-05 |
|---|---|
| Title | A step-up belongs to one person and one token |
| Persona | P1, O1 · Priority P1 · Technique: error guessing, security |
| Steps | P1 requests a code; O1's session verifies it; P1 verifies it; a second P1 session approves |
| Expected | O1 → 400 and still `stepup_required`; P1's token passes; P1's other session → `stepup_required` |
| Automated | "QA-F03-05" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-06 |
|---|---|
| Title | A step-up lasts 15 minutes, then the next sensitive action asks again |
| Persona | P1 · Priority P1 · Technique: boundary (grant TTL) |
| Steps | Step up; read the grant's TTL; approve; expire the grant; approve |
| Expected | TTL 841–900 s; passes; then `stepup_required` |
| Automated | "QA-F03-06" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-07 |
|---|---|
| Title | A step-up code goes to the account's own phone in the account's language |
| Persona | O1 (si) · Priority P1 · Technique: equivalence (language) |
| Steps | O1 requests a step-up |
| Expected | Recipient O1's `+94…`; body in Sinhala script |
| Automated | "QA-F03-07" |
| Result | Pass (1366 px) — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite |

| ID | QA-F03-08 |
|---|---|
| Title | Asked again within the minute, the check does not say "too many tries" |
| Persona | O1 (si) · Priority P1 · Technique: error guessing (cancel, retry) |
| Steps | Invite → check opens (code sent) → Cancel → Send invitation again within 60 s |
| Expected | The check opens saying the code was sent, with a countdown to a new one; no "too many tries" error; no second text; the earlier code finishes the invitation |
| Automated | "QA-F03-08" |
| Result | **Fail — DEF-003** — run 2026-11-22 on `develop` cb21bdd + uncommitted QA suite. **Re-run after the fix: Pass** (1366 px, same day, fresh QA world; full F01-F04 49/49) |
