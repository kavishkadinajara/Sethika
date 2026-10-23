# Plan E — Religious services and leader coordination

Status: **Designed, not started.** Added to scope on 2026-10-23 after the supervisor's feedback on
the proposal: *"add a few more functions to the scope, like coordination of religious leaders and
making appointments"*. Proposal v3 (`docs/proposal/Sethika_Project_Proposal.docx`) promises it as
**Phase E**. Build it **after the Phase A booking engine**, because it reuses that engine's patterns
(Redis lock, conditional write, outbox, number series). It gets a number and a branch when work
starts.
Last updated 2026-10-23. Update this line and the §8 boxes whenever either changes.

---

## 0. What the proposal promises (the report must match this list)

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

## 1. What it reuses

| Existing piece | How Phase E uses it |
|---|---|
| RBAC catalogue, `vw_EffectivePermissions`, `[HasPermission]` | New module `SERVICES` and its screens and permissions (§5) |
| Institution modules + `org.InstitutionTypeDefaultModules` | `SERVICES` is on by default for types with `Category = 'Religious'` and off for care homes |
| Template roles cloned on approval | New template role `LEADER` (personal schedule only) |
| RLS (`sec.TenantIsolationPolicy`) + composite tenant FKs | Every new table carries `InstitutionId` |
| `core.NumberSeries` (`UPDLOCK`) | `AppointmentNo` like `APT-2026-000123` |
| Outbox + Hangfire + `DedupKey` | Request, approval, assignment, decline and reminder messages |
| Booking engine's Redis lock helper | `lock:leader:{leaderId}`, the same SET NX PX plus compare-and-delete |

## 2. Decisions

| # | Decision | Why |
|---|----------|-----|
| E1 | A leader is a person record, `org.ReligiousLeaders`, with an **optional** `UserId` | Many leaders (elderly monks, visiting clergy) will never log in. An administrator can accept on their behalf (`RespondedOnBehalf = 1`). Leaders who do log in get the `LEADER` role. |
| E2 | **Overlap is enforced by 15-minute time blocks.** `booking.LeaderTimeBlocks` has the primary key `(LeaderId, BlockStart)`. Every assignment and every leave period writes its blocks in the same transaction, so an overlap becomes a PK violation (2627), which is reported as "leader not free". | "No two intervals overlap" cannot be written as a constraint, but "no two rows share a key" can. This keeps the project's rule that **the database makes an invalid commitment impossible**. Rejected alternatives: a `SERIALIZABLE` / `UPDLOCK, HOLDLOCK` range check (correct, but the guarantee lives in C#, so one missing hint breaks it); a trigger (hidden logic, against D3). Cost: times are aligned to 15 minutes. A 3-hour pirith is 12 rows and a week of leave is 672 rows. |
| E3 | Redis lock `lock:leader:{leaderId}`. When several leaders are assigned at once, the locks are taken in **ascending `LeaderId`** order. | It is the first layer, mirroring `lock:slot:`. The fixed order means two admins assigning overlapping sets can't deadlock each other. |
| E4 | Service types are institution-owned rows in `booking.ServiceTypes`. Draft **suggestions** per institution type (`InstitutionId NULL`, `InstitutionTypeId` set) are copied by the institution from its own screen, **not cloned at approval**. | It leaves plan 02's approval transaction untouched. The suggestions are facts about faiths, so they are seeded as visibly marked `DRAFT` rows (CLAUDE.md: never invent data). |
| E5 | `ServiceTypes.BufferMinutes` (multiple of 15) is added before and after an at-home service | Travel time. The blocks cover `[StartsAt − buffer, EndsAt + buffer)`, so back-to-back home visits across town are refused. |
| E6 | Appointment `Status`: `Requested`, `Approved`, `Rejected`, `Cancelled`, `Completed`. Assignment `Response`: `Pending`, `Accepted`, `Declined`, `Removed`. "Needs reassignment" is **computed** (live assignments < `LeadersRequired`), not stored. | It avoids a status that can disagree with its own rows. |
| E7 | Times are stored in UTC (`DATETIME2(3)`) and shown in Asia/Colombo | Sri Lanka is UTC+05:30, so 15-minute alignment holds in both. The CHECK can be written on the UTC value. |
| E8 | One leader belongs to one institution (`UQ` on the tenant FK) | Leaders shared across institutions would need cross-tenant blocks under RLS. That is out of scope (§6). |

