# Plan E — Religious services and leader coordination

Branch: `phase-e-religious-services`, cut on 2026-10-25 from `develop` at 59b10ab (plan 03 merged,
PR #10).

Status: **built 2026-10-25 → 2026-11-01; all of §8 done, the PR into `develop` is the last step.** Added to scope on 2026-10-23 after the
supervisor's feedback on the proposal: *"add a few more functions to the scope, like coordination of
religious leaders and making appointments"*. Proposal v3 (`docs/proposal/Sethika_Project_Proposal.docx`)
promises it as **Phase E**. It reuses the booking engine's patterns (Redis lock, a write the database
refuses when it would be wrong, outbox, number series).

Deadline is about **2026-11-06** and the report needs the last week, so this plan aims at
**~4 working days (2026-10-25 → 10-29)**. Plan 03 was estimated at 6.25 days and took three; this
one is estimated the same way. If it runs over, the cut order is in §8.
Last updated 2026-11-01 (8.1-8.10 built; 8.5-8.10 on branch `phase-e-donor`, cut from `develop` at 3c74dc1; full `dotnet test` 973/973; e2e 87/92, the 5 shell failures fixed and that spec rerun 22/22). Update this line and the §8 boxes whenever either changes. A fresh session
trusts them and will rebuild anything they claim is missing.

The mobile app's F.3/F.4 (`docs/plans/PHASE-F-MOBILE.md`) are being built at the same time on the
branch `mobile-booking`, inside `mobile/` only. This plan does not touch `mobile/`.

Decision numbers here are **V1, V2, …** (serVices). Plan 03 already uses E1-E35 for its own
build-time decisions, and this file's first draft called its decisions E1-E8; V1-V8 are those same
eight.

---

## 0. How to resume this from a cold start

```bash
git checkout phase-e-religious-services
cat docs/plans/PHASE-E-RELIGIOUS-SERVICES.md    # this file
cat CLAUDE.md docs/DECISIONS.md                  # the rules this plan must obey
dotnet build Sethika.sln -m:1 -nodeReuse:false && dotnet test Sethika.sln   # ~35 min; 774 must pass
```

The rules from plans 01-03 still hold:
- `src/Sethika.Database` is the only place schema changes are made. A new column goes **last** in
  its table: a mid-table column makes the deploy rebuild the table, which RLS does not allow.
- Every endpoint carries `[HasPermission]`, or is on `EndpointPolicyTests`' reviewed anonymous list.
- Permission decisions are never re-derived in C#.
- Anything the database enforces is tested against real SQL Server and Redis, never a fake.
- Every test is shown to fail under a mutation before it counts. Expected results are written first.
- Screens are tested with the `browser-test` skill, desktop and 320 px, en/si/ta.

**Before any browser test:** `dotnet user-secrets list --project src/Sethika.Api`. If
`Sms:Provider = TextLk` is set, remove it, or every test signup sends a paid SMS.

Then read §1-§4 and start at the first unchecked box in §8.

---

## 0a. What the proposal promises (the report must match this list)

1. An institution publishes the **religious services** it offers (pirith, sermon, pooja, house
   blessing, funeral rites, memorial prayers…). Each one has a usual duration, the number of leaders it
   needs, and whether it happens at the institution or at the donor's home.
2. A donor **requests** a service for a date, time and place. The workflow is Requested → Approved →
   Completed / Cancelled (plus Rejected).
3. Each institution keeps a **roster of its religious leaders**, with their regular availability and
   their leave.
4. An administrator **assigns leaders who are free**. Each leader is notified and **accepts or
   declines** from a personal schedule. A decline returns the request for reassignment.
5. **Reminders** go to the donor and to the assigned leaders.
6. **A leader can never be committed to two places at once.** A Redis lock serialises assignments,
   and a database constraint makes the overlap impossible even if the lock is bypassed.

Anything not on this list is out of scope (§6). If the build drops an item, change the report, not
just the code.

---

## 1. What it reuses (none of Phase E's tables exist yet)

