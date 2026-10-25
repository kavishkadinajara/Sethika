# Plan E — Religious services and leader coordination

Branch: `phase-e-religious-services`, cut on 2026-10-25 from `develop` at 59b10ab (plan 03 merged,
PR #10).

Status: **started 2026-10-25; §8 boxes show what is built.** Added to scope on 2026-10-23 after the
supervisor's feedback on the proposal: *"add a few more functions to the scope, like coordination of
religious leaders and making appointments"*. Proposal v3 (`docs/proposal/Sethika_Project_Proposal.docx`)
promises it as **Phase E**. It reuses the booking engine's patterns (Redis lock, a write the database
refuses when it would be wrong, outbox, number series).

Deadline is about **2026-11-06** and the report needs the last week, so this plan aims at
**~4 working days (2026-10-25 → 10-29)**. Plan 03 was estimated at 6.25 days and took three; this
one is estimated the same way. If it runs over, the cut order is in §8.
Last updated 2026-10-25. Update this line and the §8 boxes whenever either changes. A fresh session
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

- [ ] **8.1 Schema** (§3) + RLS predicates (V9); `dotnet build` of the SQL project; publish to
      `Sethika` and `Sethika_Test`; a second `/Action:Script` shows seed MERGEs only (D10).
      EF mappings for the new tables and `EfMappingSchemaTests` green. ~0.5 day
- [ ] **8.2 Seeds**: module, screens, permissions, dependencies, ceilings, `LEADER` template role;
      `APPOINTMENT` number series; `InstitutionTypeDefaultModules`; text templates en/si/ta (si/ta
      drafts into `docs/translations-to-review.md`). ~0.25 day
- [ ] **8.3 Domain**: `TimeBlocks.For` (alignment, buffers, the midnight crossing, a week of leave);
      `ServiceRules.CanRequest`; the `ServiceAppointment` aggregate's transitions and the
      donor-cancel window. Unit tests + mutations. ~0.5 day
- [ ] **8.4 Roster and types API**: leaders (list, create, edit, deactivate, weekly hours), time off
      (add with clash 409, remove), service types (list, create, edit, deactivate); Dapper lists, EF
      commands. ~0.5 day
- [ ] **8.5 Donor**: public service types on `/api/institutions/{slug}`; request; my appointments;
      cancel. ~0.5 day
- [ ] **8.6 Assign engine**: `ILeaderLocks` on the extracted Redis lease; assign / remove; the
      picker query (free + normally available). **Concurrency tests against real SQL Server and
      Redis, race forced with `UPDLOCK`** (the plan 03 pattern): two parallel assignments of one
      leader to overlapping times, exactly one succeeds; again with the lock bypassed, the PK still
      stops it; two admins assigning `{A,B}` and `{B,A}` do not deadlock. Mutations: drop the PK ⇒
      must fail; drop the 2627 mapping; lock order reversed. ~0.75 day
- [ ] **8.7 Respond, reject, cancel, complete**, time-off clashes. ~0.5 day
- [ ] **8.8 Texts and the reminder job** with `DedupKey` (V14, V15). ~0.25 day
- [ ] **8.9 Screens**: `/services/types`, `/services/leaders`, `/services/appointments` (agenda +
      assign sheet), `/services/schedule`, the request sheet on `/i/[slug]`, `/services/mine`.
      Playwright, desktop + 320 px, si/ta, `clipsSideways`. ~1 day
- [ ] **8.10 Done**: §9, `docs/DECISIONS.md` (V1-V15 summary, the cut list), the PR.

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

(empty)
