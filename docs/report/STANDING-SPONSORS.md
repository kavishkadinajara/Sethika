# Report chapter draft: Standing sponsors, the institution's own regular donors (plan 08)

Draft for the final report, written when the work was finished (2026-11-04). Decision numbers (K1-K37)
refer to `docs/plans/08-STANDING-SPONSORS.md`; the summary is in `docs/DECISIONS.md`.

## 1. Why this exists

The proposal describes donors who find an institution and book a day themselves. When the booking
engine and Phase C were finished, the user pointed out the giving that most temples, kovils, mosques,
churches and homes depend on most: **their own regular donors**. A family gives the lunch on the 5th of
every month. Someone else gives breakfast every Sunday, or once a year on a parent's birthday. Most of these people never open an app. The institution keeps them in a
book, or in someone's memory, and phones them when something changes.

The user's words (2026-11-02): "an institution must be able to set up dana dates itself, for a donor,
on a specific day, daily, weekly, monthly, yearly, every 2 or 3 months, every other month - any
scenario. People give like that today." Seven scenarios came with it (plan 08 §0): any pattern; several
days a month for one person; those days shown as taken; another donor asking for one; a sponsor who
cannot come on a day; the institution needing the day for something else; and two regular donors
swapping days, once or for good.

This was not in the proposal. It replaced the gratitude wall and part of Phase D's reports, which the
user cut to make room (plan 08 Q1), and section 9 says so.

## 2. What was asked for and what was built

| # | Scenario | Where it lives |
|---|---|---|
| 1 | Staff add a person and set up a meal on any pattern | The donor book and "Set up regular days" on `/bookings/sponsors`, `StandingSponsorship` (domain), `StandingSponsorshipHandler` (section 4) |
| 2 | Several days a month for one person | The monthly pattern's 1-31 mask, every N months (section 4) |
| 3 | Their days show as taken | Each day is an ordinary booking that holds its place; the calendar's ringed dot and "Regular donor" (section 5) |
| 4 | Another donor asks for a held day; the institution decides | "Held by a regular donor: ask for it" on `/i/[slug]`, the tray beside booking requests, `DayRequestHandler` (section 6) |
| 5 | The sponsor cannot give on a day | "Let this day go" for staff; "I can't give that day" in the sponsor's own My bookings (section 7) |
| 6 | The institution needs the day | A blocked date lets the standing days go and texts each sponsor (section 6) |
| 7 | Two sponsors swap days, once or for good | `StandingSwapHandler` (section 6) |

Little schema had to be designed from nothing. `org.InstitutionDonors` and the booking's
`InstitutionDonorId` had been in the first schema and never used ("the donor CRM", cut in plan 03). The
work added three tables (`booking.StandingSponsorships`, `booking.StandingExceptions`,
`booking.DayRequests`), a booking source `Standing`, ten row-level security predicates, a screen in the
permission catalogue and 24 SMS templates.

## 3. The donor book: people who may never use the app

### The problem

A regular donor is a person in the institution's book, not necessarily a Sethika account. Sethika must
let the institution keep them, text them about their own days, and, when the same person does sign up,
show them their days, without guessing who holds a phone number.

### The design (K1, K13, K14, K36, K37)

A row of the donor book holds a name, a phone, notes, the language of their texts, and "this person
agreed to receive texts", stored with the staff member who ticked it and when (Q2). Without that box
Sethika never texts someone who has no account; the institution tells them itself, and the screens say
"not texted".

A row is **linked** to an account only through a *confirmed* phone, and never to a deleted account,
whose number a new person may hold. Linking happens in two places. When staff add or edit a donor, the
handler looks for the account. When the person signs up, or signs in with a password or a code,
`SqlDonorBookLinks` links every donor book row, in every institution, that has their phone and no
account yet (K36). An institution has at most one row per account (`UX_InstDonor_User`); a row that
staff linked to someone else stays as they left it.

Linking alone was not enough, and the browser would have shown it. A standing day is booked with the
account the row had at the time, which for a person not yet in the app is none. A person linked at
sign-in would have seen an empty My bookings until the job booked a new day. So in the same transaction
the row's days still to come become theirs. The same gap existed when staff changed a donor's phone
(K37); it was found while building K36 and closed the same way.

## 4. One row for any pattern, and each day an ordinary booking

### The problem

"Any scenario" could mean a table per pattern, or a rule language. Either way the days still have to
appear in the calendar, count against the places left and be impossible to overbook, and the institution
must be able to change one day without breaking the rest.

### The design (K2, K3, K15, K18, K20-K22)

A standing sponsorship is one row: the donor, the meal slot, a pattern and its fields, a start date, an
optional end date, the headcount and who brings the meal. The patterns are `Once`, `Daily`, `Weekly`
(a weekday mask), `Monthly` (a mask of days 1-31, so the 5th and the 20th are one row) every N months
counted from the start month (2 is every other month, 3 is quarterly) and `Yearly`. A day a month does
not have, such as the 31st of April or 29 February in a common year, is **skipped, not moved** (Q3): the
one rule that never puts two days in one month by surprise. The database refuses any other shape
(`CK_Standing_Shape`); a test found that `EndDate = StartDate` alone let a `Once` with no end through,
because a CHECK passes on unknown (K15).

