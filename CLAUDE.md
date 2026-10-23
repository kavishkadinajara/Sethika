# Sethika — Project Context for Claude Code

Sethika is a multi-faith meal & donation sponsorship platform for Sri Lanka.
Temples, kovils, mosques, churches, elders' homes and children's homes publish
meal slots and needs; donors search across all of them, book, and get reminders.
HND Software Engineering (NIBM) 2nd-year project, built to production standards.

Read before large changes: `docs/DECISIONS.md` (what was decided and why) and `docs/LOCAL_SETUP.md`.

## Ground rules
- Everything here is written from scratch. **Never copy code from any employer/company repository.**
- **Modular monolith** + Clean Architecture. One deployable API (Hangfire and SignalR run in the same process).
  No microservices, no RabbitMQ, no message broker.
- **No Docker required.** Local dev uses SQL Server Developer Edition + Redis in WSL2 (see `docs/LOCAL_SETUP.md`).
- Do not add new projects, packages or architectural layers without explaining why first.

## Solution layout
```
Sethika/
├─ Sethika.sln
├─ Directory.Build.props          # net10.0, nullable, warnings as errors (C# projects only)
├─ src/
│  ├─ Sethika.Domain/             # entities, value objects, domain rules — no dependencies
│  ├─ Sethika.Application/        # use cases (commands/queries), interfaces, validation (FluentValidation)
│  │   └─ Access/ Onboarding/ Institutions/ Bookings/ Giving/ Notifications/   (one folder per module)
│  ├─ Sethika.Infrastructure/     # EF Core, Dapper, Redis, SMS/email, files, Hangfire jobs, outbox
│  │   └─ Persistence/ Queries/ Caching/ Messaging/ Security/
│  ├─ Sethika.Api/                # host: endpoints, auth, SignalR hubs, Hangfire dashboard, DI wiring
│  └─ Sethika.Database/           # SDK-style SQL project (Microsoft.Build.Sql) — SCHEMA SOURCE OF TRUTH
├─ tests/
│  ├─ Sethika.Domain.Tests/
│  ├─ Sethika.Application.Tests/
│  └─ Sethika.Api.IntegrationTests/
├─ web/                           # Next.js frontend
├─ docs/  branding/
```
Dependency rule: `Api → Infrastructure → Application → Domain`. Domain references nothing.
Modules talk to each other through Application interfaces, never by reaching into another module's internals.
Adding the SQL project to the .sln: project type GUID `42EA0DBD-9CF1-443E-919E-BE9C484E4577`
(or `dotnet sln add src/Sethika.Database/Sethika.Database.sqlproj`).

## Tech stack
- Frontend: Next.js (App Router) + TypeScript (strict) + Tailwind + shadcn/ui, FullCalendar, Leaflet + OpenStreetMap
- Backend: ASP.NET Core Web API on .NET 10
- Data: SQL Server 2022 locally, Azure SQL in production
- Redis (mandatory course requirement): booking locks, permission cache, caching, GEO search, OTP, rate limiting, SignalR backplane
- SignalR (live slot updates), Hangfire (reminders, recurring bookings, outbox processor)
- Auth: ASP.NET Core Identity + JWT access token + rotating refresh tokens in httpOnly cookies
- Hosting: Vercel (web), Azure App Service (API), Azure SQL, managed Redis. CI/CD: GitHub Actions or Azure DevOps

## Database — `src/Sethika.Database` is the source of truth
- Schema changes are made ONLY by editing `.sql` files in the SQL project, then `dotnet build` → `.dacpac` → SqlPackage publish.
- **EF Core migrations are NOT used.** EF Core is only the ORM: map entities to existing tables in `IEntityTypeConfiguration` classes
  and keep those mappings in sync when a table changes (column names, types, `RowVersion`, temporal tables via `IsTemporal`).
- Layout: one object per file — `<schema>/Tables/`, `Views/`, `Functions/`, `Types/`, `SecurityPolicies/`, schemas in `Security/`.
- Reference data lives in `Scripts/Seed/*.sql`, run from `Scripts/PostDeployment.sql`. **Seeds must be idempotent (MERGE).**
  Never put business or test data in post-deployment.
- Temporal tables have explicit history tables in the project (`*History.sql`). Change both together.
- Stored procedures only for set-based batch jobs (yearly recurring generation, heavy report aggregation) → `<schema>/StoredProcedures/`.

### Conventions (do not deviate)
- PK column is always `Id`; FK columns are `<Entity>Id`. Composite-key link tables have no `Id`.
- Schemas: `core`, `auth`, `org`, `booking`, `giving`, `notify`, `audit`, `sec`.
- UTC everywhere: `DATETIME2(3)` + `SYSUTCDATETIME()`.
- Status columns are `VARCHAR` + `CHECK` (readable values, not magic ints).
- Every tenant-owned table has `InstitutionId`; cross-table tenant consistency uses composite FKs
  like `(RoleId, InstitutionId) → auth.Roles(Id, InstitutionId)`.
- Never expose internal `Id` in URLs or public APIs; use `Slug`, `PublicId` or business numbers
  (`BookingNo`, `ReceiptNo`, `RequestNo`) generated from `core.NumberSeries` (row locked with `UPDLOCK`).