| Existing piece | How Phase E uses it |
|---|---|
| RBAC catalogue (`10_RbacCatalogue.sql`), `vw_EffectivePermissions`, `[HasPermission]` | New INSTITUTION module `SERVICES` and DONOR module `REQUEST` with their screens and permissions (§5) |
| `org.InstitutionTypeDefaultModules` (no rows yet) + `SqlInstitutionModules` | Its first rows: `SERVICES` on by default for the four `Religious` types, off for care homes (V12) |
| Template roles cloned on approval (`08`/`10` seeds) | New template role `LEADER` at the STAFF level: personal schedule only |
| RLS `sec.fn_Tenant`, `sec.fn_TenantOrOwner` + composite tenant FKs | Every new table carries `InstitutionId` (V9) |
| `INumberSeries` (`core.NumberSeries`, `UPDLOCK`) | `AppointmentNo` like `APT-2026-000123`, platform-wide per year like `BookingNo` (C7) |
| Outbox + Hangfire + `DedupKey` + `notify.Templates` (en/si/ta) | Request, approval, assignment, decline, cancellation and reminder texts |
| `RedisSlotLocks` (SET NX PX + token, Lua compare-and-delete, degrades on outage) | The same mechanism for `lock:leader:{leaderId}` (V3). Extract the shared part instead of copying it |
| Plan 03's `MealSlotRules` style | `ServiceRules` and `TimeBlocks`: pure functions, unit-tested in Domain |
| `web/components/bookings/*` (`BowlCalendar`, sheets, `useDates`, `useWide`, `BookingStatusBadge`) | The donor's date picker and the screens' building blocks |

---

## 2. Decisions

| # | Decision | Why |
|---|----------|-----|
| V1 | A leader is a person record, `org.ReligiousLeaders`, with an **optional** `UserId` | Many leaders (elderly monks, visiting clergy) will never log in. An administrator can answer on their behalf (`RespondedOnBehalf = 1`). Leaders who do log in get the `LEADER` role in that institution. |
| V2 | **Overlap is enforced by 15-minute time blocks.** `booking.LeaderTimeBlocks` has the primary key `(LeaderId, BlockStart)`. Every assignment and every leave period writes its blocks in the same transaction, so an overlap is a PK violation (2627), answered as "leader not free". | "No two intervals overlap" cannot be written as a constraint, but "no two rows share a key" can. This keeps the project's rule that **the database makes an invalid commitment impossible**. Rejected: a `SERIALIZABLE` / `UPDLOCK, HOLDLOCK` range check (correct, but the guarantee lives in C#, so one missing hint breaks it); a trigger (hidden logic, against D3). Cost: times are aligned to 15 minutes; a 3-hour pirith is 12 rows, a week of leave 672. |
| V3 | Redis lock `lock:leader:{leaderId}`. Several leaders at once: locks taken in **ascending `LeaderId`** order. On a Redis outage the assignment goes on without the lock, as in C2. | The first layer, mirroring `lock:slot:`. The fixed order means two admins assigning overlapping sets cannot deadlock. The PK is the guarantee, so an outage degrades rather than denies. |
| V4 | Service types are institution-owned rows in `booking.ServiceTypes`. **No seeded suggestions** (the first draft had per-faith `DRAFT` suggestions). | Which services each faith offers, how long they take and how many leaders they need are facts about real institutions that nobody has confirmed (CLAUDE.md: never invent data). An institution types its own; that is also all promise 1 asks for. One less open item. |
| V5 | `ServiceTypes.BufferMinutes` (multiple of 15) is added before and after an **at-home** service | Travel time. The blocks cover `[StartsAt − buffer, EndsAt + buffer)`, so back-to-back home visits across town are refused. |
| V6 | Appointment `Status`: `Requested`, `Approved`, `Rejected`, `Cancelled`, `Completed`. Assignment `Response`: `Pending`, `Accepted`, `Declined`, `Removed`. "Needs leaders" is **computed** (live assignments < `LeadersRequired`), not stored. | No status that can disagree with its own rows. |
| V7 | Times are stored in UTC (`DATETIME2(3)`) and shown in Asia/Colombo | Sri Lanka is UTC+05:30, so 15-minute alignment holds in both, and the alignment CHECK can be written on the UTC value. |
| V8 | One leader belongs to one institution | Shared leaders would need cross-tenant blocks under RLS. Out of scope (§6). |
| V9 | **RLS:** `ServiceAppointments` uses `fn_TenantOrOwner(InstitutionId, DonorUserId)`, FILTER + BLOCK after INSERT and UPDATE, exactly like `Bookings`. Leaders, weekly hours, time off, assignments, blocks and history use `fn_Tenant`. `ServiceTypes`: public read, tenant-only write, like `MealSlots`. | Answers the first draft's open question the way plan 03 did (C8 note): the donor reads their own appointments through the owner branch. A leader who logs in works in their institution's session, so `fn_Tenant` already lets them see their own rows; the query narrows to them, the permission `SERVICES.MYSCHEDULE.*` gates the endpoint. |
| V10 | The institution's appointments screen is **a day-by-day agenda with filters** (waiting, needs leaders, upcoming), not a time grid. The donor picks a day on the `BowlCalendar` and a start time from a 15-minute list. | FullCalendar was dropped in plan 03 (E32); its resource/timeline views were paid anyway. An agenda answers the admin's real question, "what needs me", and works at 320 px. |
| V11 | The donor chooses a date and a start time; `EndsAt = StartsAt + DurationMinutes`. Start times are offered on the 15-minute grid between 05:00 and 21:00 Colombo. | The block expansion needs aligned times; offering only aligned times means a donor never meets the CHECK. The window is a UI default, not a rule in the database. |
| V12 | Module `SERVICES` is **not core**. It is enabled at approval through `org.InstitutionTypeDefaultModules` for the four `Religious` types. Institutions approved before Phase E do not get it (development data only; there is no production yet). | Care homes have no religious leaders. The platform-admin "enable a module" action is cut (§6). |
| V13 | A donor may cancel **until the end of the day before** (Colombo); after that only the institution can | The same rule as meals (plan 03 Q1), so donors learn one rule. |
| V14 | A leader is texted on assignment and reminded the day before, if the leader has a phone. Language: the leader's account language if they have one, else `ReligiousLeaders.Language` (default `si`). | Promise 4 and 5. A leader without an account still has to hear about it. |
| V15 | Reminders run at **18:00 Colombo the day before**, the same daily Hangfire job pattern as meals, `DedupKey` `appt-reminder:{AppointmentNo}:{recipient}` | One reminder rule for the whole product; the first draft's "24 hours before" would need a job every 15 minutes for no visible gain. |
| V16 | When the **donor** cancels, the appointment's leader blocks are released by the outbox handler (platform session), not in the donor's transaction. When the institution cancels or rejects, they go in its own transaction. | The donor works in a personal session, and RLS (V9) hides leaders, assignments and blocks from it, so its DELETE would match nothing. Opening RLS for donors would expose the roster. Until the outbox runs (seconds), the leader looks busy: a stale block can only refuse an assignment, never allow a double one, so the delay errs on the safe side. The handler's delete is idempotent. |

