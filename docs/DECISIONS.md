# Sethika — Decision Log

Decisions made while planning the project, with the reasoning behind each.

## Product
| # | Decision | Why |
|---|----------|-----|
| P1 | Multi-faith, faith-neutral platform (temples, kovils, mosques, churches, elders' & children's homes) | Same problem exists across every community; configurable types avoid hard-coding any religion |
| P2 | Signature feature: cross-institution availability search ("free lunch slot near Galle on 14 March") | Nothing like it exists locally; solves the "call one by one" problem |
| P3 | Institutions must submit a registration request with legal documents and be approved by the platform admin | Prevents fake institutions; trust is the product |
| P4 | Never charge booking fees or take a cut of donations | Trust; revenue comes from optional tips, meal-package partner commission, CSR partnerships, premium plans, grants |
| P5 | Name: Sethika (sethika.com available) | Sinhala root ("seth" = blessing), modern sound, available .com |

## Architecture
| # | Decision | Why |
|---|----------|-----|
| A1 | Next.js frontend + .NET 10 Web API backend | SEO for public institution pages; developer's professional .NET/SQL Server experience |
| A2 | Next.js is frontend only — no business logic in Next API routes | Single backend, clean architecture story |
| A3 | Monolith, Clean Architecture | Right size for the project; microservices would be over-engineering |
| A4 | Redis is mandatory (course requirement) and genuinely used | Distributed booking locks, permission cache, GEO search, OTP, rate limiting, SignalR backplane |
| A5 | No RabbitMQ | Hangfire + outbox + Redis pub/sub cover background work; a broker adds cost without need |
| A6 | Hosting: Vercel + Azure App Service + Azure SQL (free offer) + managed Redis | Near-zero cost during the project |
| A7 | Modular monolith: 5 projects (Domain, Application, Infrastructure, Api, Database) + tests; modules are folders | Company-style splits (separate Core/Caching/DataAccess/Producer/Consumer projects) suit shared frameworks across many products, not a single app |
| A8 | No Docker required for development | SQL Server Developer Edition + Redis in WSL2 are enough; a docker-compose file can be added later for demos |
| A9 | Everything written from scratch | Employer code is company IP and must not be reused |
| A10 | SMS through **Text.lk** (sender id `Sethika`, registered 2026-10-23), v3 API with a bearer token; the Notify.lk sender stays as an alternative, and the console sender is Development only | The sender id was approved on Text.lk. The v3 API keeps the token out of URLs, which `IHttpClientFactory` logs. Text.lk's two docs pages disagree on the reply shape, so both are accepted and anything else is a failure. **Verified live on 2026-10-23**: an English and a Sinhala signup code reached a Dialog phone from "Sethika"; Sinhala renders correctly under `type: plain` (the docs say nothing about it). |

## Data
| # | Decision | Why |
|---|----------|-----|
| D1 | PK always `Id`, FK `<Entity>Id` | .NET/EF Core/Identity convention; Dapper `splitOn` default; generic base entity |
| D2 | EF Core for commands, Dapper inline SQL for reads | CQRS-lite; testable logic stays in C#; parameterized SQL gets plan caching and injection safety like SPs |
| D3 | Stored procedures only for set-based batch jobs | Avoid business logic leaking into the DB |
| D3b | SDK-style SQL project (Microsoft.Build.Sql) is the schema source of truth; no EF Core migrations | RLS, temporal tables, masking, composite FKs and filtered indexes are first-class in SQL projects; the build validates references without a server; dacpac deploys cleanly from CI |
| D4 | TVPs for list parameters (generic types only) | One round trip for syncs like role-permission saves; types rarely change |
| D5 | Row-Level Security + tenant-consistent composite FKs | Tenant isolation enforced by the DB, not only by careful queries |
| D6 | SlotInstances (slot-day inventory) as booking anchor | Supports exclusive and shared slots; CHECK constraints make overbooking impossible; availability search works under RLS |
| D7 | Temporal tables for roles/permissions/assignments | Answer "who had which permission on date X" |
| D8 | Transactional outbox | No lost SMS/events if the app crashes after commit |
| D9 | Dynamic data masking on NIC/phone/email; PDPA consents with versioned legal texts | Privacy and legal proof |
| D10 | CHECK constraints are always named, and never use `IN` or `BETWEEN`; masking functions are written with SQL Server's own spacing | Keeps `dotnet build` → SqlPackage publish a true no-op when nothing changed (see below) |
| D11 | `READ_COMMITTED_SNAPSHOT` is on in every database (`<ReadCommittedSnapshot>` in the SQL project, 2026-11-01) | Azure SQL runs with it on. With it off locally, plain reads waited for writers, so a race that only shows when they stop waiting could not be found before production, and a read under a write could deadlock (seen on the admin institution list). Sethika's guarantees rest on conditional writes, keys, `UPDLOCK` and `RowVersion` rather than on a plain read waiting; the one test that relied on a read queueing now holds an `UPDLOCK` instead, and the 230 race-class tests passed with it on |

