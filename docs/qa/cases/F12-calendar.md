# F12 — Booking calendar (staff) and live updates

Priority **P1** · Personas M1 (T1 Manager, Sinhala), O1 (the colleague in a second browser), S1 (Staff:
looks only), C1, L1; O2 (T2); donors D1, D2, D5 · Spec `web/e2e/qa/F12-calendar.spec.ts`. Decisions:
approve / reject (`APPROVE` / `REJECT`), cancel / served / did-not-happen (`CALENDAR.EDIT`). Live updates
come over SignalR (`/api/hubs/availability`) through the outbox. Every case cancels what is still open
and switches its "QA F12 …" meals off.

| ID | QA-F12-01 |
|---|---|
| Title | M1 answers requests on the Sinhala calendar: accept texts the donor; not accepting needs a reason the donor reads |
| Persona | M1, D1 · Priority P1 · Technique: happy path, cross-actor (SMS, the donor's screen), X2 |
| Steps | D1 requests +5 (in memory of "QA සීයා") and +6; `/si/bookings/calendar`; පිළිගන්න on the first; පිළිනොගන්න on the second → reason → send; D1's `/si/bookings/mine` |
| Expected | Both in "තීරණය කිරීමට" with D1's name and the in-memory line; "… පිළිගත්තා…" toast, Approved, one `BOOKING_APPROVED` text; the not-accept button disabled until a reason; then Rejected, the place back, one `BOOKING_REJECTED` text **without** the reason (E30); D1 reads "පිළිගත්තේ නැත: …" |
| Automated | "QA-F12-01" (@phone) |
| Result | Pass (1366, 390, 320 px) — run 2026-11-24 |

| ID | QA-F12-02 |
|---|---|
| Title | A booking's states: each decision only from its state; served / not happened only from its day; places given back or kept |
| Persona | M1, D1, D2, D5 · Priority P1 · Technique: state transition (every edge of `BookingStatus`) |
| Steps | Requested: complete, no-show, reject without / blank / 301-character reason, approve, approve again, reject; before the day: complete, no-show; approve B, cancel B without / 301-character reason; an unknown number. The day moved to today (SQL); the Sinhala calendar on today; සපයා අවසන් on A; no-show B; cancel / complete A and B; cancel the request C with a reason |
| Expected | `invalid_transition` ×2, `reason_required` ×2, `reason_too_long`, Approved, `invalid_transition` ×2, `too_early` ×2, Approved, `reason_required`, `reason_too_long`, `404 booking_not_found`; 3 places held. Today: served / did-not-happen buttons only on the approved card; Completed; NoShow; both keep their place (3); nothing moves on from them; C Cancelled, the place back (2), one `BOOKING_CANCELLED` text; A has 3 history rows |
| Automated | "QA-F12-02" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F12-03 |
|---|---|
| Title | Two staff decide the same booking at the same moment: one wins, the other is told, the place is counted once |
| Persona | M1, O1, D1, D2 · Priority P1 · Technique: concurrency (RowVersion, E7) |
| Steps | Three days: M1 approves while O1 rejects the same request; M1 and O1 cancel the same approved booking at once |
| Expected | `[200, 409]` (`stale` or `invalid_transition`), BookedSponsors matches the winner; the cancel pair `[200, 409]`, the place given back once, one Cancelled history row |
| Automated | "QA-F12-03" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F12-04 |
|---|---|
| Title | Role × action on the calendar: Owner and Manager decide, Staff only looks (screen and API), others are refused |
| Persona | O1, M1, S1, C1, L1, D1 · Priority P1 · Technique: role × action matrix (6 × 4 reads, 4 × 5 decisions) |
| Steps | GET bookings (range), today, ledger, a booking; S1 / C1 / L1 / D1 approve, reject, cancel, complete, no-show; S1's `/si/bookings/calendar?date=…` |
| Expected | O1, M1, S1 200 ×4; C1, L1, D1 403 ×4; every decision 403, statuses unchanged; S1 sees the request with no decision button, no sideways scroll |
| Automated | "QA-F12-04" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F12-05 |
|---|---|
| Title | Another tenant cannot see or decide T1's bookings by number; T1's staff cannot decide their own booking at another place (E8) |
| Persona | O2, M1 (as donor and as staff), D1 · Priority P1 · Technique: cross-tenant (X1) |
| Steps | O2: GET T1's booking and its shared-with; approve / reject / cancel / complete / no-show it; O2's calendar. O2 adds a T2 meal; M1 switches to a personal session and books it; M1 (T1 session) approves that booking |
| Expected | `404 booking_not_found` for all; not in O2's calendar; still Requested. M1's approval `404 booking_not_found`, still Requested |
| Automated | "QA-F12-05" |
| Result | Pass (1366 px) — run 2026-11-24 |

| ID | QA-F12-06 |
|---|---|
| Title | Live: a donor's booking appears on M1's open calendar, and O1's answer in a second browser clears it, without a reload |
| Persona | M1, O1 (second browser), D2 · Priority P1 · Technique: cross-actor, live (SignalR, plan 07 H11) |
| Steps | M1 and O1 open `/si/bookings/calendar?date=+10` (a not-reloaded flag set in each); D2 books (API); O1 accepts it on screen; D2 cancels |
| Expected | Both calendars show the request within 20 s; after O1 accepts, M1's tray count goes to 0 and the day shows තහවුරුයි ("a colleague's decision shows here without a reload", `CalendarScreen`); after D2 cancels, M1's card shows අවලංගුයි; neither page reloaded |
| Automated | "QA-F12-06" |
| Result | Pass after the fix (1366 px) — re-run 2026-11-24 against the rebuilt API. First run: **Fail — DEF-008** (O1's acceptance never reached M1's open calendar; evidence `QA-F12-06-after-colleague-accepts-qa.png`) |

| ID | QA-F12-07 |
|---|---|
| Title | The calendar's reads refuse what is malformed and carry no internal ids |
| Persona | M1 · Priority P1 · Technique: boundary (62 days), error guessing, X1 |
| Steps | `bookings?from` only; to < from; 63 days; `2026-02-30`; `status=Lost`; then 62 days with `status=Requested` |
| Expected | 400 for each; 200, and no `id` / `slotInstanceId` / `mealSlotId` / `institutionId` / `donorUserId` key |
| Automated | "QA-F12-07" |
| Result | Pass (1366 px) — run 2026-11-24 |