## 3. Schema (add to `src/Sethika.Database`; D10 rules apply: named CHECKs, no `IN` / `BETWEEN`)

| Table | Key columns and rules |
|---|---|
| `org.ReligiousLeaders` | `Id`, `PublicId`, `InstitutionId`, `UserId NULL → auth.Users`, `DisplayName`, `NameSi`, `NameTa`, `Title NVARCHAR(40) NULL` (free text: Ven., Rev., Fr., Moulavi, Kurukkal…), `Phone` (masked, D9), `IsActive`, audit columns, `RowVersion`. `UQ (Id, InstitutionId)`; filtered `UX (InstitutionId, UserId) WHERE UserId IS NOT NULL`. |
| `org.LeaderWeeklyAvailability` | `(LeaderId, InstitutionId) → ReligiousLeaders`, `DayOfWeek`, `StartTime`, `EndTime`. Only a filter for the assignment picker; it is **not** the guarantee. |
| `org.LeaderTimeOff` | `Id`, `LeaderId`, `InstitutionId`, `StartsAt`, `EndsAt`, `Reason`. Writes `TimeOff` blocks. |
| `booking.ServiceTypes` | `Id`, `InstitutionId NULL`, `InstitutionTypeId NULL`, `Name` / `NameSi` / `NameTa`, `DurationMinutes`, `BufferMinutes` (both multiples of 15), `LeadersRequired TINYINT ≥ 1`, `LocationMode` (`AtInstitution` / `AtDonorPlace` / `Either`), `MinAdvanceDays`, `MaxAdvanceDays`, `IsActive`, `RowVersion`. CHECK: an owned row has `InstitutionId`, a suggestion has only `InstitutionTypeId`. `UQ (Id, InstitutionId)`. |
| `booking.ServiceAppointments` | `Id`, `PublicId`, `AppointmentNo`, `InstitutionId`, `(ServiceTypeId, InstitutionId)` composite FK, `DonorUserId`, `StartsAt`, `EndsAt`, `LocationMode` (`AtInstitution` / `AtDonorPlace`), `Address`, `Latitude` / `Longitude` (Sri Lanka CHECK as in `RegistrationRequests`), `Occasion`, `DonorNote`, `Status`, decision / cancel / complete fields, `RowVersion`. CHECKs: `EndsAt > StartsAt`, 15-minute alignment, at-home ⇒ `Address`, rejected ⇒ reason. |
| `booking.AppointmentLeaders` | PK `(AppointmentId, LeaderId)`, `InstitutionId`, `Response`, `AssignedBy` / `At`, `RespondedBy` / `At`, `RespondedOnBehalf`. |
| `booking.LeaderTimeBlocks` | PK `(LeaderId, BlockStart)`, `InstitutionId`, `Kind` (`Appointment` / `TimeOff`), `AppointmentId NULL`, `TimeOffId NULL`. CHECK: exactly the source column that matches `Kind` is set; `BlockStart` sits on a 15-minute boundary. |
| `booking.AppointmentStatusHistory` | Same shape as `BookingStatusHistory`. |

All of these join `sec.TenantIsolationPolicy`. **Still open:** how a donor reads their own rows under
RLS. Use whatever the booking engine decides for `Bookings`, and don't decide it twice.

## 4. Flows (each one is a single transaction plus outbox)

1. **Request** (donor). Check that the type is active, the advance window, the alignment and the
   location rule. Insert as `Requested`, write history, and add outbox `AppointmentRequested`. No
   blocks are written yet.
2. **Assign and approve** (`SERVICES.APPOINTMENTS.APPROVE`).
   1. Take `lock:leader:{id}` for each chosen leader, in ascending order, with a short PX. If a lock
      can't be taken, return 409 "try again".
   2. In the transaction: load the appointment with its `RowVersion`, insert the `AppointmentLeaders`
      rows (`Pending`), and insert the blocks for each leader. A **2627 → 409** response names the
      leader who isn't free. Then set `Approved`, write history, and add outbox `LeaderAssigned`
      (one per leader) and `AppointmentApproved` (to the donor).
   3. Release the locks with compare-and-delete.

   Adding or replacing leaders later uses the same path, without the status change.