### D10 — keeping the dacpac deploy convergent
DacFx compares its model against what SQL Server *stored*, so any expression the server rewrites on the
way in is seen as a difference on every publish. That makes the deploy drop and recreate the object
forever, and on a table with data each recreate is a full scan under a schema lock.

Three rules avoid it:

1. **Name every CHECK constraint.** An unnamed one gets a server-generated name (`CK__Users__Status__59E54FE7`)
   whose hash DacFx cannot reproduce, so it never matches.
2. **Never write `IN` or `BETWEEN` in a CHECK.** SQL Server expands `x BETWEEN a AND b` to `x >= a AND x <= b`,
   and `x IN (a,b)` to `x = b OR x = a` (*reversed*). Write the expanded form yourself. An explicit `OR` chain
   is stored exactly as written, so keep the values in natural reading order — only `IN` reverses them.
   `x NOT IN (a,b)` becomes `NOT (x = a OR x = b)`.
3. **Write masking functions the way the server stores them:** `partial(4, "XXXXX", 3)` — with a space after
   each comma. Without the spaces DacFx re-masks the column on every publish, which also forces a rebuild of
   every index on that column and a drop/recreate of `sec.TenantIsolationPolicy`, leaving RLS off mid-deploy.

Filtered-index predicates (`WHERE Status IN (...)`) are not affected and may keep using `IN`.

To check: `SqlPackage /Action:Script` against the target should produce a script containing only the
post-deployment seed MERGEs and no schema statements.

## Scope revision (proposal v2)

The proposal originally listed **online payments** and **a Sinhala/Tamil interface** under
"beyond the scope of this project". Both were brought into scope, and
`docs/proposal/Sethika_Project_Proposal.docx` was revised accordingly
(`Sethika_Project_Proposal_v1_original.docx` keeps the pre-revision text so the change is auditable).

| # | Decision | Why |
|---|----------|-----|
| S1 | Trilingual interface (en/si/ta) is built from Phase A, not retrofitted | The schema already carries `NameSi`/`NameTa`/`PreferredLanguage`; adding i18n after the screens exist means rewriting every one of them |
| S2 | Online donation payments via PayHere, Phase D, sandbox in development | A money donation can also simply be *recorded* by the institution, so the booking flow never depends on a gateway being reachable |
| S3 | The final report must reconcile two places where the proposal understates the build | The proposal describes ~10 snake_case tables in 3NF; the delivered schema is 83 tables across 8 schemas with temporal history, RLS and data masking |

Four statements in the proposal contradicted the implementation and were corrected in the same
revision: EF Core "migrations" (the SQL Database Project is the source of truth), Docker Compose
for local dev (not required), query-scoped access control (it is Row-Level Security in the
database), and "one active booking per slot per date" (shared slots hold up to their configured
maximum).

> PayHere's notify URL is a server-to-server callback, so it cannot reach `localhost`. Local
> development needs a tunnel (ngrok or cloudflared) or payment confirmations never arrive.

## Scope revision (proposal v3, 2026-10-23)

The supervisor's feedback on the submitted proposal asked for "a few more functions, like coordination
of religious leaders and making appointments". `docs/proposal/Sethika_Project_Proposal.docx` (and its
`.pdf`) is now v3.

