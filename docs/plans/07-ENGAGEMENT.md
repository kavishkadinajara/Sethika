# Plan 07 — Phase C: recurring bookings, the Memorial Dana Planner, live slot updates

Branch: `phase-c-engagement`, cut from `develop` once the Phase B pull request is merged
(`git switch -c phase-c-engagement develop`). If C starts before that merge, cut it from `rcsi-on` and
merge `develop` in later; never rebase a pushed branch.

Status: **drafted 2026-11-01; the user answered §8 the same day (co-sponsors in, email cut for now,
the C# engine and the three packages OK, the draft rules OK). H1-H19 agreed. C.1 done (2026-11-01,
committed by the user). C.2 done 2026-11-01 (schema on `Sethika_Test` and the dev `Sethika`; the user
kept the "day of passing is day 1" count, H24); H20-H27 recorded while building. C.2 committed by the
user (f5cb142). C.3 done 2026-11-02 (H28-H31; no schema change). Next: C.4.**
Update this line and the §7 boxes whenever either changes; a fresh session trusts them.

Decision numbers here are **H1, H2, …**. Plan 03 uses C1-C12 and E1-E35, Phase E V1-V39, plan 06 G1-G11.

## 0. What the proposal promises (the report must match this list)

Proposal v3, Phase C "Engagement" ("Forgotten commitments"):

1. **Recurring yearly bookings.** "For occasions that repeat every year, such as a parent's memorial
   dana, a donor can create a recurring booking, and a scheduled job reserves the slot for each
   coming year and notifies both sides."
2. **Real-time slot updates across open calendars.** "When a slot is booked, SignalR pushes the
   change to every open calendar so users always see current availability." Technology list: "SignalR
   with a Redis backplane for live slot updates; Hangfire for reminders and recurring bookings."
3. **SMS and email reminders.** "Background jobs send SMS and email confirmations and reminders
   before each sponsorship." The SMS half was built in Phase A (plan 03, C11); email was not (Q2).
4. **Donor profiles.** "Donors keep a profile with their booking and donation history and their
   preferred institutions." Booking history exists (`/bookings/mine`); donation history is Phase D;
   preferred institutions are new.

Added outside the proposal text:

5. **The Memorial Dana Planner** (CLAUDE.md, Phase C). The user decided on 2026-10-31: build the engine,
   seed the rules as `DRAFT`, and make it configuration-driven so that it fits every religion (rules are
   data per faith; no faith in code).
6. **Co-sponsors** (`BookingContributors`, CLAUDE.md). Not in the proposal (Q1).

## 1. What already exists

- Tables, from the first schema, never used: `booking.RecurringBookings` (slot, `RecurMonth`/`RecurDay`,
  `StartYear`/`EndYear`, `Headcount`, `InMemoryOf`, `MemorialPlanId`, `LastGeneratedYear`, `IsActive`),
  `booking.MemorialPlans` (donor, deceased's name, relationship, `DateOfPassing`, religion, photo,
  notes), `core.MemorialRules` (per religion: `OffsetDays`, `OffsetMonths`, `RepeatsAnnually`, names in
  three languages), `booking.BookingContributors`, `auth.DonorProfiles`, `auth.FavoriteInstitutions`,
  `core.SpecialDays`. `booking.Bookings` already has `RecurringBookingId`, `MemorialPlanId`,
  `MemorialRuleId` and `Source` values `Recurring` and `Memorial`. **No schema design is needed**; RLS
  and seeds are (H13, H7).
- None of those tables is in `sec.TenantIsolationPolicy`. `RecurringBookings` has `InstitutionId`;
  `MemorialPlans` and `FavoriteInstitutions` belong to a donor only.
- `core.LookupTypes` has `RELIGION`, `RELATIONSHIP` and `OCCASION`, but **no values** are seeded.
- The booking engine: `BookMealHandler.BookAsync` (Redis lock, fresh read, `MealSlotRules.CanBook`,
  conditional `UPDATE`, CHECKs, outbox `BookingRequested`/`BookingConfirmed`), `BookingDecisionHandler`,
  the 18:00 `BookingReminderJob`, `BookingSmsHandler` with `DedupKey`s. Slots have `MinAdvanceDays`
  and `MaxAdvanceDays` (default 365) and `RequiresApproval`.