---

## 3. Schema (add to `src/Sethika.Database`; D10: named CHECKs, no `IN` / `BETWEEN` in CHECKs)

| Table | Key columns and rules |
|---|---|
| `org.ReligiousLeaders` | `Id INT`, `InstitutionId`, `UserId NULL → auth.Users`, `DisplayName`, `NameSi`, `NameTa`, `Title NVARCHAR(40) NULL` (free text: Ven., Rev., Fr., Moulavi, Kurukkal…), `Phone VARCHAR(20) NULL` masked like `Bookings.DonorPhone` (D9), `Language CHAR(2) NOT NULL DEFAULT 'si'` (CHECK en/si/ta), `IsActive`, `CreatedAt/By`, `UpdatedAt/By`, `RowVersion`, `PublicId` (last). `UQ (Id, InstitutionId)`; filtered `UX (InstitutionId, UserId) WHERE UserId IS NOT NULL`; `UX PublicId`. |
| `org.LeaderWeeklyHours` | `Id`, `(LeaderId, InstitutionId) → ReligiousLeaders(Id, InstitutionId)`, `DayOfWeek TINYINT` (0 Sunday … 6), `StartTime`, `EndTime` (`TIME(0)`, CHECK end > start). Only a filter for the picker; **not** the guarantee. |
| `org.LeaderTimeOff` | `Id`, `(LeaderId, InstitutionId)` tenant FK, `StartsAt`, `EndsAt` (UTC, aligned, CHECK end > start), `Reason NVARCHAR(200) NULL`, `CreatedAt/By`. `UQ (Id, InstitutionId)`. Writes `TimeOff` blocks. |
| `booking.ServiceTypes` | `Id INT`, `InstitutionId NOT NULL`, `Name` / `NameSi` / `NameTa`, `Description NVARCHAR(300) NULL`, `DurationMinutes SMALLINT`, `BufferMinutes SMALLINT DEFAULT 0` (both multiples of 15, duration 15-720, buffer 0-180), `LeadersRequired TINYINT` (1-20), `LocationMode` (`AtInstitution` / `AtDonorPlace` / `Either`), `MinAdvanceDays`, `MaxAdvanceDays`, `IsActive`, audit, `RowVersion`, `PublicId` (last). `UQ (InstitutionId, Name)`, `UQ (Id, InstitutionId)`. |
| `booking.ServiceAppointments` | `Id BIGINT`, `AppointmentNo VARCHAR(20) UNIQUE`, `InstitutionId`, `(ServiceTypeId, InstitutionId)` tenant FK, `DonorUserId`, `DonorName`, `DonorPhone` (masked snapshot, as `Bookings`), `StartsAt`, `EndsAt`, `LocationMode` (`AtInstitution` / `AtDonorPlace`), `Address NVARCHAR(300) NULL`, `Occasion NVARCHAR(200) NULL`, `DonorNote NVARCHAR(500) NULL`, `Status`, `DecidedBy/At`, `RejectReason`, `CancelledBy/At`, `CancelReason`, `CompletedAt`, `CreatedAt/By`, `RowVersion`. CHECKs: `EndsAt > StartsAt`; both on a 15-minute boundary; at-home ⇒ `Address`; rejected ⇒ reason; cancelled ⇒ `CancelledAt`. `UQ (Id, InstitutionId)`. |
| `booking.AppointmentLeaders` | PK `(AppointmentId, LeaderId)`, `InstitutionId`, tenant FKs to both, `Response` (CHECK), `AssignedBy/At`, `RespondedBy/At NULL`, `RespondedOnBehalf BIT`, `DeclineReason NVARCHAR(300) NULL`. |
| `booking.LeaderTimeBlocks` | PK `(LeaderId, BlockStart)`, `InstitutionId`, tenant FK to the leader, `Kind` (`Appointment` / `TimeOff`), `AppointmentId BIGINT NULL`, `TimeOffId INT NULL` (tenant FKs). CHECKs: exactly the source column that matches `Kind` is set; `BlockStart` on a 15-minute boundary (`DATEPART(MINUTE) % 15 = 0`, seconds and ms 0). |
| `booking.AppointmentStatusHistory` | Same shape as `BookingStatusHistory`. |

