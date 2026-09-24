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

## RBAC
- Portal → Module → Screen → ScreenElement (Button, Dropdown, DropdownOption, Field, Tab, Section, GridColumn, ExportAction)
- Permission = Screen/Element + Action; permission dependencies (EDIT requires VIEW)
- Roles: platform roles, template roles (cloned on approval), institution-owned roles
- Per-user Grant/Deny overrides; Deny always wins; temporary role validity windows
- Institution modules come from type defaults + plan; disabled module removes its permissions automatically

## Deliverables produced so far
- `docs/proposal/Sethika_Project_Proposal.docx` (NIBM format)
- `src/Sethika.Database/` SQL project (78 tables, idempotent seeds) and `docs/erd/*.png` (5 diagrams)
- `branding/` logo kit (light/dark transparent SVG/PNG, favicon set)
