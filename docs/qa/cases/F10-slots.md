# F10 — Meal slots and blocked dates

Priority **P1** · Personas M1 (T1 Manager, Sinhala), O1, S1, C1, L1 of T1; O2 (T2); donors D1, D2, D5 ·
Spec `web/e2e/qa/F10-slots.spec.ts`. Every meal a case makes is "QA F10 … <stamp>" (names are unique per
institution even when switched off); each case cancels the bookings it made, switches its meals off and
removes its closed days.

| ID | QA-F10-01 |
|---|---|
| Title | M1 adds a shared meal on the Sinhala slots screen; the form and the API refuse what is wrong, in Sinhala |
| Persona | M1 · Priority P1 · Technique: happy path, boundary (21 sponsors), error guessing (same name), X2 |
| Steps | `/si/bookings/slots` → ආහාර වේලක් එක් කරන්න → save empty → name, Sinhala name, 40 people, Shared, 21 sponsors → save → 4 → save; add another meal with the same name |
| Expected | Empty: "නමක් ඇතුළත් කරන්න…" and the most-people error; 21: "2 සිට 20 දක්වා…" under the field; 4: the card "4 දෙනෙකු දක්වා බෙදා ගනී"; no sideways scroll. Same name: **"ඔබට දැනටමත් මෙම නමින් ආහාර වේලක් ඇත."** (`slot_name_taken`) |
| Automated | "QA-F10-01" (@phone) |
| Result | Pass after the fix (1366, 390, 320 px) — re-run 2026-11-24. First run: **Fail — DEF-007** (the taken name got "Enter a name (up to 100 characters)") |

| ID | QA-F10-02 |
|---|---|
| Title | A meal's rules at their boundaries |
| Persona | M1 · Priority P1 · Technique: boundary values, equivalence partitions |
| Steps | API create with: Shared 1 / 2 / 20 / 21 / none; Exclusive 2 / 1; mode "Buffet"; min people 0; max < min, = min, none; end = start; 25:00; days 0 / 128; advance min > max, −1, 0–0; cost −1, 450.555, 1e8; fulfilment [] / Caterer; name 101 / blank / 100; a taken name. Then the 100-character name on the Sinhala slots screen |
| Expected | Each wrong one `400 invalid_details` naming exactly that field; 2, 20, Exclusive 1, max = min, same day 0–0, name 100 → 201; taken → `409 slot_name_taken`; only the good ones made. The 100-character name (one word) stays inside its card |
| Automated | "QA-F10-02" |
| Result | Pass after the fix (1366 px) — re-run 2026-11-24. First run: **Fail — DEF-009** (the 100-character name ran ~400 px past its card) |

| ID | QA-F10-03 |
|---|---|
| Title | Two people edit one meal: the later save with the old version is refused and changes nothing (RowVersion), on the API and on the screen |
| Persona | M1, O1 · Priority P1 · Technique: concurrency (E17) |
| Steps | API: both read v1; O1 saves (30 people); M1 saves v1 (99); no version; "AAAA". Screen: M1 opens the meal's edit sheet; O1 saves 25 meanwhile; M1 types 45 and saves; then saves again |
| Expected | `409 stale`, still 30; `400 invalid_details ["version"]` ×2. Screen: "වෙනත් කෙනෙක් මේ දැන් මෙය වෙනස් කළා. එය යාවත්කාලීන කළා; කරුණාකර පරීක්ෂා කර නැවත උත්සාහ කරන්න." ("…It has been refreshed; please check and try again."), the meal still 25; the form then holds O1's 25 and a second save goes through |
| Automated | "QA-F10-03" |
| Result | Pass after the fix (1366 px) — re-run 2026-11-24. First run: **Fail — DEF-006** (the form said "refreshed" but kept the old values and version) |