Seeds: `core.NumberSeries` code `APPOINTMENT` (prefix `APT`), wherever `BOOKING` is seeded; the
module, screens, permissions, dependencies, ceilings and the `LEADER` template role in
`10_RbacCatalogue.sql`; `InstitutionTypeDefaultModules` rows; texts in `11_NotifyTemplates.sql`.

---

## 4. Flows (each one a single transaction plus outbox)

1. **Request** (donor, personal session). `ServiceRules.CanRequest`: type active, institution
   active, advance window (Colombo dates), 15-minute alignment, location mode (at home ⇒ address).
   Insert as `Requested`, history, outbox `AppointmentRequested`. **No blocks yet**: a request holds
   no leader.
2. **Assign** (`SERVICES.APPOINTMENTS.APPROVE`).
   1. Take `lock:leader:{id}` for each chosen leader, ascending, short PX. Busy ⇒ 409 `try_again`.
   2. Transaction: load the appointment at the client's `RowVersion`; for each leader insert an
      `AppointmentLeaders` row (`Pending`) and its blocks (`TimeBlocks.For(start, end, buffer)`).
      **2627 on `LeaderTimeBlocks` ⇒ 409 `leader_not_free`**, naming the leader. If the appointment
      was `Requested`, set `Approved` and write history. Outbox `LeaderAssigned` per leader and
      `AppointmentApproved` once.
   3. Release the locks (compare-and-delete).

   Removing a leader sets `Response = Removed` and deletes their blocks. Adding later uses the same
   path without the status change.
3. **Respond** (a leader for their own row on `SERVICES.MYSCHEDULE.EDIT`, or an admin on behalf via
   `SERVICES.APPOINTMENTS.EDIT`). Accept sets the response. Decline sets it (reason optional),
   deletes that leader's blocks, outbox `LeaderDeclined` (the institution sees it as "needs leaders").
