# Plan 08 — Standing sponsors: the institution's own regular donors

Status: **agreed 2026-11-02 (answers in §8). K.1-K.4 built 2026-11-02** on `standing-sponsors` (from develop
f2a7be5, after PR #34); K24 and K14 wait for the user (K34). Asked for by
the user on 2026-11-02 (after C.5): "an institution must be able to set up dana dates itself, for a donor,
on a specific day, daily, weekly, monthly, yearly, every 2 or 3 months, every other month - any scenario.
People give like that today." Decision numbers here are **K1, K2, …**.

Branch: after Phase C's pull request, `git switch -c standing-sponsors develop`.

## 0. The scenarios the user gave (the report must match these)

1. An institution's admin or staff adds a person as one of its donors - someone who may never use the app
   - and sets up a meal for them on a pattern: one day, every day, a weekday every week, a day (or several
   days) every month, every 2 or 3 months, every other month, every year.
2. One person can hold several days a month (the 5th and the 20th), every month.
3. Those days belong to that person: the calendar shows them taken.
4. Another donor who wants one of those days can **ask for it**. The institution decides: give it to
   them (the standing sponsor's day is let go, and they are told) or not.
5. A standing sponsor can say ahead that they cannot give on a day; staff can mark it for them.
6. If the institution has something else on that day (a function, a closed day), the standing day is
   cancelled and the person is texted.
7. Two standing sponsors can **swap** days: A has the 5th, B the 12th; A cannot do the 5th this month, so
   this month B gives on the 5th and A on the 12th. Once, or for good.

## 1. What already exists

- `org.InstitutionDonors` (name, phone, notes, optional `UserId`; unique per institution and phone):
  in the schema since the start, **never used** (plan 03 cut "the donor CRM").
- `booking.Bookings` already allows an institution donor: `DonorUserId` NULL, `InstitutionDonorId`
  (composite FK), `Source = 'Staff'` (cut in plan 03: "staff booking on behalf of a donor").
- The engine (`BookMealHandler`: lock, rules, conditional `UPDATE`, outbox), the reminder at 18:00, the
  blocked dates, the live hints (C.3) and the SMS outbox with `DedupKey`s.
- C.1's yearly booking is the **donor's own** promise, yearly only (`RecurMonth`, `RecurDay`); it does not
  fit patterns or the institution.

## 2. Decisions (proposed)

| # | Decision | Why |
|---|---|---|
| K1 | **The donor book.** `org.InstitutionDonors` comes alive: staff add a person (name, phone, notes, and "this person agreed to receive texts" - stored with who and when, Q2). If the phone belongs to a Sethika account with a confirmed phone, the row is linked (`UserId`) when that person first signs in or when staff add them; the person sees their standing days in My bookings. RLS: `fn_Tenant(InstitutionId)` (the institution only); the linked person reads their own standing days through a narrow query, as co-sponsors do (H33) | The table and the FK exist; one history per person per institution. A person not in the app is still a donor |
| K2 | **A standing sponsorship** (new table `booking.StandingSponsorships`): the donor, the meal slot, the pattern, start date, optional end date, headcount, who brings it, "in memory of", notes, `Active`/`Ended`, `RowVersion`. Patterns: `Once`, `Daily`, `Weekly` (a weekday mask), `Monthly` (a mask of days 1-31, **several days a month**), every **N** months (`Interval` 1-12, counted from the start month: 2 = every other month, 3 = quarterly), `Yearly` (a month and day). A day that does not exist in a month (the 31st, 30 February) is skipped, not moved (Q3) | Every scenario in §0 is one row. "Skipped, not moved" is the one rule that never puts two days in one month by surprise |
| K3 | **Each day is an ordinary booking**, `Source = 'Standing'` (new CHECK value) with `StandingSponsorshipId`, made by the engine and **approved at once** (the institution made it). A nightly job (00:45 Colombo) books every active sponsorship's days up to the slot's booking window (`MaxAdvanceDays`); creating a sponsorship books them at once. So the calendar, the places left, the overbooking guarantee, the reminder, completing and no-show all work with no new code | One engine, one judge (C4, H2). Unlike H2 (a donor's promise to *try*), a standing day **holds** the place: that is the point of the institution's own arrangement |
| K4 | **Exceptions** (new table `booking.StandingExceptions`: sponsorship, date, kind, reason, who, when): `Skipped` (the sponsor cannot), `Released` (given to someone who asked), `Blocked` (the institution's other use), `MovedTo` / `MovedFrom` (a swap). The job never books a day that has an exception. Letting a day go cancels its booking in the same transaction, with the reason | A cancelled booking alone would be booked again by the next night's run |
| K5 | **"I can't give that day"**: a linked sponsor in My bookings, or staff for them. The day opens for others (live hint), the institution sees it in its ledger, and the sponsor gets one text confirming it | Scenario 5. Most standing sponsors will tell the temple by phone; staff need the same button |
| K6 | **Asking for a held day** (new table `booking.DayRequests`): a donor on `/i/[slug]` sees a standing day as "held by a regular donor - ask for it"; the request (headcount, note) waits for the institution. **Give it**: one transaction releases the standing day (K4), cancels its booking, and books the asker approved; the sponsor and the asker are texted. **Decline**: the asker is texted. Asking holds nothing. One open request per donor and slot-day | Scenario 4. The institution decides, as the user said |
| K7 | **The institution's other use**: adding a blocked date over a standing day lets it go (`Blocked`) and texts the sponsor; ordinary donors' bookings on those days are named, as today (they are not cancelled automatically; staff speak to those donors, Q4) | Scenario 6. The user asked for the standing day to be cancelled and the person told |
| K8 | **Swap**: staff choose two standing days (A's 5th, B's 12th) and "this time" or "from now on". This time: one transaction, `MovedTo`/`MovedFrom` exceptions, the two bookings cancelled and rebooked on each other's day. From now on: each sponsorship ends the day before and a new one starts with the other's days. Both sponsors are texted | Scenario 7. Both in one transaction, so a swap never leaves one day empty or double |
| K9 | **Clashes when setting up**: creating or changing a sponsorship shows its next days and any that are already full or blocked; staff can save, and those days are listed as not booked (with the reason) rather than refused | A monthly pattern that collides with one day should not block the other eleven |
| K10 | **Texts** (en/si/ta, drafts): `STANDING_CREATED` (the days and the place), `STANDING_SKIPPED`, `STANDING_RELEASED` (given to someone who asked), `STANDING_BLOCKED`, `STANDING_SWAPPED`, `DAY_REQUEST_GIVEN`, `DAY_REQUEST_DECLINED`; the day-before reminder is the existing one. A standing sponsor without an app account is texted at the phone the institution gave, **only if the consent box was ticked** (Q2); without it the institution tells them itself and the ledger says "not texted" | Every change of someone's day reaches them |
| K11 | **Permissions**: a new screen `BOOKINGS.SPONSORS` (`/bookings/sponsors`): VIEW, CREATE, EDIT (end, skip, swap). Template roles: OWNER and ADMIN all, STAFF VIEW. Answering a day request needs `BOOKINGS.CALENDAR.APPROVE` / `REJECT`. Asking needs `MYBOOKINGS.BOOKINGS.CREATE` | Same catalogue rules as every screen (anti-escalation, deny wins) |
| K12 | **Live**: every change above writes the `AvailabilityChanged` hint (H28) in its transaction | Open calendars stay right |
| K13 | (K.1) **The donor book's columns**: `PublicId` (the API address), `Language` (en/si/ta, default si, the language of their texts, as a leader's, V14), `TextConsentAt` + `TextConsentBy` (both or neither, `CK_InstDonor_TextConsent`; ticking again keeps the first who and when, unticking clears both), `CreatedBy`/`UpdatedAt`/`UpdatedBy`, `RowVersion`. All added at the end of the table, so the deploy is an `ALTER`; the table was empty everywhere. `UX_InstDonor_User`: an account is one donor of an institution at most | Q2 asked for "who and when". Language is needed by K10 and costs one column now |
| K14 | (K.1) **Linking** happens when staff add or change a donor: the account whose *confirmed* phone is the donor's, never a deleted account. Linking at the person's own sign-up or sign-in is left to K.4 (it needs the platform's reach across institutions, like H33) | An unconfirmed phone proves nothing about who holds it |
| K15 | (K.1) **Shapes the database enforces**: `CK_Standing_Shape` (each pattern has its own fields and no others; `Once` has `EndDate = StartDate`), `CK_Standing_YearDate` (29 February yes, 31 April never), `CK_Standing_Ended`; a standing booking names its sponsorship and the institution's donor (`CK_Bookings_Standing`), and its FK is `(StandingSponsorshipId, InstitutionId, InstitutionDonorId)`, so the day is booked for that sponsorship's own donor; one exception per sponsorship and day (`UQ_StandingExc_Day`), a swap names its other day (`OtherDate`, never the same day), a released day names its request; a day request has one open per donor and slot-day, and *Given* names both the day let go and the booking made | The later slices rely on these; the database refuses anything else. Found while testing: `EndDate = StartDate` alone let a `Once` without an end through, because a CHECK passes on unknown |
| K16 | (K.1) **RLS**: `InstitutionDonors` gains its missing `AFTER UPDATE` block; `StandingSponsorships` and `StandingExceptions` `fn_Tenant` (filter, block after insert and update); `DayRequests` `fn_TenantOrOwner(InstitutionId, DonorUserId)`, as `Bookings`. A linked sponsor reads none of the first three: their days reach them as their own bookings (`DonorUserId`), through `Bookings`' owner branch | The narrowest reach that still lets each person see what is theirs |
| K17 | (K.1) **Permissions**: `BOOKINGS.SPONSORS` screen (`/bookings/sponsors`), VIEW / CREATE / EDIT; VIEW needs `BOOKINGS.SLOTS.VIEW`. OWNER and MANAGER templates hold all three (every institution permission), STAFF ceiling and template VIEW only. **Institutions approved before this change keep their cloned roles** (cloning happens at approval), so their owners do not hold the new permissions until someone grants them: the same as SERVICES for institutions approved before Phase E | K11 as agreed; the gap for existing institutions is the existing cloning rule, not new |
| K18 | (K.2) **Booked while staff wait: the next 31 days.** Setting one up books the window's days up to 31 days ahead (their clashes are on screen at once, K9) and starts the standing job for the rest (`IStandingBookingWake`, Hangfire, as the outbox is woken); the night's run stays the guarantee. The answer carries `later`, the days left to the job | Measured: a daily sponsorship on a 365-day window took 17.5 s in one request (about 48 ms a day, each its own lock and transaction). Monthly or weekly patterns are 1-2.5 s a year |
| K19 | (K.2) **One change at a time per sponsorship.** Booking one of its days, skipping a day and ending it each take an update lock on the sponsorship's row inside their transaction, after the slot-day's Redis lock, and read its status under it (`IStandingLocks`) | Without Redis, and with READ_COMMITTED_SNAPSHOT, a reader does not wait for an uncommitted exception: a skip and a run could each miss the other and the day be booked although let go. Both races are forced in the tests |
| K20 | (K.2) **A day is booked once.** A day with a booking of the sponsorship in any status, or an exception, is never booked again, so a standing day cancelled on the calendar stays cancelled. A day the engine refuses (full, blocked, closed, too soon) writes nothing and is tried again each night, so a day that opens later is still booked | The job must not undo staff's decisions, and must not give up on a day that opens |
| K21 | (K.2) **No text per day.** A standing day writes the calendars' hint only, not `BookingConfirmed`: a daily sponsor would be texted every day, and a sponsor without an account or consent must not be (Q2). The day-before reminder already goes only to bookings with an account (`DonorUserId`); K.3 adds the sponsorship's own texts (K10) | Q2 and K10 |
| K22 | (K.2) **The engine's rules are unchanged for standing days**, the advance window too: with `MinAdvanceDays = 1` today is "too soon" when setting one up (listed, K9). Each day is recorded as made and approved by the staff member who set the sponsorship up, also the days the night's run books | One engine, one judge (C4) |
| K23 | (K.2) **Ending** lets go every booked day after today (cancelled with the reason given, or "The standing sponsorship ended."), keeps today's meal, and needs the version read (`stale`). **Skipping** takes a day of the pattern, today or later, also beyond the window (recorded only, so never booked); a day already given (completed) is refused, one cancelled before is recorded without a booking. An ended sponsorship refuses both (`sponsorship_ended`) | Scenario 5; today's meal is already being made |
| K24 | (K.2) **A linked sponsor's own "I can't give that day"** (K5) waits for My bookings in K.4: their session cannot write `StandingExceptions` (K16), so it needs the platform's reach for that one change, as linking at sign-in does (K14) | Narrowest reach first |
| K25 | (K.3) **"Held" is read past Row-Level Security, as counts only.** The public calendar has no session and a donor sees only their own bookings, yet both must know a full day is full through a regular donor. `SqlStandingHolds` (`IStandingHolds`) stamps its own connections as the platform, like `SqlShareLinks` (H33), and returns per slot-day only how many active standing bookings it has and the most people one brings: no name, phone or number. A day reads `held` when it is full, open or full (not blocked or closed), and letting one standing day go would leave room for a booking (`MealSlotRules.IsHeld`) | The second narrow platform class after H33; nothing else in a request gains the platform's reach |
| K26 | (K.3) **Asking** (`POST /api/institutions/{slug}/day-requests`, `MYBOOKINGS.BOOKINGS.CREATE`) is accepted only for a day the calendar shows held, from a donor with a confirmed phone who does not hold the day already, and whose headcount and way of giving the engine's rules accept (asked again when the day is given). `DayRequests.FulfilmentMode` is added for that (end of the table, an `ALTER`). Numbers `DR-2026-000001` from `core.NumberSeries` (`DAYREQUEST`). A second open request for the slot-day is `day_request_open` (the filtered unique index decides). Withdrawing is not built: the table allows it | A day with room is booked, not asked for; asking holds nothing |
| K27 | (K.3) **Giving** (`.../give`, `BOOKINGS.CALENDAR.APPROVE`) is one transaction: the slot-day's Redis lock, then the sponsorship's row lock (K19) and every read under it; the standing booking cancelled ("Given to a donor who asked for this day."), its place given back, the asker booked through the engine's rules and conditional UPDATE and approved by staff, the day recorded `Released` (naming the request and the cancelled booking), the request `Given` (naming the sponsorship and the asker's booking), the calendars' hint and two texts. Anything refused on the way (the asker's people do not fit: `slot_full`; the donor holds the day: `already_booked`) leaves all of it as it was. Several regular donors on one day: the body names whose (`sponsorship`). **Declining** (`.../decline`, `REJECT`) takes an optional reason the asker reads after signing in; it is never texted | Scenario 4 in one move; forced race: two askers given the same day at once, one gets it, the other `day_not_held` |
| K28 | (K.3) **A block over standing days** runs in the block's own transaction. Each sponsorship it touches is taken under its row lock, in id order; every day of its pattern from today (today included) to the block's end is recorded `Blocked`, booked or not, so a day beyond the booking window is never booked later and lifting the block brings nothing back; a day swapped in (`MovedFrom`) is let go and its change becomes the block. Each sponsor gets one text per block naming the first and last day let go. The answer adds `standingDays`; ordinary bookings are still only named (Q4) | Scenario 6; a block and the night's run at once (forced) leave the day let go whichever goes first |
| K29 | (K.3) **Swaps** (`POST /api/institution/sponsorships/swap`, `BOOKINGS.SPONSORS.EDIT`): the same meal, and neither day one the other already gives. Both slot-days' Redis locks in date order, both row locks in id order. *This time*: both days must be booked (`day_not_booked`); the two bookings are cancelled and each sponsor booked on the other's day through the engine, approved; four changes are recorded (`MovedTo` on each own day, `MovedFrom` on the day given instead, each naming its booking); refused when any of the days has a change (`day_already_changed`). *From now on*: each sponsorship ends (`EndBefore`: its end the day before its day, unless that is before it began) and a new one gives its meal on the other's pattern from the other's day (`TakingOver`), which keeps an every-N-months pattern in its months; booked days from those days on move in the same transaction, the job books the rest; refused for a `Once`, or when either has a change from its day on (that change would have no owner). Both texted: `STANDING_SWAPPED` / `STANDING_SWAPPED_ON` (the second added to K10's list) | Scenario 7; forced race: two staff swapping the same day with different partners at once, one wins |
| K30 | (K.3) **Texts** go through the outbox: `StandingTextDue` (template, sponsorship, the day it is about, a block's last day or a swap's old day, and a dedup key of template, sponsorship and what makes the change one of a kind) and `DayRequestAnswered` (request, given or not), sent by `StandingSmsHandler`. A linked donor is texted as their account; one without an account at the institution's phone in the donor book's language, only with consent (Q2); otherwise not at all. Setting one up now texts `STANDING_CREATED` once, naming the first day, and a skip `STANDING_SKIPPED`; ending texts nothing (K10 lists no text for it). 24 drafts (en/si/ta) in the seed and `docs/translations-to-review.md`; English fits one segment with a 20-character institution | K10 and Q2 |
| K31 | (K.4) **The form's preview is the server's** (`POST /api/institution/sponsorships/preview`, `BOOKINGS.SPONSORS.CREATE`, writes nothing): the slot and the pattern only, checked by the create handler's own rules (same `fields`), laid out by the arithmetic the night's run books by (`NextDays`, 12 days), each day in the window judged by `AvailabilityCalendar`'s rule (new `DaysAsync`, one read for the whole range): `free`, `clash` + the refusal code (`held` = another regular donor has it), or `later` (beyond the window). The screen asks 350 ms after the last change and drops older answers | One judge (C4): a TypeScript copy of the pattern arithmetic and the engine's rules would drift. Saving still answers with what became of each day (K9), since a day can go between preview and save |
| K32 | (K.4) **Marks for the screens**: a calendar booking carries `standing` (its sponsorship's `PublicId`, null otherwise), so staff see "Regular donor" and the month a ringed dot; a donor's own booking carries only `regular` (a bool from `Bookings.StandingSponsorshipId`), because the donor's session reads no sponsorship (K16) | The narrowest read that tells each side what it needs |
| K33 | (K.4) **A held day on `/i/[slug]`** is a gold full bowl that can be chosen ("held by a regular donor: you can ask for it"); it opens the ask sheet, not the booking sheet, also when the address names it (back from signing in). The headcount is the slot's range, since the day's own places are the regular donor's until given. `day_not_held` closes the sheet and reads the month again. The institution's tray lists open requests beside booking requests; when several regular donors hold the day, staff choose whose | K6, K26, K27 on screen |
| K34 | (K.4) **Not built, waiting for the user** (they need the platform's reach): a linked sponsor's own "I can't give that day" in My bookings (K24) and linking at sign-up or sign-in (K14). Until then a linked sponsor sees their standing days in My bookings marked "A regular day the institution set up for you" and asks the institution by phone; the ordinary donor cancel still applies to those bookings (a cancelled standing day is never booked again, K20, but nobody is texted) | The user asked to be asked first (2026-11-02) |

## 3. API (sketch)

- `GET/POST /api/institution/donors`, `PUT /api/institution/donors/{publicId}`.
- `GET/POST /api/institution/sponsorships`, `GET /.../{publicId}` (with its next days and exceptions),
  `POST /.../{publicId}/end`, `POST /.../{publicId}/skip` `{ date, reason }`,
  `POST /api/institution/sponsorships/swap` `{ a: {sponsorship, date}, b: {...}, permanent }`.
- `POST /api/institutions/{slug}/day-requests` (donor), `GET /api/bookings/day-requests` (mine),
  `GET /api/institution/day-requests`, `POST /.../{no}/give`, `POST /.../{no}/decline`.
- `GET /api/bookings/standing` and `POST /api/bookings/standing/{date}/skip` (a linked sponsor).
- Availability gains a reason `held` for a day full only through standing days, so the page can offer "ask".

## 4. Tests (what must be proved)

- The pattern arithmetic (domain): daily, weekly masks, several days a month, every N months from the start
  month, yearly, 29 February, the 31st in short months, start and end dates.
- The nightly job books each day once, never a day with an exception, and a rerun books nothing.
- Skip, release, block and swap each in one transaction (a forced failure halfway leaves nothing changed);
  two staff swapping the same day at once (forced race): one wins.
- A day request given: the sponsor's booking cancelled, the asker's approved, both texted once.
- RLS from each session: the institution, another institution, the linked sponsor, another donor.
- Mutations on each of the above; the browser: set up a monthly sponsor with two days, see the calendar,
  skip one, swap two, ask for a held day and give it; en/si/ta, desktop and 320 px.

## 5. Screens

- `/bookings/sponsors` (institution): the donor book, each donor's sponsorships and their next days, add
  (pattern picker with a preview of the next days and any clashes), skip a day, end, swap.
- The institution's calendar: standing days marked as such; day requests in the tray beside booking
  requests.
- `/i/[slug]`: a held day offers "ask for this day".
- My bookings: "My regular days" for a linked sponsor, with "I can't give that day"; my day requests.

## 6. Out of scope (proposed)

- Poya and festival patterns ("every poya day"): `core.SpecialDays` is not seeded (CLAUDE.md open items).
- Importing a donor book from a spreadsheet.
- Payments or pledges for standing days (Phase D's donations).

## 7. Work breakdown (estimate ~2.5 days)

- [x] **K.1** Schema (sponsorships, exceptions, day requests, `Source = 'Standing'`, the donors' text consent), RLS, the donor book API. ~0.5
  Built 2026-11-02 (K13-K17): tables `booking.StandingSponsorships`, `booking.StandingExceptions`,
  `booking.DayRequests`; `Bookings.StandingSponsorshipId` + `Source = 'Standing'`; the donor book's new
  columns; 10 RLS predicates; `BOOKINGS.SPONSORS.*`. API: `GET/POST /api/institution/donors`,
  `PUT /api/institution/donors/{publicId}` (`DonorBookHandler`, `DonorBookQueries`). Errors:
  `invalid_details` (fields), `donor_not_found` 404, `donor_phone_taken`, `donor_account_taken`, `stale` 409.
  Tests: `DonorBookEndpointsTests` 14, `StandingSchemaTests` 9, `RbacCatalogueTests` updated (50
  permissions, STAFF ceiling 11). Schema published to Sethika_Test and the dev `Sethika` (the user's OK).
  Mutants 65/65 killed: C# 27, SQL/RLS 30 (run on `Sethika_C2Test`), web 8. Neighbours (Persistence,
  booking engine, policy) 101/101. Screen `/bookings/sponsors` (donor book: list, search, add/edit,
  consent) built ahead of K.4, en/si/ta, vitest; not yet tried in a browser.
- [x] **K.2** Pattern engine (domain) + nightly job + create/end/skip. ~0.75
  Built 2026-11-02 (K18-K24), no schema change: `StandingSponsorship` (pattern arithmetic, `OccursOn`),
  `StandingPattern` (masks), `StandingDayChange` (a `StandingExceptions` row), `Booking.Standing`;
  `StandingDayBooker` (the engine's door: slot-day lock, the sponsorship's row lock `IStandingLocks`,
  the rules, the conditional UPDATE), `StandingBookingRun` + Hangfire `standing-bookings` at 00:45 Colombo,
  `IStandingBookingWake`. API: `GET/POST /api/institution/sponsorships` (`?donor=`),
  `GET /.../{publicId}` (its next 12 days: booked, cancelled, an exception's kind, open, upcoming),
  `POST /.../{publicId}/end` `{version, reason}`, `POST /.../{publicId}/skip` `{date, reason}`. Errors:
  `invalid_details` (fields), `sponsorship_not_found` 404, `sponsorship_ended`, `day_already_changed`,
  `stale`, `slot_busy`, `invalid_transition` 409. Tests: domain `StandingSponsorshipTests` 33,
  integration `StandingSponsorshipTests` 18 (two forced races on the row lock). Mutants 46/46 killed
  (domain 12, integration 34); two survived first and each led to a test (a `Once` without an end;
  skipping a past day of a running pattern). Neighbours (engine, yearly, calendar, donor book, schema,
  EF mapping, RLS, SMS, live) 139/139; domain 361, application 71. Screens are K.4's.
- [x] **K.3** Day requests, blocks over standing days, swap; texts. ~0.75
  Built 2026-11-02 (K25-K30). Schema: `DayRequests.FulfilmentMode`; 24 SMS templates (`STANDING_CREATED`,
  `_SKIPPED`, `_RELEASED`, `_BLOCKED`, `_SWAPPED`, `_SWAPPED_ON`, `DAY_REQUEST_GIVEN`, `_DECLINED`; en/si/ta drafts),
  published to Sethika_Test and the dev `Sethika` (the user's OK). `DayRequest` (domain), `DayRequestHandler`,
  `StandingBlocks` (inside `SlotManagementHandler.AddBlockedDateAsync`'s transaction), `StandingSwapHandler`,
  `StandingTexts` + `StandingSmsHandler`, `SqlStandingHolds` (K25), `MealSlotRules.IsHeld`, availability reason
  `held`. API: `POST /api/institutions/{slug}/day-requests`, `GET /api/bookings/day-requests`,
  `GET /api/institution/day-requests` (`?all=`), `POST /.../{requestNo}/give` `{sponsorship?}`, `/decline` `{reason?}`,
  `POST /api/institution/sponsorships/swap`; a block's answer adds `standingDays`. Errors: `day_not_held`,
  `day_request_open`, `day_request_answered`, `day_request_not_found` 404, `phone_required`, `day_not_booked`,
  `day_already_changed`, `slot_full`, `already_booked`. Tests: integration `StandingChangesTests` 17 (three races
  forced on the row lock: two gives, two swaps, a block and the night's run), domain 374 (13 new). Mutants 43/43
  killed (domain 9, integration 34; three first failed to build and were rewritten to compile). Neighbours
  (standing, schema, donor book, donor and institution booking endpoints, policy, live, search, EF mapping,
  booking SMS, RBAC catalogue, OTP templates, yearly) 166/166. Not yet: withdrawing a request; screens (K.4).
- [x] **K.4** Screens + e2e. ~0.5
  Built 2026-11-02 (K31-K34). API: `POST /api/institution/sponsorships/preview` (`StandingSponsorshipPreview`,
  `AvailabilityCalendar.DaysAsync`); calendar bookings carry `standing`, a donor's bookings `regular`. Screens:
  `/bookings/sponsors` (each donor's arrangements; `NewSponsorshipSheet`: meal, pattern picker, live preview of
  the next 12 days and clashes, what became of each day; `SponsorshipSheet`: next days, skip, end, swap this time
  or from now on), the calendar (ringed dot and "Regular donor" on standing days; `DayRequestTray` beside the
  booking requests, give / keep, whose day when several), `/i/[slug]` (held day = gold bowl, `AskDaySheet`), My
  bookings (regular-day mark, `MyDayRequests`). en/si/ta (181 strings, drafts in `docs/translations-to-review.md`).
  Found in the browser and fixed: the Tamil page heading was wider than a 320 px phone; the month-day chips
  overlapped at 320 px; a field's error stayed after it was fixed. Tests: integration `StandingSponsorshipTests` +4
  (preview, pattern, refusals, marks), vitest `pattern.test.ts` 12 (248 in all), e2e `sponsors/standing.spec.ts`
  4 scenarios x desktop + 320 px = 7 runs, 7/7, with `donor-book.spec.ts` 5/5. Mutants 21/21 killed: C# 10 (one
  first failed to build and was rewritten to compile; one equivalent dropped before running), web 11.
  Neighbours (standing, donor book, donor and institution booking endpoints, search, live, yearly, co-sponsors,
  endpoint policy) 157/157; build, ESLint, tsc and `dotnet format` clean. Not built: K24, K14 (K34).

## 8. Questions for the user (answered 2026-11-02)

- **Q1 When?** Nov 10 is in 8 days; left: C.6, Phase D (donations, needs board, gratitude wall,
  dashboards/reports), mobile F.5, the report. Plan 08 is ~2.5 days. Recommendation: build it right after
  C.6 and cut the gratitude wall and part of the reports from Phase D (the report says so).
- **Q2** May Sethika text a standing sponsor who has no app account, at the phone the institution gave?
  Recommendation: yes - the texts are only about their own days at that institution - with the
  institution confirming the person agreed when adding them (a checkbox, stored).
- **Q3** A day that does not exist in a month (the 31st in April): skip it (recommended), or move it to the
  month's last day?
- **Q4** When the institution blocks a day, cancel only the standing days (recommended, K7), or ordinary
  donors' bookings too?
- **Q5** Should a standing day hold the place (K3, recommended), so others see it taken and must ask? The
  alternative - the day stays open and the standing sponsor competes like anyone - loses the point.

**Answers (the user, 2026-11-02):**

- **Q1:** build it right after C.6 (Phase C's pull request), then Phase D without the gratitude wall and
  part of the reports; the report says what was cut and why.
- **Q2:** yes, with the consent box: staff tick "this person agreed to receive texts" when adding the
  donor (stored: who, when). Unticked, no texts are sent to that donor (K1, K10).
- **Q3:** skip it. The 31st in April and 29 February in a common year are not booked (K2).
- **Q4:** standing days only (K7). Ordinary donors' bookings on a blocked day are listed, as today, not
  cancelled.
- **Q5:** yes, a standing day holds the place; others see it taken and ask for it (K3, K6).