3. **Respond** (a leader on `SERVICES.MYSCHEDULE.EDIT` for their own rows only, or an admin on behalf
   via `SERVICES.APPOINTMENTS.EDIT`). Accept sets the response. Decline sets the response, deletes
   that leader's blocks, and adds outbox `LeaderDeclined` to the institution.
4. **Reject / cancel.** Delete all the blocks, write history, and notify the donor and the leaders.
5. **Complete.** Allowed only after `EndsAt` (checked in the handler, because a CHECK can't read the
   clock).
6. **Time off.** Writes `TimeOff` blocks. If it clashes with an appointment, return 409 with the
   list, so the admin reassigns first.
7. **Reminders.** A Hangfire job sends them 24 hours before, to the donor and to every live leader.
   `DedupKey` is `appt-reminder:{appointmentId}:{recipient}`.

## 5. Permissions (`10_RbacCatalogue.sql`)

INSTITUTION portal, module `SERVICES`:

| Screen | Actions |
|---|---|
| `SERVICES.TYPES` | VIEW, CREATE, EDIT |
| `SERVICES.LEADERS` (roster, weekly hours, time off) | VIEW, CREATE, EDIT |
| `SERVICES.APPOINTMENTS` | VIEW, APPROVE (assign + approve), REJECT, EDIT (reassign, respond on behalf, cancel, complete) |
| `SERVICES.MYSCHEDULE` | VIEW, EDIT (accept / decline own) |

DONOR portal: `MYSERVICES.APPOINTMENTS` with VIEW, CREATE and EDIT (cancel own), scope `Self`.

OWNER and MANAGER levels get all of `SERVICES.*` except `MYSCHEDULE`. The new template role `LEADER`
gets only `SERVICES.MYSCHEDULE.*`. The dependencies follow the existing pattern (every action
requires VIEW).

## 6. Out of scope (state it in the report)

- Cross-institution search for services, and showing them on the map
- Leaders shared by several institutions (E8)
- Assigning leaders to **meal bookings**, e.g. who attends a home dana. This is the natural next step.
- Any fee for a service (P4: Sethika never charges)
- Automatic assignment. The picker only filters by "free + normally available".
- Live SignalR updates of schedules
- FullCalendar **resource / timeline views**: they are a paid FullCalendar Premium plugin. Use the
  standard `timeGrid` views with a leader filter.

## 7. Open questions (never invent data)

- Which services each faith's institutions actually offer, with typical durations and leader counts.
  Seed them as `DRAFT` suggestions and add them to DECISIONS "Unconfirmed reference data".
- The donor's cancellation window (hours before `StartsAt`)
- Turning `SERVICES` on for institutions approved before Phase E ships: through a platform-admin
  action, not a post-deployment script
- Donor reads under RLS (see §3)

## 8. Steps

- [ ] 8.1 Schema (§3), RLS predicates, `dotnet build`, and a publish no-op check (D10)
- [ ] 8.2 Seeds: module, screens, permissions and the `LEADER` template role; `DRAFT` service-type
      suggestions; the `APPOINTMENT` number series; type default modules for `Religious`
- [ ] 8.3 Domain: the `ServiceAppointment` aggregate with its status transitions. Block expansion as a
      pure function, unit-tested with buffers, alignment and the midnight crossing
- [ ] 8.4 Leaders, weekly hours, time off and service types: EF commands, Dapper lists, endpoints
- [ ] 8.5 Donor request flow and endpoints
- [ ] 8.6 Assign / approve with the lock and the blocks. **Concurrency test against real SQL Server and
      Redis**: two parallel assignments of one leader to overlapping times, exactly one succeeds. Run it
      again with the Redis lock bypassed, and the PK still stops it. Mutation: drop the PK and the test
      must fail.
- [ ] 8.7 Accept / decline, reject / cancel / complete, time-off conflicts
- [ ] 8.8 Outbox texts (en/si/ta) and the reminder job with `DedupKey`
- [ ] 8.9 Screens: service types, leader roster, appointments calendar, my schedule, donor request and
      my appointments. Playwright tests, including the 320 px layout
- [ ] 8.10 Report: the new tables in the ERD, and the two concurrency patterns side by side (counted
      slot inventory vs. interval overlap as time blocks)