4. **Reject** (`REJECT`, `Requested` only, reason required) / **cancel** (donor until the day before,
   V13; institution `EDIT` any time before it ends). Delete all blocks, history, notify the donor
   (unless they cancelled it themselves) and every live leader.
5. **Complete** (`EDIT`): only after `EndsAt`, checked in the aggregate (a CHECK cannot read the clock).
6. **Time off** (`SERVICES.LEADERS.EDIT`). Writes `TimeOff` blocks; a 2627 ⇒ 409 `time_off_clashes`
   with the clashing appointments, so the admin reassigns first. Removing time off deletes its blocks.
7. **Reminders** (V15): daily job at 18:00 Colombo, tomorrow's `Approved` appointments, to the donor
   and every `Pending`/`Accepted` leader with a phone.

---

## 5. Permissions (`10_RbacCatalogue.sql`)

INSTITUTION portal, module `SERVICES` (not core, V12):

| Screen | Route | Actions |
|---|---|---|
| `SERVICES.TYPES` | `/services/types` | VIEW, CREATE, EDIT |
| `SERVICES.LEADERS` (roster, weekly hours, time off) | `/services/leaders` | VIEW, CREATE, EDIT |
| `SERVICES.APPOINTMENTS` | `/services/appointments` | VIEW, APPROVE (assign + approve), REJECT, EDIT (reassign, answer on behalf, cancel, complete) |
| `SERVICES.MYSCHEDULE` | `/services/schedule` | VIEW, EDIT (accept / decline own) |

DONOR portal, module `REQUEST`: `MYSERVICES.APPOINTMENTS` (`/services/mine`) with VIEW, CREATE and
EDIT (cancel own), scope `Self`, granted to the global Donor role like `BOOK`.

OWNER and MANAGER get all of `SERVICES.*` except `MYSCHEDULE`. STAFF gets the three VIEWs. The new
template role `LEADER` (STAFF level) gets only `SERVICES.MYSCHEDULE.*`. Every action requires VIEW.
The STAFF ceiling gains `SERVICES.MYSCHEDULE.*`.