- Money/receipts are never deleted — only voided (maker-checker: voider ≠ recorder).

## Data access rules
- **Commands (create/update/delete): EF Core** — change tracking, `RowVersion` concurrency, transactions.
- **Queries (lists, search, dashboards, reports): Dapper with inline parameterized SQL.**
  Never string-interpolate SQL. Never `SELECT *`. Alias columns. Large SQL in `.sql` embedded resources.
  Pass `CancellationToken` via `CommandDefinition`.
- TVPs for list parameters: `core.IntList`, `core.BigIntList`, `core.GuidList`, `core.DateList`
  (small lists → Dapper `IN @Ids`; huge imports → `SqlBulkCopy`). EF Core does not support TVPs — use Dapper.

## Multi-tenancy & security (critical)
- **Row-Level Security** is ON (`sec.TenantIsolationPolicy`). On every connection open, set
  `sp_set_session_context` for `UserId`, `InstitutionId`, `IsPlatform` with `@read_only = 1`.
  Implement once as an EF Core `DbConnectionInterceptor` + a Dapper connection factory. Background jobs use `IsPlatform = 1`.
- RBAC catalogue: `Portal → Module → Screen → ScreenElement`; `Permission = Screen/Element + Action`
  (VIEW, CREATE, EDIT, DELETE, APPROVE, REJECT, EXPORT, PRINT, ENABLE, EXECUTE). Codes like `BOOKINGS.CALENDAR.APPROVE`.
- Effective permissions = (role grants ∩ institution's enabled modules) + grant overrides − deny overrides. **Deny wins.**
  Source: `auth.vw_EffectivePermissions`. Redis cache key
  `perm:{userId}:{institutionId}:v{Users.PermissionsVersion}.{Institutions.PermissionsVersion}`; bump versions on any change.
- **Every API endpoint** has a permission policy, e.g. `[HasPermission("BOOKINGS.CALENDAR.APPROVE")]`.
  Hiding a button in the UI is UX only — the backend check is the security.
- Permissions with `RequiresMfa = 1` need step-up verification.
- Anti-escalation: a user can only grant permissions they hold; institution roles never contain Platform-scope permissions.

## Core flows
- **Institution onboarding:** user signs up (phone OTP) → submits `org.RegistrationRequests` with legal details,
  office bearers, required documents, versioned declaration → admin reviews (checklist, clarifications) →
  **Approve** runs ONE transaction: create institution, copy verified documents, enable modules (type defaults + plan),
  clone template roles & permissions, add applicant as Owner (+ Owner role), status history, audit, outbox event.
- **Booking:** `MealSlots` → `SlotInstances` (one row per slot per date = inventory) → `Bookings`.
  1. Redis lock `lock:slot:{mealSlotId}:{date}` (SET NX PX)
  2. Get-or-create SlotInstance; conditional `UPDATE ... SET BookedSponsors += 1 WHERE BookedSponsors < MaxSponsors`
     (CHECK constraints make overbooking impossible)
  3. Insert booking + outbox message in the same transaction
- Side effects (SMS, email, SignalR, cache invalidation) go through `core.OutboxMessages`, processed by Hangfire.
  Notifications use `DedupKey` so reminders are never sent twice.

## Delivery phases
- **Phase A (build first):** auth + RBAC + RLS, onboarding & approval, meal slots, SlotInstances booking engine, outbox, reminders.
- **Phase B:** public pages, cross-institution search + Redis GEO + map.
- **Phase C:** recurring bookings, **Memorial Dana Planner**, co-sponsors, SignalR live updates.
- **Phase D:** donations, needs board, gratitude wall, dashboards & reports.
- **Phase E (added in proposal v3 after supervisor feedback):** religious services & leader coordination —
  service types, leader roster + leave, appointment requests, conflict-free leader assignment (accept/decline),
  personal schedule. Build after the booking engine. Design: `docs/plans/PHASE-E-RELIGIOUS-SERVICES.md`.
- Designed but optional: plans/subscriptions, availability alerts, demand heatmap, impersonation, trust & safety.

## First tasks for Claude Code
1. `dotnet build src/Sethika.Database` and fix every build error/warning in the SQL project
   (it was generated from a single script and syntax-checked with a parser, never built with DacFx).
2. Publish to local SQL Server with `Sethika.Database.local.publish.xml`; verify seeds ran.
3. Scaffold the solution and C# projects exactly as in the layout above, then Phase A.

## Open items (never invent data for these)
- `core.MemorialRules` per religion, required legal documents per institution type, and `core.SpecialDays`
  must be confirmed with real institutions before seeding.
- Religious service types per faith (names, durations, leaders needed) for Phase E — seed only as `DRAFT` suggestions.

## Branding
- Name **Sethika** (sethika.com). Colours: Estate Green `#16513D`, Offering Gold `#E0A33E`, Deep Ink `#0E2A21`, Rice Ivory `#F7F4EC`.
- Fonts: Outfit (UI/wordmark), Fraunces Italic (tagline). Assets in `branding/`.
- Faith-neutral design: never use one religion's symbols in shared UI.