| # | Decision | Why |
|---|----------|-----|
| S4 | New **Phase E: religious services and leader coordination**: service types, a leader roster with availability and leave, appointment requests, conflict-free assignment with accept/decline, and a personal schedule. Design: `docs/plans/PHASE-E-RELIGIOUS-SERVICES.md` | Answers the feedback and stays faith-neutral (services are configuration, like meal slots). It adds a second concurrency guarantee of a different shape: interval overlap, enforced as 15-minute time blocks under a primary key. That is worth more in the report than another CRUD module. |
| S5 | Phase E is built **after** the Phase A booking engine, and is cut before Phase A rigour is | It reuses the engine's lock, outbox and number-series patterns. If time runs out, the report states what was not built. |
| S6 | v3 is based on the copy **submitted on 2026-10-18**, not on the repo's v2 | That copy (`Sethika_Project_Proposal_submitted_2026-10-18.docx`) was edited from v1 outside the repo and still carried v1's four contradictions and the old "beyond scope" list. v3 re-applies v2's corrections (dacpac instead of migrations, no Docker, shared slots, trilingual UI, PayHere) to it. The submitted format has no Data Model section, so v2's ERD/3NF chapter lives on in `Sethika_Project_Proposal_v2_repo.docx` for the final report. |

## Scope revision (mobile app, 2026-10-24)

The supervisor suggested that a mobile application part would strengthen the project.

| # | Decision | Why |
|---|----------|-----|
| S7 | New **Phase F: a donor-first mobile app** (React Native + Expo, TypeScript) in `mobile/`, calling the same API. Staff get only a "today's sponsors / mark served" view; all other institution work stays on the web. Design: `docs/plans/PHASE-F-MOBILE.md` | One codebase for Android and iOS in the language `web/` already uses. Donors are the phone users; the API stays the single place for business rules, so the app adds a client, not a second backend. |
| S8 | Phase F runs **in parallel** with the backend phases: scaffolding and mobile sign-in start now, each later slice starts when the API it calls has merged | The booking engine is being built on another machine. The app never changes another module's API from its own branch, so the two lines of work don't collide. |

## Redis (local development)
| # | Decision | Why |
|---|----------|-----|
| R1 | Sethika uses a dedicated logical database (`defaultDatabase=3`) | db 0 on this machine is shared with another application's cache (`EstateList_*`, `RoleMenuList_*`). A `FLUSHDB` while testing would wipe those keys, and both would compete for the same eviction budget |
| R2 | Connection strings and the JWT signing key live in User Secrets | Nothing secret in `appsettings.json` or source control. `dotnet user-secrets set "ConnectionStrings:Redis" ...` |

> **Local Redis is 3.2.100** — the archived Microsoft Windows port, not the WSL2 install
> `docs/LOCAL_SETUP.md` describes (WSL is not installed on this machine). Everything the
> permission cache needs works on it, but it has had no security patches since ~2016 and
> production Azure Redis will be 6.x/7.x. Worth replacing before relying on anything newer
> than Redis 3.2 semantics.

## RBAC
- Effective permissions come from `auth.vw_EffectivePermissions` and are **never** re-derived in C#.
  Deny-wins and the module-enabled intersection are both implemented in that view; a second
  implementation in application code could disagree with it, and the database is what decides access.
- Cached in Redis under `perm:{userId}:{institutionId}:v{userVersion}.{institutionVersion}`. The versions
  are part of the key, so bumping either one *is* the invalidation: the next read builds a different key,
  misses and reloads. No eviction call to forget, and it works across every API instance. A null
  institution (donors, platform staff) renders as the literal segment `global` with version `0`.
- A Redis outage degrades to a SQL read, never to a denial — a cache failure must not lock everyone out.
- `[HasPermission("CODE")]` on every endpoint. Policies are materialised per code on demand by
  `PermissionPolicyProvider`, so adding a permission never means editing `Program.cs`.
- Portal → Module → Screen → ScreenElement (Button, Dropdown, DropdownOption, Field, Tab, Section, GridColumn, ExportAction)
- Permission = Screen/Element + Action; permission dependencies (EDIT requires VIEW)
- Roles: platform roles, template roles (cloned on approval), institution-owned roles
- Per-user Grant/Deny overrides; Deny always wins; temporary role validity windows
- Institution modules come from type defaults + plan; disabled module removes its permissions automatically