- The outbox runs every minute (`OutboxJob`, Hangfire). Hangfire 1.8 on SQL Server storage, 2 workers.
- **No SignalR and no email** anywhere in the code. `auth.Users.Email` is optional at sign-up and
  never confirmed (`EmailConfirmed` is always 0); sign-in and every text are by phone.
- Screens: `InstitutionPage` + `BowlCalendar` + `BookingSheet` (donor), `CalendarScreen` (institution),
  `MyBookings`, the donor's home `DonorDay`, the search page `/find` (Phase B).

## 2. Decisions

| # | Decision | Why |
|---|---|---|
| H1 | Phase C delivers: recurring yearly bookings (H2-H5), the Memorial Dana Planner (H6-H8), live slot updates (H9-H11), preferred institutions (H12) and co-sponsors (H16-H19, the user, Q1). **Email is cut for now** (the user, Q2): every confirmation and reminder already goes by SMS in the donor's language; email is built on the hardening day only if time is left, and the report says so otherwise | The proposal's Phase C row plus the user's planner and co-sponsor decisions |
| H2 | **A recurring booking is a promise to try every year, not a held slot.** A `RecurringBookings` row (slot, month and day, headcount, "in memory of", years). Each year's booking is an ordinary booking with `Source = 'Recurring'` and `RecurringBookingId`, made by the same engine, approved, cancelled and reminded like any other | One engine, one judge (C4, G7). Nothing is reserved that the institution cannot see in its calendar, and the overbooking guarantee (C2) covers recurring bookings with no new code |
| H3 | A daily Hangfire job `recurring-bookings` (00:30 Colombo, platform scope) books each active recurring booking's next occurrence **on the first day it enters the slot's booking window** (`date − MaxAdvanceDays ≤ today`), one at a time, through `BookMealHandler`'s own path (lock, rules, conditional `UPDATE`, outbox). `LastGeneratedYear` is written in the same transaction, so a year is tried once and a rerun is a no-op. **Not a stored procedure** (Q3) | The volume is one booking per recurring booking per year. A procedure would have to re-implement `MealSlotRules`, skip the Redis lock and write its own outbox rows. CLAUDE.md allows procedures for set-based jobs; it does not require one here |
| H4 | When a year cannot be booked (slot off, not on that weekday, out of season, blocked, full, institution not active, no confirmed phone), the donor gets one SMS naming the date and the reason, with a link to `/find?date=` for that day. No daily retries. "Both sides" on success: the donor's usual SMS, and the request in the institution's calendar and "today" (institutions get no SMS, C11) | A retry loop would compete with other donors for weeks and text the donor repeatedly; Phase B already answers "where else can I give that day?" |
| H5 | Created two ways: "Book this every year" in the booking sheet (this year's booking now, then yearly from next year), and from a yearly memorial observance (H6). Month and day are taken from the date; 29 February books 28 February in a common year; `EndYear` optional. The donor stops it from My bookings (`IsActive = 0`, history kept); a year already booked stays and is cancelled the usual way | The smallest change to the existing flow. Stopping must never silently cancel a booking the institution has already accepted |
| H6 | **Memorial Dana Planner.** The donor enters the deceased's name, relationship (optional), date of passing and religion. A pure domain function computes the observances from `core.MemorialRules` (date of passing + `OffsetMonths` + `OffsetDays`; `RepeatsAnnually` gives each year's date). Each upcoming observance offers "Find a place" (`/find?date=`); a booking made from it carries `MemorialPlanId`, `MemorialRuleId` and `Source = 'Memorial'`; a yearly observance offers "every year", which creates a `RecurringBookings` row with `MemorialPlanId` | Configuration, not code: a faith, or an institution's custom, is a row. The planner reuses the search (Phase B) and the engine instead of a second booking path |
| H7 | **Seeded as drafts** (the user, 2026-10-31): `RELIGION` values Buddhism, Hinduism, Islam, Christianity (Sinhala and Tamil names as drafts), and `core.MemorialRules` exactly as the table's own comment lists them (Buddhist 7th day, 3 months, yearly; Hindu 31st day; Muslim 40th day; Christian 40th day). Every rule's name starts with `DRAFT`; listed in DECISIONS.md's unconfirmed reference data | CLAUDE.md forbids inventing these facts. Even "the 7th day" is ambiguous (date of passing + 6 or + 7?): the offsets are data a reviewer corrects without a code change |
| H8 | A memorial reminder: one SMS to the donor 7 days before each upcoming observance that has no booking linked to it yet, from a daily job, `DedupKey memorial:{plan}:{rule}:{date}` | The planner's point is not to forget; one text, never two |
| H9 | **Live updates (SignalR).** Hub `/hubs/availability`, anonymous (availability is public). A calendar joins the group of one institution (`institution:{slug}`). The message is a hint, `AvailabilityChanged { dates }`; the calendar refetches its month from the API | One source of truth, the API (G7). A lost or late hint costs freshness, never correctness: the engine still refuses a full slot |
| H10 | **Through the outbox** (CLAUDE.md): every change of a slot-day's places (book, reject, cancel, a blocked date added or removed, a slot changed) already writes an outbox message in its transaction; a new handler sends the hint. The endpoints wake the outbox job right after commit (a Hangfire enqueue), so a calendar hears within about a second; if the wake fails, the minute run delivers. Redis backplane (`Microsoft.AspNetCore.SignalR.StackExchangeRedis`) with a channel prefix from configuration; tests use their own prefix, like `GeoIndex:Key` | The proposal names the backplane, and Azure App Service scales out: a booking on one instance must reach a calendar on another. Redis pub/sub channels ignore the database number, so the prefix keeps Sethika (and its tests) apart from other projects on the same Redis |
| H11 | Live: the institution's public page calendar and the institution's booking calendar. On reconnect a calendar refetches. `/find`'s month counts are not live | The two calendars the proposal means ("every open calendar"); counts across all institutions would need a broadcast to every searcher |
| H12 | **Preferred institutions:** a heart on `/i/[slug]` for a signed-in donor (`auth.FavoriteInstitutions`) and "My places" on the donor's home. Booking history is My bookings; donation history comes with Phase D | The proposal's donor profile, at the smallest size that is real |
| H13 | **RLS** for the new rows: `RecurringBookings` by `fn_TenantOrOwner(InstitutionId, DonorUserId)`; `MemorialPlans` and `FavoriteInstitutions` by owner only (a new predicate or the same function with no tenant). The jobs run as the platform | A donor's plans and the names of their dead are theirs alone; the institution sees a recurring booking aimed at it |
| H14 | Texts (en/si/ta, drafts in `docs/translations-to-review.md`): `RECURRING_NOT_BOOKED`, `MEMORIAL_REMINDER`; the yearly booking itself uses the existing booking texts | Nothing new for what is already a booking |
| H15 | New packages (Q4, agreed): `Microsoft.AspNetCore.SignalR.StackExchangeRedis` (API), `@microsoft/signalr` (web), `Microsoft.AspNetCore.SignalR.Client` (integration tests only). SignalR's server is part of ASP.NET Core | Each is Microsoft's own package for exactly this; no alternative avoids them |
| H16 | **Co-sponsors.** The donor of a future `Requested` or `Approved` booking invites people to share it: a name, an optional note ("brings dessert"), and Sethika makes a link `/share/{token}` (32 random bytes; only the SHA-256 is stored in `InviteTokenHash`). **The donor sends the link themselves** (WhatsApp, a text); Sethika texts no one it was not asked to by that person. At most 10 invitations per booking | A number typed by someone else is not consent to be texted. The link is a bearer secret, so only its hash is kept, like the refresh tokens |
| H17 | Opening the link shows the booking's public summary (institution, meal, date, the donor's display name, "in memory of"). A signed-in person joins or declines; joining sets `UserId`, `Status = 'Joined'`, `JoinedAt`. The donor cannot join their own booking, and one person joins a booking once. A cancelled, rejected or past booking shows its state and cannot be joined. The donor can withdraw an invitation that is still `Invited` | The flow of any invitation link; a link that outlives its booking must say so rather than fail |
| H18 | **Schema and RLS:** `BookingContributors` gains `InstitutionId` (composite FK `(BookingId, InstitutionId) → Bookings(Id, InstitutionId)`, CLAUDE.md's tenant rule) and `InvitedBy` (the booking's donor), and a filtered unique index on `(BookingId, UserId)`. A new predicate lets a row be seen by the institution, the contributor (`UserId`) and the donor who invited (`InvitedBy`). Two reads cannot run under the person's own session and use a narrow platform-scoped query instead, each documented and tested: the summary behind a token (looked up by its hash only), and a joined contributor's view of the booking itself (`Bookings` RLS shows it only to the donor and the institution) | The table is empty everywhere, so the columns cost nothing. A contributor must see the booking they joined without `Bookings`' RLS being widened for every query |
| H19 | Joined contributors appear on the donor's booking ("shared with"), on the institution's booking detail (names only), and under "Shared with me" in the contributor's My bookings. A joined contributor gets the 18:00 reminder the day before, like the donor (`DedupKey` per contributor). Cancelling or rejecting the booking ends it for everyone; contributors see the state | The point of sharing a memorial dana is that the siblings come; the reminder is what makes them |

