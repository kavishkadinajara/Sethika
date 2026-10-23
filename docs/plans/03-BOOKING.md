# Plan 03 — Meal slots and the booking engine

Branch: `5-booking-engine`, cut on 2026-10-23 from `4-textlk-sms` (PR #7) and rebased on
`origin/develop` the same day, after PR #6 and #7 were merged.

Status: **5.1, 5.2 and 5.3 built (2026-10-23); 5.4 is next.** §8 lists what was decided while
building them. Deadline is about **2026-11-06**, and
Phase E (religious services) and the report still need time after this. So this plan aims at
**~6 working days (2026-10-24 → 10-31)**. If it runs over, the cut order is in §4.
Last updated 2026-10-23. Update this line and the §5 boxes whenever either changes. A fresh session
trusts them and will rebuild anything they claim is missing.

---

## 0. How to resume this from a cold start

```bash
git checkout 5-booking-engine            # rebase on origin/develop first if #6/#7 are merged
cat docs/plans/03-BOOKING.md             # this file
cat CLAUDE.md docs/DECISIONS.md          # the rules this plan must obey
dotnet build Sethika.sln && dotnet test Sethika.sln   # ~25 min; 615 must pass before you add anything
```

The rules from plans 01 and 02 still hold:
- `src/Sethika.Database` is the only place schema changes are made.
- Every endpoint carries `[HasPermission]`.
- Permission decisions are never re-derived in C#.
- Anything the database enforces is tested against real SQL Server and Redis.
- Every test is shown to fail under a mutation before it counts.
- Screens are tested with the `browser-test` skill.

**Before any browser test:** `dotnet user-secrets list --project src/Sethika.Api`. If
`Sms:Provider = TextLk` is set, remove it, or every test signup sends a paid SMS
(`docs/LOCAL_SETUP.md`).

Then read §1, §2 and §3, and start at the first unchecked box in §5.

---

## 1. What already exists (do not rebuild)

### Database: in the SQL project and published, unused so far

| Object | What it gives this plan |
|---|---|
| `booking.MealSlots` | Name (en/si/ta), `StartTime`/`EndTime`, `DaysOfWeekMask`, `BookingMode` `Exclusive`/`Shared` with `MaxSponsors` (CHECK: 1 for Exclusive, 2–20 for Shared), `Min`/`MaxHeadcount`, `EstimatedCostPerHead`, `Min`/`MaxAdvanceDays`, `RequiresApproval`, `AllowedFulfilment`, `IsActive`, `RowVersion`. `UQ (InstitutionId, Name)`, `UQ (Id, InstitutionId)`. **No `PublicId`** (see C9). |
| `booking.SlotInstances` | One row per slot per date: `MaxSponsors`/`MaxHeadcount` snapshot, `BookedSponsors`/`BookedHeadcount`, `Status` `Open`/`Full`/`Blocked`/`Closed`. **CHECKs make overbooking impossible** (`BookedSponsors <= MaxSponsors`, `BookedHeadcount <= MaxHeadcount`). `UQ (MealSlotId, SlotDate)`. **No RLS.** |
| `booking.Bookings` | `BookingNo` (UNIQUE), tenant FK to the slot instance, donor snapshot (`DonorName`, masked `DonorPhone`), `Headcount`, `FulfilmentMode`, `OccasionLookupId`, `InMemoryOf`, `Notes`, `Source`, `Status` (`Requested`/`Approved`/`Rejected`/`Cancelled`/`Completed`/`NoShow`), decision and cancel fields, `RowVersion`. CHECKs: a cancelled booking has `CancelledAt`, a rejected one has a reason. **`UX_Bookings_DonorPerSlot`**: one active booking per phone per slot-day. |
| `booking.BookingStatusHistory` | Every transition. |
| `booking.BlockedDates` | A range, optionally for one slot, with a reason. |
| `booking.MealSlotSeasons` | If a slot has seasons, it is bookable only inside them. |
| RLS | `Bookings`: `fn_TenantOrOwner(InstitutionId, DonorUserId)`, FILTER plus BLOCK after INSERT and UPDATE, so the institution sees its own and the donor sees theirs. `MealSlots`, `BlockedDates`: public read, tenant-only write. |
| RBAC | Module `BOOKINGS` (INSTITUTION portal, `IsCore = 1`, so always enabled), screen `BOOKINGS.CALENDAR` (`/bookings/calendar`), permissions `.VIEW` and `.APPROVE`. OWNER and MANAGER have both; STAFF has VIEW. |
| `org.Institutions` | `Slug`, `Status`, **`IsPubliclyListed`** (0 at approval, B7), district, coordinates. |

Unused and **out of scope** here (§7): `Waitlist`, `BookingContributors`, `RecurringBookings`,
`MemorialPlans`, `org.InstitutionDonors`, `AvailabilityAlerts`/`Searches`, `GratitudePosts`.

### Backend

- **From plans 01 and 02:** `ICurrentUserContext`, the session-context interceptor and the Dapper
  factory, `ISethikaDbContext`, `[HasPermission]`, `INumberSeries` (`UPDLOCK`, inside the caller's
  transaction), the outbox (`core.OutboxMessages`) and its Hangfire processor, `ISmsSender` with
  `DedupKey`, `notify.Templates` (en/si/ta), and the Redis connection (permission cache, OTP, rate
  limiter).
- **Not there:** any Redis *lock*. `IRegistrationLocks` is `sp_getapplock`, not Redis. Also missing:
  any `Booking` code (`Application/Booking/` is an empty folder) and FullCalendar in `web/`.

---

## 2. Decisions, with the reasoning

### C1: A request holds its place

`BookedSponsors` counts `Requested` **and** `Approved` bookings. A rejection or cancellation gives
the place back, in the same transaction.

*Why:* if only approved bookings counted, two donors could both request an exclusive slot, both see
"requested", and the institution would have to choose. That is the double-booking problem the
project exists to remove, moved one step later. A slot with `RequiresApproval = 0` creates the
booking directly as `Approved`.

### C2: Three layers, each with its own job

1. **Redis lock** `lock:slot:{mealSlotId}:{yyyy-MM-dd}`: `SET NX PX 10000` with a random token,
   released by a Lua compare-and-delete. It serialises attempts on one slot-day, so the
   get-or-create and the validation run one at a time, and a loser gets a fast, clean 409.
2. **Conditional UPDATE** in the transaction. This is the correctness guarantee:
   ```sql
   UPDATE booking.SlotInstances
   SET BookedSponsors  = BookedSponsors + 1,
       BookedHeadcount = BookedHeadcount + @headcount,
       Status = CASE WHEN BookedSponsors + 1 = MaxSponsors THEN 'Full' ELSE 'Open' END
   WHERE Id = @id AND Status = 'Open'
     AND BookedSponsors < MaxSponsors
     AND BookedHeadcount + @headcount <= MaxHeadcount;
   ```
   0 rows means 409 `slot_full`.
3. **CHECK constraints.** These make an overbooked row impossible to store, whatever the code does.
   A 547 from them is also mapped to `slot_full`.

**If Redis is down, booking goes on without the lock, and the lock failure is logged.** The
`DECISIONS.md` RBAC rule is that a Redis outage degrades rather than denies. Here that is safe,
because layers 2 and 3 alone are correct. The report should say exactly this: *the lock is an
optimisation, the database is the guarantee.*

### C3: The slot instance is created on first booking, under the lock

The handler reads `(MealSlotId, SlotDate)` and inserts it if it is missing, copying `MaxSponsors`
and `MaxHeadcount` from the meal slot. `UQ_SlotInst_SlotDate` is the backstop: on 2627, read again.

A later capacity change on the meal slot does **not** change instances that already exist. They are
snapshots, so a promise already made is not broken by an edit. Say so on the edit screen.

### C4: Bookability is a pure domain function

`MealSlotRules.CanBook(slot, seasons, blocks, date, today, headcount, fulfilment)` returns the first
reason it fails, or none. The reasons:
- `slot_inactive`
- `institution_not_active`
- `not_on_this_day` (`DaysOfWeekMask`)
- `out_of_season` (if any seasons exist)
- `date_blocked` (institution-wide or this slot)
- `too_soon` / `too_far` (`Min`/`MaxAdvanceDays`, counted in **Asia/Colombo** dates)
- `headcount_out_of_range`
- `fulfilment_not_offered`

It is unit-tested in Domain. The handler calls it **inside the lock**, after reading fresh data.

### C5: Status transitions live on the aggregate

| From | To | Who | Rule |
|---|---|---|---|
| Requested | Approved / Rejected (reason) | institution, `APPROVE` / `REJECT` | — |
| Requested / Approved | Cancelled (reason) | donor (own) or institution `EDIT` | donor: until the day before the slot (Q1) |
| Approved | Completed / NoShow | institution `EDIT` | only on or after the slot date (Colombo) |

Rejected and Cancelled release the place (C1). Completed and NoShow keep it. Every transition writes
`BookingStatusHistory` and an outbox message. Concurrent decisions on one booking are caught by
`RowVersion` and answered with 409.

### C6: The donor is the signed-in user

`DonorUserId` is the current user. `DonorName` and `DonorPhone` are snapshots from the account.
`InstitutionDonorId` stays NULL (the CRM is cut). `UX_Bookings_DonorPerSlot` turns a second active
booking by the same phone on the same slot-day into a 2601, answered as 409 `already_booked`.
Booking on behalf of someone else (`Source = 'Staff'`) is cut (§4).

### C7: `BookingNo` is platform-wide per year

`core.NumberSeries` code `BOOKING`, platform scope, giving `BK-2026-000123`. It is drawn inside the
booking transaction, so a rolled-back booking leaves a gap. That is acceptable and is the same as
B5.

### C8: RLS stays as designed, with one stated exposure

`Bookings` is already right (the tenant and the donor). `SlotInstances` has **no** RLS on purpose:
a donor's connection must update another tenant's counters, and availability is public. The
consequence is that any signed-in connection *could* change any counter. The only code that does
so is the booking handler, and its effect is bounded by the CHECKs. Record this in the report as a
known, deliberate trade-off.

**This also answers Phase E's open question** about how a donor reads their own rows: the same
`fn_TenantOrOwner` pattern.

### C9: Meal slots get a public id

`booking.MealSlots.PublicId UNIQUEIDENTIFIER NOT NULL` (v7, generated in C# like A9), with a unique
index. This is a **schema change**. URLs and APIs never carry `Id` (CLAUDE.md), and a slot is
addressed from the donor's side. Seasons and blocked dates are reached through their slot or by
their own `Id` inside the institution's API. They are tenant-internal, and the rule is about public
ids.

### C10: Discovery is minimal: a link plus a plain list

Phase B (search, Redis GEO, map) is not in this plan. A donor reaches an institution in two ways:
- **`/i/{slug}`**, a link the institution shares (its slots and a month of availability), and
- **`/institutions`**, a plain list of `Active` and `IsPubliclyListed` institutions (name, type,
  district). No search, no map.

An Owner turns `IsPubliclyListed` on from the slots screen ("Show us in the list"), once at least
one slot exists. Both GET endpoints are **anonymous** (public data, like `/api/legal/current`) and
rate-limited. Booking needs sign-in.

### C11: Notifications

SMS to the donor through the outbox, in their language, with `DedupKey`, when a booking is:
- received (`BOOKING_REQUESTED`), or `BOOKING_CONFIRMED` if no approval is needed,
- approved, rejected, or cancelled by the institution,
- and as a **reminder at 18:00 Colombo the day before** (Hangfire daily job; key
  `booking-reminder:{BookingNo}`).

The institution gets no SMS (cost). Its calendar shows the waiting requests (Q4). New templates are
seeded in en/si/ta, with the si/ta drafts added to `docs/translations-to-review.md`.

### C12: Calendar UI

FullCalendar, already named in CLAUDE.md's stack, so this is not a new technology. Use the MIT
packages only (`@fullcalendar/react`, `daygrid`, `interaction`). Resource and timeline views are a
paid plugin and are not used. The institution gets a month view of slot-days and their bookings.
The donor gets a month grid of availability for one institution.

---

## 3. Open questions (default applies if unanswered)

| # | Question | Default |
|---|---|---|
| Q1 | Until when may a donor cancel? | Until the end of the day before the slot date (Colombo). After that, only the institution can. |
| Q2 | Does a fresh institution appear in `/institutions`? | No. `IsPubliclyListed` stays 0 until the Owner turns it on (C10). |
| Q3 | Reminder time | 18:00 Colombo, the day before. |
| Q4 | SMS to the institution on a new request? | No (cost). The calendar's "waiting" count is the signal. |

---

## 4. Scope, and the cut order

**In:**
- meal slot create / edit / deactivate
- blocked dates (add, remove)
- the booking engine (C1–C3)
- donor: directory, institution page with availability, book, my bookings, cancel
- institution: calendar, approve / reject / complete / no-show / cancel
- notifications and the day-before reminder
- en/si/ta screens, desktop and 320 px

**Cut, and the report says so:**
- waitlist
- co-sponsors (`BookingContributors`)
- recurring bookings and the Memorial Dana Planner (Phase C)
- staff booking on behalf of a donor
- the donor CRM (`InstitutionDonors`)
- seasons UI (the rule is enforced, but there is no screen to create seasons)
- caterer fulfilment
- SignalR live updates (Phase C)
- availability alerts and search
- search, GEO and map (Phase B)

**If time runs out, cut in this order:**
1. the `/institutions` list (keep `/i/{slug}`)
2. Completed / NoShow
3. the institution's month calendar (fall back to a list per day)

Never cut the concurrency tests.

| Slice | Effort |
|---|---|
| 5.1 schema, seeds, catalogue | ~0.5 day |
| 5.2 domain rules and the aggregate | ~0.5 day |
| 5.3 the engine: lock, book, release; concurrency tests | ~1.5 days |
| 5.4 institution API (slots, blocked dates, calendar, decisions) | ~1 day |
| 5.5 donor API (directory, availability, book, mine, cancel) | ~0.75 day |
| 5.6 notifications and reminder job | ~0.5 day |
| 5.7 screens and browser tests | ~1.5 days |

**About 6.25 days.** Order: 5.1 → 5.2 → **5.3 first and fully tested** (the report's centrepiece)
→ 5.5 → 5.4 → 5.6 → 5.7.

---

## 5. Work breakdown

### 5.1: Schema, seeds and catalogue

- [x] `MealSlots.PublicId` + `UX_MealSlots_PublicId` (C9). EF mapping. The column is last in the
  table, not after `Id` (§8, E5).
- [x] `10_RbacCatalogue.sql`, INSTITUTION portal:
  - screen `BOOKINGS.SLOTS` (`/bookings/slots`): VIEW, CREATE, EDIT
  - `BOOKINGS.CALENDAR`: add REJECT and EDIT
  - dependencies: every action requires VIEW, and CALENDAR.* requires SLOTS.VIEW
  - grants: OWNER and MANAGER get all; STAFF keeps CALENDAR.VIEW and gets SLOTS.VIEW
- [x] DONOR portal: module `BOOK`, screen `MYBOOKINGS.BOOKINGS` (`/bookings/mine`) with VIEW, CREATE
  and EDIT (cancel own), scope `Self`, granted the way `APPLY` is. Extend `RbacCatalogueTests`
  (ceilings) for the new rows.
- [x] Number series `BOOKING` (`BK`, pad 6) in `NumberSeriesDefinition`.
- [x] `11_NotifyTemplates.sql`: `BOOKING_REQUESTED`, `_CONFIRMED`, `_APPROVED`, `_REJECTED`,
  `_CANCELLED`, `_REMINDER` × en/si/ta. Add si/ta to `translations-to-review.md`.
- [x] Publish. A second `SqlPackage /Action:Script` shows seed MERGEs only (D10): 0 schema
  statements, 23 MERGEs, on 2026-10-23.

### 5.2: Domain

- [x] `Domain/Bookings/MealSlot`, `SlotInstance`, `Booking` (aggregate with the C5 transitions),
  `BlockedDate`, `MealSlotSeason`. EF configurations matching the tables, `RowVersion`.
  `EfMappingSchemaTests` checks every mapped column against the published schema (§8, E4).
- [x] `MealSlotRules.CanBook` (C4). Unit tests cover every reason. The day-of-week mask is tested on
  a Colombo date that is a different UTC date.
- [x] Transition tests: each legal move, each illegal one refused, and only Rejected and Cancelled
  release.

### 5.3: The engine (the centrepiece)

- [x] `ISlotLocks` (Application) with `RedisSlotLocks` (Infrastructure): SET NX PX plus a token, and
  Lua release. On a Redis failure it reports "no lock" and the caller proceeds (C2).
- [x] `BookMealHandler`, in this order:
  1. lock
  2. one transaction: read the slot, its seasons and blocks fresh → `CanBook` → get-or-create the
     instance (C3) → conditional UPDATE (C2) → `BookingNo` → insert booking + history + outbox
  3. commit, then release the lock

  Map 547 (CHECK), 2601 (`UX_Bookings_DonorPerSlot`) and 2627 to 409s with codes.
- [x] Release on reject and cancel (`BookingDecisionHandler`, after the booking's save: §8, E7): `BookedSponsors - 1`, `BookedHeadcount - h`,
  `Status = 'Open'`, in the decision's transaction.
- [x] **Concurrency tests, real SQL and Redis, race forced, not hoped for.** `BookingEngineTests`. Use the
  verification-habits pattern: hold `UPDLOCK` on the instance from another connection, wait for N
  waiters in `sys.dm_tran_locks`, then release.
  - Exclusive slot, 20 parallel bookings: exactly 1.
  - Shared slot with `MaxSponsors = 4`, 10 parallel: exactly 4.
  - Headcount cap reached before the sponsor cap.
  - **The same tests with the Redis lock bypassed** (C2): still exactly 1 and 4.
  - Two donors and the first slot-day insert: one instance row (C3).
  - Cancel, then book again: the place is reused.
- [x] **Mutations, expected results written down first.** Results in the 5.3 commit message.
  - Removing the `WHERE` cap alone is expected to *survive*, because the CHECK still refuses
    (C2 layer 3), and the commit says so.
  - Removing both the `WHERE` cap and the 547 mapping must fail.
  - Also mutate: removing the release, `Status` not set to `Full`, the get-or-create without the
    2627 retry, and the lock token check in release.

### 5.4: Institution API (`/api/institution/...`, the active institution from the session)

- [ ] `meal-slots`: GET list, POST, PUT `{publicId}` (RowVersion), POST `{publicId}/deactivate`.
- [ ] `blocked-dates`: GET, POST, DELETE `{id}`. A block over existing bookings is allowed but
  returns their `BookingNo`s, so the screen can warn ("3 bookings fall on these days").
- [ ] `bookings?from=&to=&status=`: Dapper, per slot-day with counts and bookings.
  `POST bookings/{bookingNo}/approve | reject | complete | no-show | cancel`.
- [ ] `PUT listing` (`IsPubliclyListed`), Owner only, through `BOOKINGS.SLOTS.EDIT`. Bump nothing:
  it is not a permission.
- [ ] Tests over HTTP: every endpoint's permission (STAFF refused writes), RLS (institution A never
  sees B's bookings), RowVersion 409, release on reject/cancel.

### 5.5: Donor API

- [ ] `GET /api/institutions` (anonymous, listed + Active only).
- [ ] `GET /api/institutions/{slug}` (anonymous): profile and active slots, by `PublicId`.
- [ ] `GET /api/institutions/{slug}/availability?month=2026-11` (anonymous). Dapper. For each slot
  and day: bookable or the reason, and remaining sponsors and headcount. Computed from slots ×
  dates, `CanBook`'s data and instance counters.
- [ ] `POST /api/bookings` (`MYBOOKINGS.BOOKINGS.CREATE`), `GET /api/bookings/mine`,
  `POST /api/bookings/{bookingNo}/cancel`.
- [ ] Tests: anonymous reads work and show nothing private; an unlisted institution is absent from
  the list but reachable by slug; each `CanBook` refusal over HTTP; `already_booked`; a donor sees
  only their own bookings (RLS); cancel window (Q1).

### 5.6: Notifications and reminders

- [ ] Outbox handlers for the six events (C11), the donor's language, `DedupKey`.
- [ ] `BookingReminderJob`, a Hangfire recurring job at 18:00 Colombo, running as the platform. It
  covers tomorrow's Approved bookings. A re-run texts nobody twice.
- [ ] Tests like `OnboardingSmsTests`: each event's template in each language, dedup, and the
  reminder for tomorrow only.

### 5.7: Screens (en/si/ta, desktop + 320 px)

- [ ] `npm i @fullcalendar/react @fullcalendar/daygrid @fullcalendar/interaction` (C12).
- [ ] Institution:
  - `/bookings/slots`: list and form (mode, capacity, days, advance window, approval, fulfilment)
    and the listing toggle
  - blocked dates
  - `/bookings/calendar`: month view, a day panel with its bookings and the decision buttons
    (reject needs a reason)
- [ ] Donor:
  - `/institutions` list
  - `/i/[slug]`: profile and month availability; picking a slot-day opens the booking form
    (headcount, fulfilment, occasion, in memory of, notes)
  - `/bookings/mine`: status and cancel
- [ ] Browser tests (`e2e/booking`):
  - an Owner creates a slot, a donor books it, the Owner approves, and the donor sees it approved
  - an exclusive slot shows "Full" to a second donor
  - a reject with a reason, read by the donor
  - the donor cancels and the day is free again
  - a blocked date is not bookable
  - Sinhala and Tamil at 320 px, nothing clipped

---

## 6. Definition of done

1. `dotnet build Sethika.sln` clean, and `dotnet format --verify-no-changes` clean.
2. `dotnet test Sethika.sln` green. Every new test was seen to fail under a mutation, and the commit
   says which (and which mutation was *expected* to survive, C2).
3. `cd web && npm run lint && npm test && npm run build` clean. `npm run e2e` green (Console SMS).
4. A second `SqlPackage /Action:Script` shows seed MERGEs only.
5. A live walk-through: an Owner creates a slot and shares `/i/{slug}`. A donor books it and gets the
   SMS. The Owner approves. A second donor sees "Full". The donor cancels, and the day is open again.
6. `docs/DECISIONS.md` records C1–C12 in summary, the §4 cut list, and the C8 exposure.
7. PR into `develop`: Conventional Commits title, `## What` / `## Why` / `## How to verify`.

## 7. What this plan deliberately does not do

Everything in §4's cut list. Also Phase B (search, Redis GEO, map), Phase C (recurring bookings,
Memorial Dana Planner, co-sponsors, SignalR), Phase D (donations, needs board, reports) and Phase E
(religious services; `docs/plans/PHASE-E-RELIGIOUS-SERVICES.md`, built after this). The tables for
most of them already exist, and none of them blocks this plan.

## 8. Decided while building (2026-10-23)

| # | Decision | Why |
|---|---|---|
| E1 | `BOOKINGS.CALENDAR.APPROVE` stops being `RequiresMfa`, **in 5.4**, not 5.1 (asked and agreed 2026-10-23) | Approving a donor's request is the day's work; a step-up costs an SMS every 15 minutes. It drops when 5.4 replaces plan 01's demonstration endpoints (`/api/bookings/calendar`, `/api/bookings/{no}/approve`), and the step-up tests move off it then: they test the mechanism, so they can mark a permission `RequiresMfa` themselves and put it back. |
| E2 | The SMS opening line is `{bookingNo}: {institution}, {date} {time}`; the slot's name is not sent | The institution types slot names freely, and `IUserTextMessages` never sends typed text from the Sethika sender id: a "slot" called "Pay at bit.ly/..." would read as Sethika's. The institution's name was reviewed at onboarding. |
| E3 | The booking form has no occasion | `core.LookupValues` has no `OCCASION` values, and inventing them is what CLAUDE.md forbids. "In memory of" and notes stay. |
| E4 | `EfMappingSchemaTests`: every mapped column exists, with the type its configuration names | No migrations means nothing else notices a mapping that drifted from the SQL project. It covers every module, not only bookings. |
| E5 | `PublicId` is the last column of `MealSlots` | A column added mid-table makes DacFx rebuild the table, which it refuses on a table under RLS (SR0111). Even appended, the change drops and recreates `sec.TenantIsolationPolicy` for the deploy (it is schema-bound), so RLS is off for those seconds: say so in the report, and deploy schema changes outside opening hours. |
| E6 | The slot-day lock waits (up to 5 s, retrying every 20 ms) before it answers `slot_busy` | Answering 409 at once would hand a 4-place shared slot to fewer than 4 of 10 simultaneous donors. The wait keeps the lock an optimisation: the queue forms in Redis instead of on the row. |
| E7 | A decision saves the booking before it releases the place | The save's `RowVersion` check is what tells the second of two simultaneous cancellations that it lost. Released first, the second would take the place back twice and hit `CK_SlotInst_Sponsors`. |
| E8 | An institution's decision also checks `booking.InstitutionId` is the institution the caller acts for | RLS's owner branch shows a staff member their own donor bookings at *other* institutions; with only RLS, they could approve their own booking somewhere else. |
| E9 | Day mask: one bit per `DayOfWeek`, Sunday 1 to Saturday 64 | .NET's own numbering, so no table maps between them. Written in the column's comment. |
| E10 | A same-day slot (`MinAdvanceDays = 0`) closes when the meal starts, in Colombo time | Otherwise a donor could book a lunch at 15:00 that day. |
| E11 | C7 is corrected: a rolled-back booking does **not** leave a gap | `SqlNumberSeries` holds the series row until the transaction ends, so a rollback hands the number back. The cost is that every booking on the platform takes its number one at a time, for the few milliseconds between the number and the commit. At this scale that is fine; say so in the report. |
| E12 | The domain namespace is `Sethika.Domain.Bookings`, and `Application/Booking/` became `Application/Bookings/` | A type `Booking` inside a namespace `...Booking` makes every reference to it bind to the namespace instead. |
| E13 | `booking.Bookings.DonorPhone` joins the columns a deployed API's login needs `UNMASK` on (`docs/LOCAL_SETUP.md`) | A masked snapshot would make `UX_Bookings_DonorPerSlot` compare `+947XXXXX567` with itself. |