| ID | QA-F10-04 |
|---|---|
| Title | Closing a day that has bookings keeps them, lists them, and stops new ones; reopening lets booking in again |
| Persona | M1, D1, D2, D5, V1 · Priority P1 · Technique: state transition (open → closed → open), boundary (366 / 367 dates, ended yesterday), X2 |
| Steps | D1 requests and D2 holds an approved place on day +6 (Shared 4); M1 closes +6 for all meals on `/si/bookings/slots`; D5 books both meals that day; public availability; a block for the dinner only on +7; reopen +6; reopen again; the block rules |
| Expected | The sheet says bookings stay; both booking numbers listed after saving; still Requested / Approved, BookedSponsors 2; `409 date_blocked` ×2; availability `bookable:false, reason:date_blocked`; the one-meal block refuses only the dinner; reopened: availability 2 places, D5 books; again `404 blocked_date_not_found`. Rules: to < from, 367 dates, ended yesterday, no from, blank / 201-character reason → `400` naming the field; an unknown meal `404 slot_not_found`; 366 dates and one that began before today → 201 |
| Automated | "QA-F10-04" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-24 |

| ID | QA-F10-05 |
|---|---|
| Title | A meal's capacity changed after a day was booked: that day keeps its places, new days take the new rule |
| Persona | M1, D1, D2, D5 · Priority P1 · Technique: state transition, boundary (C3 snapshot) |
| Steps | Shared 4; D1 books +10; M1 edits to Shared 2; availability of +10 and +11; D2, D5, D1 book +11 |
| Expected | +10 keeps max 4 (3 left, as designed — the form says so: OBS-12); +11 offers 2; the third booking on +11 `409 slot_full`, counter 2 of 2 |
| Automated | "QA-F10-05" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F10-06 |
|---|---|
| Title | Switching a meal off keeps its bookings and names them, hides it, and refuses new ones; switching on undoes it |
| Persona | M1, D1, D2 · Priority P1 · Technique: state transition (active ⇄ off) |
| Steps | D1 books +8; M1 switches the meal off; D2 books +9; the public page; off again; on; D2 books |
| Expected | `200 {bookings:[D1's]}`, D1's still Requested; `409 slot_inactive`; gone from the page; off twice 200; on `{bookings:[]}`; D2 201 |
| Automated | "QA-F10-06" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F10-07 |
|---|---|
| Title | Role × action on the slot routes; the screen follows |
| Persona | O1, M1, S1, C1, L1, D1 · Priority P1 · Technique: role × action matrix (6 personas × 3 reads + 6 writes) |
| Steps | GET meal-slots, blocked-dates, listing; create, edit, activate, block, unblock (unknown id), listing |
| Expected | O1, M1: 200 ×3, 201, 200, 200, 201, 404, 204. S1: 200 ×3, 403 ×6. C1, L1, D1: 403 ×9. S1's screen lists the meals with no add / edit / close-days button and no listing switch (the hint only) |
| Automated | "QA-F10-07" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F10-08 |
|---|---|
| Title | Plan slot limit |
| Result | **N/A** — `org.Plans.MaxMealSlots` is read by no code (OBS-02) |

| ID | QA-F10-09 |
|---|---|
| Title | Another tenant's owner cannot read or change T1's meals and closed days by their ids |
| Persona | O2 (T2), M1 · Priority P1 · Technique: cross-tenant (X1) |
| Steps | O2 lists meals and closed days; edits, deactivates, activates T1's meal by its PublicId; blocks a day for it; deletes T1's closed day by its id |
| Expected | Neither list has T1's; `404 slot_not_found` ×4; `404 blocked_date_not_found`; T1's meal unchanged (name, active, version) and its closed day still there |
| Automated | "QA-F10-09" |
| Result | Pass (1366 px) — run 2026-11-24 |

Observations recorded for F10: OBS-11 (switched-off meals stay on the screen for good), OBS-12 (capacity
snapshot), OBS-13 (closing a day or switching a meal off texts nobody).