Public reads (anonymous, rate-limited, added to `EndpointPolicyTests`' list): an institution's
active service types, shown on `/i/{slug}` beside its meals.

---

## 6. Out of scope (the report says so)

- Cross-institution search for services, and showing them on the map
- Leaders shared by several institutions (V8)
- Assigning leaders to **meal bookings** (who attends a home dana). The natural next step.
- Any fee for a service (P4: Sethika never charges)
- Automatic assignment. The picker only filters by "free + normally available".
- Live SignalR updates of schedules
- A time-grid / resource calendar (V10)
- Seeded service-type suggestions per faith (V4)
- A platform-admin action to enable `SERVICES` for institutions approved before Phase E (V12)
- Inviting a leader to create an account from the roster: a leader with an account is linked by
  phone number to an existing user, and the Owner gives them the `LEADER` role on the existing
  access screens.

## 7. Open questions (default applies if unanswered)

| # | Question | Default |
|---|---|---|
| Q1 | Which services each faith offers, with durations and leader counts | Not seeded (V4); each institution types its own |
| Q2 | Donor's cancellation window | Until the end of the day before, Colombo (V13) |
| Q3 | `SERVICES` for institutions approved before Phase E | Not enabled; development data only (V12) |
| Q4 | Donor reads under RLS | Answered: V9 |

---

## 8. Work breakdown, effort and cut order

- [x] **8.1 Schema** (§3) + RLS predicates (V9); `dotnet build` of the SQL project; publish to
      `Sethika` and `Sethika_Test`; a second `/Action:Script` shows seed MERGEs only (D10).
      (EF mappings move to 8.3, with the entities they map.) ~0.5 day
- [x] **8.2 Seeds**: module, screens, permissions, dependencies, ceilings, `LEADER` template role;
      `InstitutionTypeDefaultModules`. (The `APPOINTMENT` series needs no seed: a series is defined
      in code, `NumberSeriesDefinition`, and its row made on first use. Text templates move to 8.8.)
      ~0.25 day
- [x] **8.3 Domain**: `TimeBlocks.For` (alignment, buffers, the midnight crossing, a week of leave);
      `ServiceRules.CanRequest`; the `ServiceAppointment` aggregate's transitions and the
      donor-cancel window. Unit tests + mutations. ~0.5 day
- [x] **8.4 Roster and types API**: leaders (list, create, edit, deactivate, weekly hours), time off
      (add with clash 409, remove), service types (list, create, edit, deactivate); Dapper lists, EF
      commands; the leader picker (`GET /api/institution/appointments/{no}/candidates`). ~0.5 day
- [x] **8.5 Donor**: public service types on `/api/institutions/{slug}`; request; my appointments;
      cancel. ~0.5 day. Built as: `services` on the existing page (V25, so no new anonymous
      endpoint); `POST /api/appointments`, `GET /api/appointments/mine`,
      `POST /api/appointments/{no}/cancel` on `MYSERVICES.APPOINTMENTS.*`; `SriLankaTime.ToUtc`
      (Colombo date + time to UTC); `APPOINTMENT` series (`APT`); `AppointmentCancelledHandler`
      releases the blocks (V16, V27). Tests: `DonorAppointmentEndpointsTests` (21, real SQL and
      Redis, the cancel race forced with `UPDLOCK`), `SriLankaTimeTests` (9).
- [x] **8.6 Assign engine**: `ILeaderLocks` on the extracted Redis lease; `AssignLeadersHandler`.
      (Built without an HTTP endpoint, and **remove is not built**: only the domain's
      `ServiceAppointment.Remove`. Both move to 8.7. The picker query was built in 8.4.) **Concurrency tests against real SQL Server and
      Redis, race forced with `UPDLOCK`** (the plan 03 pattern): two parallel assignments of one
      leader to overlapping times, exactly one succeeds; again with the lock bypassed, the PK still
      stops it; two admins assigning `{A,B}` and `{B,A}` do not deadlock. Mutations: drop the PK ⇒
      must fail; drop the 2627 mapping; lock order reversed. ~0.75 day
- [x] **8.7 Assign / remove endpoints** (remove = `Removed` + delete that leader's blocks), **respond,
      reject, cancel, complete**. (Time-off clashes were built in 8.4.) ~0.5 day. Built as
      `InstitutionAppointmentEndpoints`: `GET /api/institution/appointments?view=` (V32),
      `POST .../{no}/leaders` (assign), `DELETE .../{no}/leaders/{leader}`,
      `POST .../{no}/leaders/{leader}/respond` (on behalf), `.../reject`, `.../cancel`, `.../complete`;
      `GET /api/institution/schedule` and `POST /api/institution/schedule/{no}/respond` for a leader
      (V33). One change path in `AppointmentDecisionHandler` (V30, V31). Tests:
      `InstitutionAppointmentEndpointsTests` (15; the cancel/decline race forced with `UPDLOCK`).
- [x] **8.8 Texts and the reminder job** with `DedupKey` (V14, V15). ~0.25 day. Built as: 27 rows in
      `11_NotifyTemplates.sql` (9 templates x en/si/ta, in `docs/translations-to-review.md`);
      `AppointmentSmsHandler` and the texts in `AppointmentCancelledHandler` (V27); `AppointmentTexts`;
      `IUserTextMessages.SendToPhoneAsync` for leaders without an account; `AppointmentReminders` +
      `AppointmentReminderJob` at 18:00 Colombo (V35-V39). Tests: `AppointmentSmsTests` (9).
- [x] **8.9 Screens**: `/services/types`, `/services/leaders`, `/services/appointments` (agenda +
      assign sheet), `/services/schedule`, the request sheet on `/i/[slug]`, `/services/mine`.
      Playwright, desktop + 320 px, si/ta, `clipsSideways`. ~1 day. Built as: `ServicesOffered` +
      `ServiceRequestSheet` on the institution page (`?service=` opens it; the sign-in gate shared
      with the booking sheet as `DonorSheetGate`), `MyServices`, `ServiceTypesScreen`,
      `LeadersScreen` (roster, usual week, leave), `AppointmentsScreen` (the four views, assign
      sheet, reasons), `ScheduleScreen`; `web/lib/services` (API client, Colombo times, tested in
      Vitest); `Services` messages in en/si/ta (in `docs/translations-to-review.md`); sidebar items
      under Giving and the institution. Tests: `e2e/services/services.spec.ts` (5 scenarios, 3 also
      at 320 px; the si/ta screens and the request form checked for clipping).
- [x] **8.10 Done**: §9, `docs/DECISIONS.md` (V1-V15 summary, the cut list), the PR. Done as: full
      `dotnet test` 973/973 (227 + 71 + 675); `dotnet format --verify-no-changes`, `npm run lint`,
      `npm test` (196), `npm run build` clean; full e2e 87/92, the 5 failures the shell spec's donor
      menu counting 4 items before "My service requests" (fixed; shell spec 22/22); a second
      `SqlPackage /Action:Script` shows seed MERGEs only. `docs/DECISIONS.md` summarises V1-V39 with
      the cut list (§6; nothing on the cut order was cut).

About **4.75 days** estimated. Order: 8.1 → 8.2 → 8.3 → **8.6 first among the APIs** (the report's
second concurrency pattern) → 8.4 → 8.5 → 8.7 → 8.8 → 8.9.

**If time runs out, cut in this order** (and change the report):
1. Weekly hours (the picker shows only "free"; promise 3 keeps leave)
2. Leader texts (the donor keeps theirs; leaders see the schedule screen)
3. `/services/schedule` (admins answer on behalf only; promise 4 changes in the report)

Never cut the overlap guarantee or its concurrency tests.

## 9. Definition of done

1. `dotnet build Sethika.sln` clean, `dotnet format --verify-no-changes` clean.
2. `dotnet test Sethika.sln` green; every new test seen to fail under a mutation, listed in the commit.
3. `cd web && npm run lint && npm test && npm run build` clean; `npm run e2e` green (Console SMS).
4. A second `SqlPackage /Action:Script` shows seed MERGEs only.
5. `docs/DECISIONS.md` records V1-V15 in summary and the cut list.
6. PR into `develop`: Conventional Commits title, `## What` / `## Why` / `## How to verify`.
7. For the report: the new tables in the ERD, and the two concurrency patterns side by side
   (counted slot inventory vs interval overlap as time blocks).

## 10. Decided while building

| # | Decision | Why |
|---|----------|-----|
| V17 | Leave writes blocks **from the current 15-minute block on**, not from a start already past; the `LeaderTimeOff` row keeps the start as typed. | Leave that began yesterday is a normal entry. Past blocks hold nothing assignable, and would only clash with appointments already over. |
| V18 | One leave period is **at most 31 days** (2,976 blocks). Longer is several periods, or the leader taken off the roster. | Bounds one insert; a longer absence is better shown as "not on the roster". |
| V19 | Adding leave takes the leader's Redis lock, like an assignment. Leave over an appointment answers 409 `time_off_clashes` **naming the appointments**; over other leave, 409 `time_off_overlaps`. A 2627 without the lock is still `time_off_clashes` (unnamed). | The lock lets the clash be named before the insert; the key stays the guarantee. Two codes, because the admin's next step differs (reassign vs. nothing to do). |
| V20 | The picker shows, per active leader: assigned, busy (`TimeOff` before `Appointment`), and normally available, which is **`null` when no week is recorded** and is judged on the service's own time, **without travel**. Gated by `SERVICES.APPOINTMENTS.APPROVE`. | Weekly hours are cut item 1; an institution that never records them must not see everyone as "unavailable". Travel is about where the leader comes from, not when they work. |
| V21 | A leader account is linked by **phone number** on the roster form (`linkAccount`); none found is 409 `account_not_found`. | §6's linking rule. It tells vetted institution staff whether a number has an account; accepted, as staff invitations already work by phone. |
| V22 | Weekly hours are replaced whole (`PUT .../weekly-hours`), with no version: last write wins. Overlapping spans on one day are refused. | Only a filter (V2): a lost edit never lets a leader be double-booked. |
| V23 | Taking a leader off the roster names their upcoming live assignments and changes none. | The same rule as deactivating a meal slot (plan 03): the institution decides. |
| V24 | The test reseed (`RlsTestData.sql`) clears every Phase E table first. | A leader left by a failed test would otherwise block `DELETE FROM org.Institutions` for every later test. |
| V25 | An institution's active services ride on its public page, `GET /api/institutions/{slug}` (`services`, beside `slots`), not a separate endpoint. Only what the donor chooses by: name, description, duration, where, advance window. No buffer, no leader count. | `/i/[slug]` shows them beside its meals, so one read serves the page; no new anonymous endpoint to review. Buffer and leaders are the institution's planning. |
| V26 | With `SERVICES` switched off (or never on, V12), the page lists no services and a request is refused `service_inactive`, whatever the types say. Checked on `org.InstitutionModules`, which has no RLS. | Switching the module off must stop requests without retiring each type, and a care home never gets them. |
| V27 | **Every** `AppointmentCancelled` message releases the appointment's blocks (`AppointmentCancelledHandler`), not only the donor's; the handler refuses (throws, so it is retried and logged) an appointment that is not `Cancelled`. Texts for the event (8.8) go in the same handler, since the outbox allows one handler per event type. | One rule, and idempotent: after an institution's cancel (8.7, blocks deleted in its own transaction) the message finds nothing. The status check means a stray message can never free a live appointment's time. |
| V28 | A request sends the Colombo **date** and **start time** (`yyyy-MM-dd`, `HH:mm`); `SriLankaTime.ToUtc` makes the instant. Appointments come back in UTC, like leave (`TimeOffView`). | What a donor picks on the calendar; before 05:30 Colombo the UTC date is the day before, so the date cannot be carried over. |
| V29 | Requests are limited to **10 per donor per hour** (`appointment-create-user`). | Each takes a number from a platform-wide series and the institution's attention; a household asks for a few a year. |
| V30 | Remove, respond, reject, cancel and complete take **no leader lock**. | They only free time. A block deleted a moment early can only let through an assignment that is genuinely free; the key (V2) still guards every insert. |
| V31 | Every change writes the appointment's row, even when only a leader's row changes, and deletes the freed blocks in the **same transaction** after the save. Two changes at once: one wins, the other is 409 `stale`. | Two changes then always meet on `RowVersion` (as assign does), so a decline cannot slip past a cancellation, and a loser frees nothing. |
| V32 | The agenda has four fixed views: `upcoming` (default; requested or approved, not over, soonest first), `waiting`, `needs-leaders` (live leaders < required, not over), `history` (over or ended in a status, latest first). At most 200, no paging yet. Leaders removed are hidden; those who declined are listed after the live ones. Each view is its own SQL text, chosen in C#. | V10's "what needs me". Nothing the caller sends is written into SQL. |
| V33 | A leader's schedule: approved appointments not yet over where their answer is `Pending` or `Accepted`. Their own answer finds them by the account's roster row **in the session's institution**; none is 403 `not_a_leader`. A leader not on the roster, named by an admin, is 409 `not_assigned` like one not assigned. | What they still have to attend or answer. The roster link is V21's; the session's institution keeps one roster from answering for another. |
| V34 | New outbox events: `AppointmentRejected`, `LeaderDeclined`, `LeaderRemoved` (payload names the leader's internal id; texts in 8.8). Accepting and completing write none. | Each is someone to tell; an acceptance and a completion change nothing anyone waits for. |
| V35 | A leader is texted at the roster's phone, else at the linked account's **confirmed** phone; in the account's language, else the roster's. A leader with neither phone is skipped, not failed. | V14. An unconfirmed number may be someone else's. A missing phone is ordinary for a roster, so it must not hold the outbox. |
| V36 | Dedup keys: the donor's `{TEMPLATE}:{AppointmentNo}`; a leader's assignment or removal also carries the outbox message id; a cancellation `{TEMPLATE}:{AppointmentNo}:{leaderId}`; reminders `appt-reminder:{AppointmentNo}:donor` / `:leader-{id}` (V15). | Each donor move happens once. A leader can be assigned, decline and be assigned again, and must hear the second time too; a re-run of one message still texts nobody twice. |
| V37 | Who hears what: the donor of the request, approval, rejection, the institution's cancellation and the reminder; a leader of being assigned or removed, of a cancellation and of the reminder **while still expected** (`Pending`/`Accepted`). A decline, an acceptance and a completion text nobody. | The institution gets no texts, as with bookings: a decline shows on its agenda as "needs leaders". A leader who declined or was removed is no longer expected. |
| V38 | The Sinhala leader texts use no pronoun; the Tamil ones use `தாங்கள்`. Both are drafts for review. | A leader may be a monk, a priest, a kurukkal or a moulavi, each addressed differently; the text cannot know which. |
| V39 | The service reminder is its own Hangfire job (`appointment-reminders`) at the same 18:00 Colombo, not folded into the meal job. | Either can fail and retry without the other; the job list shows both. |