**Each day is an ordinary booking** (`Source = 'Standing'`) made by the same engine as every other: the
Redis lock, the fresh read of the slot's rules, the conditional `UPDATE ... WHERE BookedSponsors <
MaxSponsors`, the outbox message, in one transaction, and approved at once because the institution made
it. The calendar, the places left, the reminder, completing and no-show work with no new code, and the
overbooking guarantee of Phase A covers standing days without one new line. Unlike a yearly booking
(Phase C, "a promise to try"), a standing day **holds** its place (Q5): that is the point of the
institution's own arrangement.

When staff set one up, the next 31 days are booked while they wait, and the answer lists what became of
each: booked, or why not (full, blocked, too soon). A pattern that collides with one full day is saved;
that day is listed, not refused (K9). A nightly Hangfire job (`standing-bookings`, 00:45 Colombo) books
the rest of each slot's booking window. Booking a whole year in the request was measured first: a daily
sponsorship took 17.5 seconds, about 48 ms a day, each its own lock and transaction (K18).

Two rules keep the job from undoing people's decisions (K20). A day with any booking of the
sponsorship, in any status, or any recorded change, is never booked again, so a day cancelled on the
calendar stays cancelled. A day the engine refuses writes nothing and is tried again each night, so a
day that opens later is still booked. Standing days write no per-day text: a daily sponsor would be
texted every day (K21).

The form's preview of the next twelve days and their clashes is **the server's** (K31): it runs the
night job's own arithmetic and the calendar's own rules. A TypeScript copy of the pattern and of the
engine's rules would have drifted.

## 5. Changing one day without breaking the arrangement

### The problem

A cancelled booking is not enough to record "the sponsor cannot come on the 5th": the next night's run
would see a free day of the pattern and book it again. And a change to a day can meet the job booking
that same day.

### The design (K4, K19, K23)

Every change to a day is a row of `booking.StandingExceptions`: `Skipped`, `Released` (given to someone
who asked), `Blocked`, `MovedTo` or `MovedFrom` (a swap), one per sponsorship and day
(`UQ_StandingExc_Day`). Letting a day go cancels its booking, gives the place back and writes the
calendar's hint in the same transaction.

The database runs with `READ_COMMITTED_SNAPSHOT`, so a reader does not wait for a change that has not
committed: a skip and the night's run could each miss the other, and the day would be booked although
it was let go. So booking one of a sponsorship's days, skipping a day and ending it each take **an update
lock on the sponsorship's row** inside their transaction, after the slot-day's Redis lock, and read its
state under it (K19). Both races are forced in the tests (section 8). Ending keeps today's meal, which
is already being cooked.

## 6. Asking, blocking and swapping

**Asking for a held day (K6, K25-K27).** A donor on `/i/[slug]` sees a day full only through a regular
donor as a gold bowl: "held by a regular donor: you can ask for it". Neither a donor's session nor the
public calendar may read who holds a day, so "held" is read past row-level security as counts only, by
`SqlStandingHolds`, which returns for each slot-day how many standing bookings it has and the most people
one brings: no name, phone or number (K25). The request waits in the institution's tray. **Giving** is
one transaction: the slot-day's lock, the sponsorship's row lock, the standing booking cancelled, the
asker booked through the engine's rules and approved, the day recorded `Released`, both texted. Anything
refused on the way leaves all of it as it was. Asking holds nothing.

**The institution's own use of a day (K7, K28).** Adding a blocked date over standing days lets each go
(`Blocked`) in the block's transaction, from today to the block's end, booked or not, so a day beyond the
booking window is never booked later. Each sponsor gets one text per block. Ordinary donors' bookings on
those days are listed for staff to call, not cancelled (Q4).

**Swaps (K8, K29).** *This time*: both days must be booked; the two bookings are cancelled and each
sponsor booked on the other's day, four changes recorded. *From now on*: each sponsorship ends the day
before and a new one takes the other's pattern from the other's day. Both slot-days' Redis locks are
taken in date order and both row locks in id order, so two swaps can never wait on each other in a
circle.

**Texts (K10, K30)** go through the outbox with a dedup key of the template, the sponsorship and what
makes the change one of a kind, so a change is texted once however often the outbox runs.

## 7. The sponsor's own "I can't give that day"

### The problem

Scenario 5 says the sponsor can say ahead that they cannot give on a day. Staff could do it for them
from K.2. A sponsor who uses the app should not have to phone. But their session reads their days only
as their own bookings and **cannot write** `StandingExceptions`, which is the institution's table under
row-level security (K16).

### The design (K24, K35)

The sponsor's My bookings shows "I can't give that day" on each regular day still to come, in place of
Cancel. It calls `POST /api/bookings/{bookingNo}/skip`. Behind it, `PlatformOwnStandingDays` opens a new
DI scope stamped as the platform, for this one call, and runs **the staff skip itself**
(`StandingSponsorshipHandler.SkipAsync`): the same locks, the same rules, the same record, the same
text. Before anything changes, the sponsorship is found only through a booking whose `DonorUserId` is
the caller **and** a donor book row linked to the caller now. A booking that is someone else's, an
ordinary booking, or a day of a row since linked to another account reads as "not found".

A copy of the skip written as SQL for the donor's side would have been narrower to read, but it would
have had to repeat the row lock, the rules on completed and past days and the text, and drift from them.
The request contributes only two inputs, the caller's id and a booking number, and both are checked
against the caller.

One rule differs from an ordinary booking: a donor may cancel an ordinary booking only until the end of
the day before (plan 03 Q1), but **a regular day can be let go on the day itself**, as staff can. The
user chose this (2026-11-04).

## 8. Security

- Every new endpoint has a permission policy and is in `EndpointPolicyTests`. Staff need the new screen
  `BOOKINGS.SPONSORS` (VIEW, CREATE, EDIT; STAFF hold VIEW only); giving or keeping an asked-for day is
  `BOOKINGS.CALENDAR.APPROVE` or `REJECT`; a donor asking or letting their own day go holds
  `MYBOOKINGS.BOOKINGS.CREATE` or `EDIT`, which apply only in a personal session.
- Row-level security on the three new tables by tenant, and the donor book's missing `AFTER UPDATE`
  block added. Day requests by tenant or owner. Tested from the institution, another institution, the
  linked sponsor, another donor and the platform.
- **Reach past row-level security from a request** is now in three narrow classes and one scope:
  `SqlShareLinks` (Phase C), `SqlStandingHolds` (counts only), `SqlDonorBookLinks` (keyed by the
  signed-in person's id, returning a count) and `PlatformOwnStandingDays` (one skip, of the caller's own
  day). Each stamps its own connections or scope as the platform; nothing else in the request gains
  that reach.
- Consent: a donor without an account is texted only with the box ticked, and the tick names who ticked
  it and when.

## 9. Testing

As in every phase, against **a real SQL Server and a real Redis**: the row locks, the unique indexes,
the CHECKs and the RLS predicates cannot be faked.

- **Domain tests** for the pattern arithmetic: every pattern, masks, every N months from the start
  month, 29 February, the 31st in short months, start and end dates, the day request's rules.
- **Integration tests:** `DonorBookEndpointsTests` (14), `StandingSchemaTests` (9),
  `StandingSponsorshipTests` (22), `StandingChangesTests` (17) and `StandingSponsorLinkTests` (13).
  Among them, **five races forced on the sponsorship's row lock**: a skip against the night's run, an
  end against the night's run, two staff giving the same day to two askers, two swaps of one day with
  different partners, and a block against the night's run. Each holds the row from another connection, waits until SQL Server's
  lock view shows every request queued on it, then lets them go.
- **Browser tests (Playwright)**, `e2e/sponsors/donor-book.spec.ts` and `e2e/sponsors/standing.spec.ts`,
  desktop and 320 px: a monthly sponsor with two days and the clash shown first; a swap and the
  calendar's marks; a donor asking for a held day and the institution giving it; a person the donor book
  knows signing up, seeing their regular days and letting one go; the forms in Sinhala and Tamil.
  Found in the browser and fixed: the Tamil heading was wider than a 320 px phone; the month-day chips
  overlapped at 320 px; a field's error stayed after it was fixed.
- **Mutation testing.** Each guarded line was broken on purpose and a test had to fail: 65 mutants in
  K.1 (27 C#, 30 SQL and RLS, 8 web), 46 in K.2, 43 in K.3, 21 in K.4 and 23 in K.5 (18 C#, 5 web), all killed. Some
  survived at first, and each led to a test or a fix:
  - a `Once` with no end passed the CHECK (unknown is not false), so `CK_Standing_Shape` now names it;
  - skipping a past day of a running pattern was accepted until a test asked;
  - several mutants first failed to build (warnings are errors) and were rewritten to compile, so that
    they proved something;
  - in K.5, letting a regular day go by the ordinary cancel call survived the first browser test,
    because both answers look the same to the donor. The test now reads the institution's side: the
    day must be recorded as skipped, not merely cancelled.
- **The whole suite** at the end: FILL IN after the final run (dotnet test and every Playwright spec).

## 10. Limitations and what was left out

- **Poya and festival patterns** ("every Poya day"): `core.SpecialDays` is not seeded, because the
  dates must be confirmed with real institutions (CLAUDE.md open items).
- **Importing a donor book** from a spreadsheet; a person's own view of the pattern (they see their
  days, not the arrangement); withdrawing a day request (the table allows it).
- **Payments or pledges** for standing days belong to Phase D's donations.
- The Sinhala and Tamil texts and screen words are drafts, listed in `docs/translations-to-review.md`.
- To make room, the gratitude wall and part of Phase D's reports were cut (plan 08 Q1).