## Onboarding (plan 02) — what was cut

The onboarding slice ships the core flow plus clarifications. These have tables or columns in the
schema but were **deliberately not built**, and the report says so:

- Risk scoring (`RiskScore`/`RiskFlags`: duplicate files via `IX_Files_Hash`, a reused NIC via
  `IX_OfficeBearer_Nic`, location clashes), `SlaDueAt`, `AssignedReviewerId`
- Evidence files on checklist items; staff invitations (`org.StaffInvitations`)
- Plan selection (`RequestedPlanId` stays NULL → FREE); DS divisions (no seed, nullable column)
- Virus scanning of uploads (`ScanStatus`, B4); a map picker for the location (B11)
- Editing the institution profile after approval; re-verification (`NextReviewDue`); suspending or
  deactivating an institution; institution reports; the public institution page (Phase B)

## Booking (plan 03)

Full reasoning in `docs/plans/03-BOOKING.md` §2; choices made while building are its §8 (E1-E35).

| # | Decision | Why |
|---|----------|-----|
| C1 | `BookedSponsors` counts `Requested` **and** `Approved` bookings; reject and cancel give the place back in the same transaction | If only approved bookings counted, two donors could both hold "requested" on an exclusive slot and the institution would have to choose: double booking moved one step later |
| C2 | Three layers: a Redis lock `lock:slot:{mealSlotId}:{yyyy-MM-dd}` (SET NX PX, Lua compare-and-delete), a conditional `UPDATE ... WHERE BookedSponsors < MaxSponsors`, and CHECK constraints. If Redis is down, booking goes on without the lock | **The lock is an optimisation, the database is the guarantee.** The lock gives a fast, clean 409 and serialises get-or-create; the UPDATE and the CHECKs are correct on their own, so a Redis outage degrades rather than denies |
| C3 | A slot instance is created on first booking, under the lock, copying the slot's capacity; `UQ_SlotInst_SlotDate` is the backstop | Instances are snapshots: editing a slot's capacity later does not break a promise already made |
| C4 | Bookability is one pure domain function, `MealSlotRules.CanBook`, returning the first failing reason; dates count in Asia/Colombo | Unit-testable without a database, and called inside the lock on fresh data |
| C5 | Status transitions live on the `Booking` aggregate; each writes `BookingStatusHistory` and an outbox message; concurrent decisions are caught by `RowVersion` (409) | One place for the state machine; the donor may cancel until the day before, the institution after |
| C6 | The donor is the signed-in user; name and phone are snapshots; one active booking per phone per slot-day (`UX_Bookings_DonorPerSlot`) | Booking on someone's behalf and the donor CRM are cut |
| C7 | `BookingNo` is platform-wide per year (`BK-2026-000123`), drawn inside the booking transaction | A rolled-back booking leaves a gap, as with registration numbers (B5) |
| C8 | `booking.SlotInstances` has **no** RLS | See the exposure below |
| C9 | `booking.MealSlots.PublicId` (UUID v7, unique) | A slot is addressed from the donor's side, and URLs never carry `Id` |
| C10 | Discovery is a shared link `/i/{slug}` plus a plain `/institutions` list of `Active` and `IsPubliclyListed` institutions; both anonymous and rate-limited. **Superseded by Phase B:** the list became the search page `/find`, and `/institutions` redirects there (G11) | Search, GEO and the map are Phase B. An institution is listed only once its Owner turns it on |
| C11 | SMS to the donor through the outbox, in their language, with `DedupKey`: received/confirmed, approved, rejected, cancelled, and a reminder at 18:00 Colombo the day before | The institution gets no SMS (cost); its calendar shows the waiting requests |
| C12 / E32 | Sethika's own month grid (`BowlCalendar`), not FullCalendar | A CSS grid with Intl names and keyboard navigation did what was needed without a dependency |

**The C8 exposure, stated plainly.** A donor's connection has to update another tenant's
availability counters, and availability is public, so `SlotInstances` sits outside RLS. The
consequence is that any signed-in connection *could* change any slot's counters. The only code that
does so is the booking handler, and the CHECK constraints bound what it can store
(`CK_SlotInst_Sponsors`, `CK_SlotInst_Headcount`: never above the maximum, never below zero). This is a known, deliberate trade-off, and the report
says so.

