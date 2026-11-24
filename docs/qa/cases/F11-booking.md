# F11 — Donor booking and My bookings

Priority **P1** · Personas D1 (Sinhala, web), D2 (Tamil), D5, throwaway donors `QA tmp f11-…`; M1 makes
the meals and answers · Spec `web/e2e/qa/F11-booking.spec.ts`. States: Requested → Approved / Rejected;
Requested / Approved → Cancelled (donor until the day before, institution any time). Every case cancels
what it booked and switches its "QA F11 …" meals off.

| ID | QA-F11-01 |
|---|---|
| Title | D1 books a meal on T1's page in Sinhala, sees it in My bookings, and is texted at once |
| Persona | D1 · Priority P1 · Technique: happy path, cross-actor (SMS), X2 |
| Steps | `/si/i/<T1>?slot=…&date=+4` → sheet "QA F11 දිවා දානය සඳහා අනුග්‍රහය දක්වන්න" → one more person → in memory of "QA අම්මා" → ඉල්ලීම යවන්න; `/si/bookings/mine` |
| Expected | "ඉල්ලීම යැවුණා" and a `BK-2026-…` number; Requested; slot-day 1 of 1; exactly one `BOOKING_REQUESTED` text naming the number, within seconds; My bookings: පිළිතුර බලාපොරොත්තුවෙන්, "QA අම්මා සිහිවීම පිණිස", a cancel button; no sideways scroll |
| Automated | "QA-F11-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-24 (390 / 320 failed once on DEF-009 data, see QA-F11-04) |

| ID | QA-F11-02 |
|---|---|
| Title | The last place, taken by two donors at the same moment: exactly one gets it; the counter never passes the cap |
| Persona | D1, D2, D5, 3 throwaways · Priority P1 · Technique: concurrency, boundary (last place) |
| Steps | Three days: D5 takes one of 2 shared places; D1 and D2 book the last at once; D1 and D2 book an exclusive meal at once. Then six donors at once on a 4-place day |
| Expected | Each pair `[201, 409 slot_full]`, BookedSponsors = MaxSponsors (never more); six at once → four 201, two `slot_full`, 4 of 4 places, 40 people |
| Automated | "QA-F11-02" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F11-03 |
|---|---|
| Title | Which days can be booked |
| Persona | D1 · Priority P1 · Technique: boundary values (dates), equivalence partitions |
| Pre-conditions | Meal 1–30 days ahead, served on every weekday but that of +5; day +8 closed for it; a same-day meal (0 days ahead) starting 00:00 |
| Steps | Book yesterday, today, tomorrow, +30, +31, the unserved weekday, the closed day, the same-day meal today and tomorrow; dates `…-32`, `2026-02-30`, `22/11/2026`, empty, `tomorrow`; open the calendar on −1, +2, the weekday, the closed day, +31 |
| Expected | `too_soon`, `too_soon`, 201, 201, `too_far`, `not_on_this_day`, `date_blocked`, `too_soon` (E10: closes when the meal starts), 201; malformed → `400 invalid_details ["date"]`; the calendar disables exactly the refused days |
| Automated | "QA-F11-03" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F11-04 |
|---|---|
| Title | What the donor sends is checked; who may book; the hourly limit |
| Persona | D1, D2, M1, V1, a throwaway · Priority P1 · Technique: boundary values, role × action, rate limit (X1) |
| Steps | People 4 / 51 (5–50) / none; "donor brings" where only "institution prepares"; none; in-memory 201, notes 501; an unknown meal; a malformed id; the edges 5 / 50 people, 200 / 500 characters (a throwaway); signed out; M1's T1 session; a throwaway books 20 days, then a 21st; the edge donor's `/si/bookings/mine` at 390 px |
| Expected | `headcount_out_of_range` ×2, `invalid_details` naming the field, `fulfilment_not_offered`, `404 slot_not_found`, 400; the edges 201; 401; 403 (B1: a staff session is not a donor); 20 × 201 then `429`, and no slot-day made for the 21st; the 200-character in-memory line wraps inside the page |
| Automated | "QA-F11-04" |
| Result | Pass after the fix (1366 px, with 390 px checks of My bookings and the calendar) — re-run 2026-11-24. First run: **Fail — DEF-009** (the 200-character in-memory word made My bookings scroll sideways at 390 px; D1's rows were shortened in SQL and the edge moved to a throwaway) |

| ID | QA-F11-05 |
|---|---|
| Title | A double tap books once |
| Persona | D1 (screen), D2 (API) · Priority P1 · Technique: concurrency (double submit) |
| Steps | D1 double-clicks ඉල්ලීම යවන්න; D2 sends the same booking twice at once on a shared and on an exclusive meal |
| Expected | One booking for D1, no "already booked" shown, 1 place taken; D2 shared `[201, 409 already_booked]`, exclusive `[201, 409 slot_full]` (E25); one active booking each, counters 2 and 1 |
| Automated | "QA-F11-05" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F11-06 |
|---|---|
| Title | Cancelling: the donor's own, until the day before, once; the place comes back; no text back to them |
| Persona | D1, D2, M1 · Priority P1 · Technique: state transition, boundary (the day before / the day) |
| Steps | D2 cancels D1's; D1 with a 301-character reason; D1 cancels on `/si/bookings/mine` with a reason; again; a rejected one; one moved (SQL) to today; one moved to tomorrow |
| Expected | `404 booking_not_found`; `400 reason_too_long`; "… අවලංගු කළා.", අවලංගුයි, place back (0); `409 invalid_transition`; no text to D1 for the cancel (E30); rejected `invalid_transition`; today `409 cancel_window_closed` and no cancel button on the card; tomorrow 200 |
| Automated | "QA-F11-06" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F11-07 |
|---|---|
| Title | My bookings shows a donor only their own, in their language, with no internal ids |
| Persona | D1, D2 (Tamil) · Priority P1 · Technique: X1 (RLS owner), X2, language |
| Steps | D1 and D2 book the same day; D2 `GET /api/bookings/mine`; `/ta/bookings/mine` |
| Expected | D2's list has D2's booking, not D1's; no internal id keys; the Tamil page: its title and the Requested status in Tamil (`ta.json`), the meal's Tamil name, no D1 number, no sideways scroll |
| Automated | "QA-F11-07" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-24 |

| ID | QA-F11-08 |
|---|---|
| Title | A regular (standing) day cannot be cancelled by the donor, only let go |
| Result | **Moved to F18** — needs a standing sponsorship linked to a donor account (D6 links at sign-up in F18); the rule (`regular_day`) is checked there |