Decided while building (recorded here so the report and DECISIONS.md match the code):

| # | Decision | Why |
|---|---|---|
| H20 | (C.1) A yearly booking is named by a new `PublicId` column (appended last, so adding it never rebuilds a table under RLS), and copies the first booking's `FulfilmentMode` (a new column). A unique index `(DonorUserId, MealSlotId, RecurMonth, RecurDay) WHERE IsActive = 1` keeps one active yearly booking per donor, meal and day; "every year" on a day that already has one joins it | §3 left the id open. A booking number names a booking, not the promise behind many |
| H21 | (C.1) The refusal text is two templates, `RECURRING_FULL` and `RECURRING_CLOSED` (H14's `RECURRING_NOT_BOOKED` split): full when every place was taken, closed for every other refusal | The donor's next step differs: another day at the same place, or another place |
| H22 | (C.1) The year is claimed by a conditional `UPDATE` of `LastGeneratedYear` in the booking's own transaction. A busy slot lock, or the donor booking the same day at the same moment, claims nothing and the next run tries again; a day already past, a donor without a confirmed phone, or a day the donor already holds claims the year with no text | One try per year (H4) without losing a year to a lock that was busy for a second |
| H23 | (C.2) A booking for an observance must fall on that observance's own day (`not_an_observance`, 400), on the donor's own active plan and an active rule of the plan's faith (`memorial_not_found`, 404 otherwise). "Every year" is allowed only for a yearly observance. An observance is **booked** when a live (requested or approved) booking for the plan is on its day; a yearly booking made from it carries the plan, and so does each year the job books | The planner can then tell what is booked without guessing which nearby day a booking meant. A donor who wants another day books it as an ordinary booking |
| H24 | (C.2) "The 7th day" is the date of passing + 6 (the day of passing counted as the first), and the 31st and 40th days likewise + 30 and + 39; the month offsets add months first. Drafts, in the seed's comment and the translations list, for a reviewer to confirm per faith | H7: the offsets are data. The convention had to be one, stated, and changeable without code |
| H25 | (C.2) The memorial reminder covers tomorrow to 7 days ahead, not exactly the 7th day, with `memorial:{plan}:{rule}:{date}` as the dedup key; it never carries the deceased's name (typed by the donor, and template values are never typed text) | A plan made 3 days before an observance, or a night the job did not run, still gets one text; never two |
| H26 | (C.2) Archiving a plan stops its reminders and hides it; bookings and yearly bookings made from it stay until the donor cancels or stops them in My bookings | As H5: nothing an institution accepted is cancelled as a side effect |
| H27 | (C.2) `RELATIONSHIP` values (mother, father, ...) are seeded as plain words, not drafts; the planner is a new donor screen `MYMEMORIAL.PLANS` (`/memorial`) under the BOOK module, `Self` scope, VIEW/CREATE/EDIT granted to the DONOR role. RLS: a new `sec.fn_Owner(DonorUserId)` (the donor and the platform, never an institution) with FILTER and BLOCK AFTER INSERT/UPDATE | Family words are not facts about a faith; the plan needs its own permissions so a role can never hold it by accident |
| H28 | (C.3) The hint is an outbox message of its own, `AvailabilityChanged` (aggregate `Institution`; payload the institution's id and the first and last day that changed, both null for "any day"), written in the change's own transaction by: a booking; a rejection or cancellation that gives a place back; a blocked range added or removed (its range); a slot added, changed, switched off or on (any day). Approving writes none, because it changes no place (a colleague's approval shows on the calendar's next load) | H10 assumed the existing events would do. But the outbox runs one handler per event type, the booking events already have their texts, and slots and blocked days had no event at all. A range, because one blocked range can cover months |
| H29 | (C.3) The hub is at `/api/hubs/availability` (H9 and §3 said `/hubs/availability`), and its message is `AvailabilityChanged { slug, from, to }`. A connection watches at most 3 institutions (`too_many_watched`), and a slug is 1-150 characters (`invalid_slug`). Connecting is **not** rate-limited, although §3 said it would be | Under `/api`, the web's one proxy rule (A3) carries the hub and the browser keeps one origin. Each watched institution is a Redis subscription on every instance, so the cap bounds what one connection costs them. Sethika's Redis rate limiter is an endpoint filter, and a hub's endpoints never run endpoint filters, so a limit on connecting waits for the hardening day (§6) |
| H30 | (C.3) The wake is an endpoint filter, `WakesOutbox()`, on every endpoint whose change writes outbox messages: booking, the donor's cancel, the three decisions, slots, blocked days and the listing. After a 2xx result it queues `OutboxJob` with Hangfire. A refusal wakes nothing. With jobs off (tests) the wake does nothing. A failed enqueue is logged, and the minute's run delivers | The wake comes after the commit, never inside it: a job queued before the commit could run first and find nothing. One line per endpoint, instead of a call in every handler |
| H31 | (C.3) A calendar quietly refetches the month it shows, with no loading state, when a hint touches that month and after a reconnect. The public page also forgets any other cached month the hint touched. The institution's calendar also reloads its waiting list and the shell's counts | A hint says which days changed, not what they now hold (H9). A reconnect may have missed hints |

## 3. API

- `POST /api/bookings` gains `everyYear: bool` (H5). `GET /api/bookings/recurring` (mine),
  `POST /api/bookings/recurring/{id}/stop`. A recurring booking is addressed by a public id, never `Id`
  (a `PublicId` column is needed, or the first yearly booking's `BookingNo`: decided in C.1).
- `GET /api/memorial/religions` (religions with active rules), `GET/POST /api/memorial/plans`,
  `GET /api/memorial/plans/{id}` (with its observances), `POST /api/memorial/plans/{id}/archive`.
  Bookings made from an observance send `memorialPlan` and `memorialRule`.
- `PUT/DELETE /api/me/places/{slug}`, `GET /api/me/places`.
- `POST /api/bookings/{no}/contributors` (the donor invites: `{ name, note }` → `{ link }`),
  `DELETE /api/bookings/{no}/contributors/{id}` (withdraw), `GET /api/share/{token}` (anonymous summary,
  rate-limited), `POST /api/share/{token}/join`, `POST /api/share/{token}/decline`,
  `GET /api/bookings/shared` (bookings I joined).
- Hub `/hubs/availability`: `Watch(slug)`, `Unwatch(slug)`; server message `AvailabilityChanged`.
- Every endpoint gets its policy and goes into `EndpointPolicyTests`; the hub is anonymous and
  rate-limited per connection attempt.

## 4. Tests (what must be proved)

- **Recurring:** the job books next year's occurrence on the day it enters the window and not the day
  before; a rerun books nothing twice; each refusal reason texts the donor once and books nothing; 29
  February; stopping keeps a year already booked; two job runs at once (forced, as in Phase E) still
  give one booking; the yearly booking is refused like any other when the slot is full.
- **Planner:** the observance dates from the rules (months then days; month ends; leap years); a new
  rule row changes the dates with no code change; RLS: another donor and an institution see no plan.
- **Co-sponsors:** invite, open, join, decline, withdraw; a token that does not exist, a used one, one
  for a cancelled or past booking; the donor cannot join their own; one person joins once; the 11th
  invitation is refused; RLS from each session (donor, contributor, institution, another donor, another
  institution); the token summary shows nothing beyond H17's fields; the joined contributor's reminder,
  sent once.
- **Live:** a client connected to the hub hears `AvailabilityChanged` after a booking, a rejection, a
  cancellation and a blocked date; **two API hosts on one Redis**: a booking on host A reaches a client
  on host B (the backplane); a client watching another institution hears nothing.
- **Mutations:** the window check, `LastGeneratedYear`, the refusal text, the observance arithmetic, the
  group name, the outbox wake, the channel prefix, each RLS predicate.
- **Browser** (desktop and 320 px, en/si/ta): a donor books "every year" and sees it under My
  bookings; a memorial plan shows its observances and books one from `/find`; two browsers on one
  institution's page, one books, the other's calendar changes without a reload; the heart and My places.

## 5. Screens

- Booking sheet: "Book this every year". My bookings: a "Every year" section with stop.
- `/memorial` (donor): plans, a plan's observances with dates and their state (booked, to book, past),
  "Find a place" and "every year".
- The public page and the institution's calendar refresh live. The heart on `/i/[slug]`; My places on
  the donor's home.
- A booking in My bookings: "Share this dana" (name, note → a link to copy or share), the invited and
  joined people, withdraw. `/share/[token]`: the summary, join or decline (sign-in first if needed).
  My bookings: "Shared with me". The institution's booking detail: "Shared with".

## 6. Out of scope (the report says so)

- Email confirmations and reminders (the user, Q2): built on the hardening day only if time is left.
- Texting an invitation to a co-sponsor (H16); contributors' shares of a cost or a donation.
- Live counts on `/find`; live updates in the mobile app; SignalR for anything but availability.
- A per-address limit on connecting to the hub (H29): the hardening day, if time is left.
- Confirmed memorial rules and religion names (drafts until a reviewer confirms them, H7).
- `core.SpecialDays` (poya, festivals): still unseeded (CLAUDE.md's open items); nothing here reads it.
- The waitlist and availability alerts (designed, optional).

## 7. Work breakdown

- [x] **C.1 Recurring bookings**: RLS, public id, `everyYear` on booking, the job, the refusal text, stop.
  Tests and mutations as §4. ~0.75 day. **Done 2026-11-01:** domain 15, integration 14
  (`RecurringBookingTests`: the window's first day and the day before, rerun, two runs at once forced,
  full and closed texted once per year, 29 February, stop, the donor's own booking winning a forced
  race, past and no phone, HTTP and RLS); neighbours 84/84; 35/35 mutants killed (31 code, 2 API,
  2 RLS predicates) plus a surviving control. Schema published to `Sethika_Test` and the dev `Sethika`.
- [x] **C.2 Memorial planner engine**: the seeds (H7), RLS, the domain function, the plan endpoints,
  bookings from an observance, the reminder job. ~0.75 day. **Done 2026-11-01 (H23-H27):** domain 39
  (`MemorialObservancesTests`: months then days, month ends, 29 February, a yearly rule whose days
  cross the year end in a leap year, the next yearly day), integration 12 (`MemorialPlanTests`: the
  draft seeds and the form's lists, a plan's days and states, refusals with their fields, a new rule
  row and a faith with none, booking an observance and its refusals, every year through the yearly
  job, the reminder once a week ahead in the donor's language and not for a booked observance or an
  archived plan, the host's job as the platform, archiving, RLS); neighbours 99/99; 59/59 mutants
  killed (54 code incl. 4 API, 5 RLS/seed) plus a surviving control. Built in a separate worktree on
  `Sethika_C2Test` while C.1's mutations ran, then moved in byte for byte; the memorial and neighbouring
  test classes 59/59 again on `Sethika_Test`.
- [x] **C.3 Live slot updates**: the packages (Q4), the hub, the backplane, the outbox handler and wake,
  the two calendars. ~0.5 day. **Done 2026-11-02 (H28-H31):** integration 6 (`LiveAvailabilityTests`:
  each kind of change heard, with its days, by a calendar on the institution and none by one on
  another; nothing for an approval; unwatching; the hub's slug and watch limits; a successful change
  wakes the outbox and a refusal does not; **two hosts on one Redis**, a change on one heard on the
  other, and a host on another channel prefix hears nothing; the handler and notifier wired), the
  host's wake queues the outbox job (`HangfireJobsTests`), and the hub's two routes in the anonymous
  list; neighbours 154/155, the one failure being that list before the hub's routes were added (then
  the live, Hangfire and policy classes 10/10); 24/24 mutants killed (10 writers and handler, 7 hub, 2 backplane, 5 wake)
  plus a surviving control. H6 (the message's name) survived at first, because the test client
  listened by the server's own constant; the client now listens as the web does, by the literal name
  and the JSON fields, and H6 and H7 (a renamed field) are killed. Web: `touchesMonth` 4/4 mutants
  plus a control, vitest 223, tsc, eslint and `dotnet format` clean.
- [ ] **C.4 Co-sponsors**: the schema change and predicate (H18), invite, the token summary, join,
  decline, withdraw, shared with me, the contributor's reminder. ~0.75 day
- [ ] **C.5 Screens**: every-year in the sheet and My bookings, `/memorial`, sharing and `/share/[token]`,
  the heart and My places; en/si/ta; Playwright desktop + 320 px. ~1 day
- [ ] **C.6 Done**: DECISIONS.md, `docs/report/PHASE-C.md`, the full runs, the PR.

**If time runs out, cut in this order:** preferred institutions (H12), the memorial reminder (H8), the
contributor's reminder (H19), the institution's calendar going live (the public page stays live).
Never cut the engine path for yearly bookings, the co-sponsor RLS tests or the two-host backplane test.

## 8. Questions for the user (answered 2026-11-01)

Answers: **Q1 build co-sponsors in Phase C** (against the recommendation; H16-H19); **Q2 cut email for
now**, revisit on the hardening day; **Q3 OK**; **Q4 OK**; **Q5 OK**, the drafts as listed.

- **Q1 Co-sponsors.** Not in the proposal. Shared slots already let several families sponsor one meal,
  each with their own booking. Co-sponsors (one booking, invited contributors) need an invite link,
  a join flow and an RLS answer for a contributor who is neither the donor nor the institution.
  Recommendation: **cut**, and the report says so.
- **Q2 Email.** The proposal says "SMS and email". Email needs a confirmation step (no address is
  confirmed today), a provider (SMTP in production, a console sender in development, like SMS) and
  three-language templates. Recommendation: **cut** for now and say so in the report (every text
  already goes by SMS in the donor's language); build it on the hardening day only if time is left.
  The alternative is about half a day.
- **Q3** The yearly job uses the C# engine, not a stored procedure (H3). OK?
- **Q4** The three packages in H15. OK?
- **Q5** The memorial rules and religion names as drafts exactly as H7 lists them. OK, or different
  rules per faith?