**Cut from plan 03, and the report says so:** the waitlist; co-sponsors (`BookingContributors`);
recurring bookings and the Memorial Dana Planner (Phase C); staff booking on behalf of a donor; the
donor CRM (`InstitutionDonors`); a seasons screen (the rule is enforced, seasons have no UI); caterer
fulfilment; SignalR live updates (Phase C); availability alerts; search, GEO and the map (Phase B).

## Discovery (Phase B)

Full reasoning in `docs/plans/06-DISCOVERY.md` §2 (G1-G11). All four promises of the proposal (its §0)
are built; nothing on the cut order was cut.

| # | Decision | Why |
|---|----------|-----|
| G1 | A search takes a date (Colombo), a time of day, an optional institution type, and where: near a point within 5, 10, 25 or 50 km, in a district, or anywhere. It answers the institutions with a slot that can be booked that day, nearest first (else by name), at most 100 | The proposal's question, word for word: "which institutions near Galle have a free lunch slot on 14 March?" |
| G2 | The time of day comes from the slot's start time: morning before 10:30, midday until 15:00, evening after | Slots have no meal type, and dana times are fixed by custom; the boundaries live in one place (`MealTimes`) |
| G3 | Where comes from the browser's location, the map's centre ("search this area") or a district; no geocoding. The browser's position is rounded to 3 decimals (about 100 m) before it is sent and is never stored | A geocoder is an outside service with its own terms, and district centre coordinates would be invented reference data |
| G4, G5 | **SQL Server is the truth, the Redis GEO set is an index of it.** `GEORADIUS` (the local Redis is 3.2) or, when Redis is down or the key is missing, `Location.STDistance` under the existing spatial index only fetch candidates from a circle 2 % wider; SQL re-checks `Active` and listed, and one haversine (`GeoPoint.DistanceKmTo`) decides who is inside and in what order. The set is rebuilt at start and nightly (`MULTI/EXEC` of `DEL` + `GEOADD`) and kept current by an outbox handler on listing changes | Redis and SQL Server measure on different earths (a sphere and WGS 84, 0.12 % apart near Colombo): if each decided, the answer at the edge would depend on whether Redis was up. As with the booking lock (C2), Redis makes it fast and SQL makes it correct |
| G6 | "One calendar": `GET /api/search/month` counts, for each day, the institutions a day search would answer | The proposal's calendar; it reuses G1's judgement for every day |
| G7 | Every slot-day is judged by `MealSlotRules.Openings`, from one batch read for all candidates (`core.IntList`); results are not cached | The search, an institution's calendar and the booking engine cannot disagree, and a cached "free" would send donors to a full slot |
| G8 | The search endpoints are anonymous and rate-limited per client, and answer only what the public page shows; each day search logs one row in `booking.AvailabilitySearches` with no user and no coordinates | Searching needs no account; the log is the demand signal for Phase D without knowing where a donor stood |
| G9 | `/i/[slug]` renders on the server: the institution in the HTML, its metadata (canonical, `hreflang` en/si/ta, Open Graph), schema.org JSON-LD, a dynamic `sitemap.xml` and a `robots.txt` that keeps the signed-in screens out | Promise 1: a search engine reads HTML, not the API |
| G10 | The map is Leaflet with OpenStreetMap tiles and our own numbered markers, loaded in the browser only | In the stack already; one package, no wrapper. Production would move to a tile provider |
| G11 | The search page is `/find`: filters in the address (back undoes a filter; the phone's List/Map tab does not), list and map side by side on a wide screen, tabs on a phone; `/institutions` is a permanent redirect to it | One place to look; a search can be shared as a link |

**Cut from Phase B, and the report says so:** geocoding typed place names, routes and directions;
marker clustering (results are capped at 100); searching for religious services across institutions;
availability alerts and the demand heat map (the search log is kept for them); a production tile
provider.

## Religious services (Phase E)

Full reasoning in `docs/plans/PHASE-E-RELIGIOUS-SERVICES.md` §2 (V1-V16) and §10 (V17-V39, decided
while building). All six promises of proposal v3 (its §0a) are built; nothing on the cut order was cut.

| # | Decision | Why |
|---|----------|-----|
| V1, V21 | A leader is a person record (`org.ReligiousLeaders`) with an **optional** account, linked by phone number; an administrator can answer on their behalf | Many leaders (elderly monks, visiting clergy) never log in |
| V2, V3 | **A leader can never be in two places at once:** every assignment and every leave period writes 15-minute rows to `booking.LeaderTimeBlocks`, whose primary key is `(LeaderId, BlockStart)`, so an overlap is a key violation (2627). A Redis lock `lock:leader:{id}`, taken in ascending id order, comes first; without Redis the key still holds | "No two intervals overlap" cannot be a constraint, "no two rows share a key" can. The database is the guarantee, as in C2; the fixed lock order means two admins cannot deadlock. The report sets it beside the booking engine as the second concurrency pattern (counted inventory vs interval overlap) |
| V4 | Service types are typed by each institution; **no seeded suggestions per faith** | Which services a faith offers, and for how long, are facts nobody has confirmed (CLAUDE.md) |
| V5, V7, V11, V28 | Times in UTC, shown in Asia/Colombo, on a 15-minute grid (05:00-21:00 offered); an at-home service adds the type's travel buffer to the blocks; the donor sends a Colombo date and start time | UTC+05:30 keeps the grid aligned in both; back-to-back home visits across town are refused |
| V6, V32 | Appointment `Requested → Approved → Completed`, or `Rejected` / `Cancelled`; "needs leaders" is computed, not stored; the institution's agenda has four fixed views (upcoming, waiting, needs leaders, history) | No status that can disagree with its rows; an agenda answers "what needs me" and fits 320 px (no time grid, V10) |
| V9, V16, V27 | RLS: appointments are `fn_TenantOrOwner` like bookings, the roster and blocks `fn_Tenant`. A donor's cancel frees the leaders' blocks through the outbox (platform session), and every `AppointmentCancelled` message does so idempotently | The donor's session cannot see the roster, and opening it would expose it. A stale block can only refuse an assignment, never allow a double one |
| V12, V26 | Module `SERVICES` is on by default for the four religious institution types only; switched off, the page lists no services and requests are refused | Care homes have no religious leaders |
| V13 | A donor may cancel until the end of the day before (Colombo) | The same rule as meals |
| V17-V20, V22, V23 | Leave is at most 31 days, written from the current block on; leave over an appointment is refused naming it (`time_off_clashes`). Weekly hours only filter the picker (`null` = never recorded); taking a leader off the roster names their appointments and changes none | Weekly hours are a filter, not a guarantee, so last write wins; the institution decides what to do with a leader's appointments |
| V30, V31, V33, V34 | Remove, respond, reject, cancel and complete take no lock (they only free time) but always write the appointment row, so two changes meet on `RowVersion` (409 `stale`). A leader answers from their own schedule, found through their roster row in the session's institution | A decline cannot slip past a cancellation; a loser frees nothing |
| V14, V15, V35-V39 | SMS through the outbox: the donor on request, approval, rejection, cancellation and the reminder; a leader on assignment, removal, cancellation and the reminder while still expected. Reminders at 18:00 Colombo the day before, in their own Hangfire job, with `DedupKey`s | The institution gets no texts, as with bookings. A leader without a phone is skipped, not failed |
| V25, V29 | Services ride on the existing public page (`GET /api/institutions/{slug}`); requests are limited to 10 per donor per hour | No new anonymous endpoint; each request takes a number from a platform-wide series |

**Cut from Phase E, and the report says so** (its §6): searching for services across institutions
and on the map; leaders shared by several institutions; assigning leaders to meal bookings; automatic
assignment (the picker only filters); live SignalR updates; a time-grid calendar; per-faith seeded
service types; a platform action to enable `SERVICES` for institutions approved before Phase E;
inviting a leader to create an account from the roster.

## Unconfirmed reference data (seeded as drafts)
CLAUDE.md forbids inventing facts about real institutions. Where the flow cannot run without a row,
the row is seeded as a visibly marked draft and listed here until someone confirms or replaces it.

| Data | Seed | Marked | Confirm with |
|------|------|--------|--------------|
| Terms of Use, Privacy Policy | `09_LegalDocuments.sql` | `0.1-draft` in title and body | a legal reviewer |
| Institution declaration | `09_LegalDocuments.sql` | `0.1-draft` in title and body | a legal reviewer |
| Document types and the documents each institution type must upload | `12_OnboardingDrafts.sql` | `DRAFT` in every name and instruction | real institutions of each type |
| Reviewer checklist items | `12_OnboardingDrafts.sql` | `DRAFT` in every question | the platform's reviewers |
| Religious service suggestions per institution type (name, duration, leaders needed) | not seeded (V4): each institution types its own | — | real institutions of each faith, if suggestions are ever added |
| SMS texts for religious services (Sinhala and Tamil) | `11_NotifyTemplates.sql` | listed in `docs/translations-to-review.md` | a Sinhala and a Tamil reviewer |
| Sinhala and Tamil names of districts and institution types | not seeded: `NameSi`/`NameTa` are `NULL`, so the search page shows the English name in every language | — | a Sinhala and a Tamil reviewer |

The registration reject reasons in the same seed are Sethika's own review policy, not facts about
institutions, and are not drafts. Every row is read from the tables at run time, so a confirmed list
is a seed edit, not a code change.

## Deliverables produced so far
- `docs/proposal/Sethika_Project_Proposal.docx` + `.pdf` (NIBM format, v3); earlier versions kept beside it
- `src/Sethika.Database/` SQL project (78 tables, idempotent seeds) and `docs/erd/*.png` (5 diagrams)
- `branding/` logo kit (light/dark transparent SVG/PNG, favicon set)

## Plans

Design work that is agreed but not yet implemented lives in `docs/plans/`. Decisions move up into the
tables above once the code exists and the choice has held.

| Plan | Branch | State |
|------|--------|-------|
| `docs/plans/01-AUTH.md` — authentication, MFA step-up, i18n foundation, design system | `2-authentication-and-i18n-foundation` | Merged into `develop` (PR #4, #5); browser walk-throughs still owed — see its status line |
| `docs/plans/02-ONBOARDING.md` — institution onboarding and approval, outbox processor | `3-institution-onboarding` | Merged into `develop` (PR #6, #7 Text.lk) on 2026-10-23 |
| `docs/plans/03-BOOKING.md` — meal slots, the booking engine (Redis lock + conditional UPDATE + CHECKs), donor booking, reminders | `5-booking-engine` | 5.1-5.7 built (engine, institution and donor APIs, SMS and reminders, screens); decisions summarised above under "Booking (plan 03)", the rest in its §8 |
| `docs/plans/04-UI-SHELL.md` — the app shell, dashboards, access screens, staff invitations | `ui-ux-overhaul`, `access-hardening` | Merged into `develop` (PR #13, #14) |
| `docs/plans/05-REDESIGN.md` — true-black dark theme, living sidebar, Day Ledger dashboards, every screen on the new base | `redesign` | Merged into `develop` (PR #31) on 2026-11-01 |
| `docs/plans/PHASE-E-RELIGIOUS-SERVICES.md` — religious services, leader roster, conflict-free assignment | `phase-e-religious-services`, `phase-e-donor` | Done. 8.1-8.4 and 8.6 merged (PR #12); 8.5 and 8.7-8.10 (donor requests, assignment endpoints, texts and reminders, screens) pushed to `develop` on 2026-11-01 without a pull request, described in `docs/plans/PHASE-E-DELIVERY.md`; decisions summarised above under "Religious services (Phase E)" |
| `docs/plans/06-DISCOVERY.md` — public institution pages, cross-institution search, Redis GEO, the month view and the map | `rcsi-on` (with `READ_COMMITTED_SNAPSHOT`, D11) | B.1-B.4 built and committed; decisions summarised above under "Discovery (Phase B)" |
| `docs/plans/PHASE-F-MOBILE.md` — donor mobile app (Expo), mobile sessions, push notifications | `ccr-79f10240-uk4n3v`, `mobile-booking` | F.1-F.4 merged (PR #9, #11: scaffold, mobile sessions, browse and book, my bookings); phone walk-through owed; F.5 push notifications next (S8) |
