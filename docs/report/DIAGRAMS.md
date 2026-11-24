# Sethika — System Diagrams

The UML set for the report (ERDs, class, use case and sequence diagrams as PNG, SVG and PDF) is in
`docs/diagrams/` — see its README. Phase-specific diagrams are in each `PHASE-*.md`. This file holds the
diagrams for the system as a whole: architecture, use cases, the security model (RBAC + RLS), and the
three core flows (booking, session refresh, onboarding approval). Every step shown is in the code; file
names are given under each diagram so the diagram can be checked against the code.

---

## 1. System architecture

One deployable API (a modular monolith). Hangfire and SignalR run inside the same process. There are
two clients, the Next.js web app and the Expo mobile app, and both call the same API.

```mermaid
flowchart LR
    subgraph Clients
        WEB["Web app<br/>Next.js App Router + TypeScript<br/>(Vercel)"]
        MOB["Donor mobile app<br/>React Native + Expo"]
    end

    subgraph API["Sethika.Api — one process (Azure App Service)"]
        EP["Minimal API endpoints<br/>[HasPermission] policy on each"]
        HUB["SignalR hub<br/>/api/hubs/availability"]
        HF["Hangfire server<br/>outbox, reminders,<br/>recurring + standing bookings,<br/>GEO rebuild"]
        APP["Application layer<br/>(use-case handlers)"]
        INF["Infrastructure<br/>EF Core commands · Dapper queries"]
        EP --> APP
        HF --> APP
        APP --> INF
    end

    SQL[("SQL Server / Azure SQL<br/>78 tables · RLS · temporal<br/>+ Hangfire schema")]
    REDIS[("Redis<br/>locks · permission cache · OTP<br/>rate limits · GEO · SignalR backplane")]

    SMS["SMS gateway<br/>(Notify.lk / Text.lk)"]
    PUSH["Expo Push Service"]
    PAY["PayHere<br/>(sandbox)"]
    MAP["OpenStreetMap tiles<br/>(Leaflet)"]

    WEB -- "HTTPS + JWT<br/>refresh token in httpOnly cookie" --> EP
    MOB -- "HTTPS + JWT<br/>refresh token in secure store" --> EP
    WEB -. "WebSocket" .-> HUB
    WEB --> MAP
    MOB --> MAP

    INF --> SQL
    INF --> REDIS
    HUB --- REDIS
    INF --> SMS
    INF --> PUSH
    WEB -- "checkout redirect" --> PAY
    PAY -- "notify callback<br/>/api/payments/payhere/notify" --> EP
```

Source: `src/Sethika.Api/Program.cs`, `src/Sethika.Infrastructure/DependencyInjection.cs`,
`Infrastructure/Caching/*`, `Infrastructure/Notifications/*`, `Infrastructure/Jobs/*`.

---

## 2. Clean Architecture layers and modules

References point inward only. Modules talk to each other through Application interfaces.

```mermaid
flowchart TB
    API["<b>Sethika.Api</b><br/>endpoints · auth policies · SignalR hub<br/>Hangfire dashboard · DI wiring"]
    INF["<b>Sethika.Infrastructure</b><br/>Persistence (EF Core) · Queries (Dapper)<br/>Caching (Redis) · Messaging (outbox)<br/>Security (JWT, OTP, permissions) · Jobs · Notifications"]
    APP["<b>Sethika.Application</b><br/>Access · Onboarding · Institutions · Bookings<br/>Giving · Notifications · Files<br/>interfaces (ISethikaDbContext, ISlotLocks, INumberSeries …)"]
    DOM["<b>Sethika.Domain</b><br/>entities · value objects · rules<br/>(MealSlotRules, RegistrationRequest.Approve …)<br/>no dependencies"]
    DB["<b>Sethika.Database</b> (SQL project)<br/>schema source of truth → .dacpac → SqlPackage"]

    API --> INF --> APP --> DOM
    INF -. "maps to existing tables<br/>(no EF migrations)" .-> DB
```

---

## 3. Use cases

Mermaid has no use-case diagram, so actors are on the left and use cases are grouped by module.
"Staff" means any institution member; what a staff member can do depends on their role's permissions
(section 4).

```mermaid
flowchart LR
    V(["Visitor"])
    D(["Donor"])
    AP(["Applicant"])
    ST(["Institution staff<br/>(Owner + roles)"])
    PA(["Platform admin"])
    SYS(["System<br/>(Hangfire jobs)"])

    subgraph Access
        UC1["Sign up with phone OTP"]
        UC2["Sign in / step-up MFA"]
        UC3["Switch institution"]
    end

    subgraph Discovery
        UC4["Search institutions<br/>(text + near me)"]
        UC5["View public page,<br/>calendar, needs"]
    end

    subgraph Bookings
        UC6["Book a meal slot"]
        UC7["Book every year /<br/>Memorial Dana Planner"]
        UC8["Manage slots, seasons,<br/>blocked dates"]
        UC9["Approve / reject booking"]
        UC10["Today's sponsors,<br/>mark served"]
    end

    subgraph Onboarding
        UC11["Submit registration<br/>+ documents"]
        UC12["Review, checklist,<br/>clarify, approve / reject"]
    end

    subgraph Giving
        UC13["Donate / pledge to a need"]
        UC14["Record + void receipts<br/>(maker-checker)"]
        UC15["Monthly report + CSV"]
    end

    subgraph Services["Religious services"]
        UC16["Request a service"]
        UC17["Assign leader,<br/>accept / decline"]
    end

    subgraph Admin
        UC18["Manage roles<br/>and permissions"]
        UC19["Platform support<br/>into an institution"]
    end

    subgraph Automation
        UC20["Send reminders<br/>(SMS / push)"]
        UC21["Generate yearly<br/>bookings"]
        UC22["Process outbox"]
    end

    V --> UC4 & UC5 & UC1
    D --> UC2 & UC4 & UC5 & UC6 & UC7 & UC13 & UC16
    AP --> UC11
    ST --> UC2 & UC3 & UC8 & UC9 & UC10 & UC14 & UC15 & UC17 & UC18
    PA --> UC2 & UC12 & UC19 & UC18
    SYS --> UC20 & UC21 & UC22
```

---

## 4. Security model — RBAC and Row-Level Security

### 4a. How an endpoint decides "allowed"

Effective permissions come from one SQL view, `auth.vw_EffectivePermissions`, and are cached in Redis.
The cache key holds two version numbers, so bumping either one makes the old entry unreachable.

```mermaid
flowchart TB
    REQ["Request with JWT<br/>(UserId, InstitutionId, IsPlatform)"] --> POL["[HasPermission('BOOKINGS.CALENDAR.APPROVE')]<br/>PermissionAuthorizationHandler"]
    POL --> VER["Read Users.PermissionsVersion<br/>+ Institutions.PermissionsVersion"]
    VER --> KEY["key = perm:{userId}:{institutionId}:v{u}.{i}"]
    KEY --> HIT{"In Redis?"}
    HIT -- yes --> SET["Permission set"]
    HIT -- no --> VIEW["auth.vw_EffectivePermissions"]
    VIEW --> CACHE["Write to Redis (12 h)"] --> SET

    subgraph VIEWLOGIC["Inside the view"]
        direction TB
        G1["Role grants<br/>(active role, valid dates)"] --> CEIL["∩ role-level ceiling"]
        G2["Grant overrides"]
        G3["Platform support<br/>(PLATFORM.INSTITUTIONS.EXECUTE)"]
        CEIL & G2 & G3 --> UNION["Union"]
        UNION --> MOD["∩ modules the institution has enabled"]
        MOD --> ACT["only active users,<br/>permissions and screens"]
        ACT --> DENY["− deny overrides<br/>(deny wins)"]
    end
    VIEW -.- VIEWLOGIC

    SET --> HAS{"Holds the code?"}
    HAS -- no --> F403["403 Forbidden"]
    HAS -- yes --> MFA{"RequiresMfa?"}
    MFA -- "yes, no step-up grant" --> STEP["403 stepup_required<br/>(SMS code, then retry)"]
    MFA -- "no / step-up done" --> OK["Handler runs"]
```

Source: `Api/Security/PermissionAuthorizationHandler.cs`, `Infrastructure/Security/PermissionService.cs`,
`Database/auth/Views/vw_EffectivePermissions.sql`.

### 4b. Row-Level Security on every connection

Even when a handler's query forgets a tenant filter, SQL Server filters the rows itself.

```mermaid
sequenceDiagram
    autonumber
    participant H as Handler
    participant I as SessionContextConnectionInterceptor<br/>(EF Core) / Dapper factory
    participant S as SQL Server
    participant P as sec.TenantIsolationPolicy

    H->>I: open connection
    I->>S: sp_set_session_context UserId, InstitutionId, IsPlatform<br/>(@read_only = 1)
    Note over S: read_only: later code on this<br/>connection cannot change them
    H->>S: SELECT ... FROM booking.Bookings
    S->>P: filter predicate sec.fn_Tenant(InstitutionId)
    P-->>S: row visible if IsPlatform = 1<br/>OR row.InstitutionId = session InstitutionId
    S-->>H: only this tenant's rows
    H->>S: INSERT into another tenant
    P-->>S: block predicate fails
    S-->>H: error, nothing written
```

Background jobs open their connections with `IsPlatform = 1` (`Infrastructure/Jobs/JobScopes.cs`).
Source: `Infrastructure/Persistence/SessionContext.cs`, `Database/sec/Functions/fn_Tenant.sql`,
`Database/sec/SecurityPolicies/TenantIsolationPolicy.sql`.

---

## 5. Booking a meal (the booking engine)

Three layers stop overbooking: the Redis lock, the conditional `UPDATE`, and the `CHECK` constraints.
The booking and its outbox messages are saved in one transaction; SMS, push and the live calendar
update happen afterwards, from the outbox.

```mermaid
sequenceDiagram
    autonumber
    actor D as Donor
    participant E as POST /api/bookings
    participant H as BookMealHandler
    participant R as Redis
    participant S as SQL Server
    participant J as Hangfire OutboxJob
    participant N as SMS / Push / SignalR

    D->>E: slot PublicId, date, headcount, fulfilment
    E->>H: BookAsync(donorId, command)
    H->>S: donor has a confirmed phone? slot PublicId → Id
    H->>R: SET lock:slot:{mealSlotId}:{date} NX PX 10s
    alt lock held by another request
        R-->>H: busy
        H-->>D: 409 slot_busy
    end
    H->>S: BEGIN TRANSACTION
    H->>S: read slot, seasons, blocked dates, institution status
    Note over H: MealSlotRules.CanBook(...)<br/>closed day / past cut-off → refused
    H->>S: get or create SlotInstance (one row per slot per date)
    H->>S: UPDATE SlotInstances SET BookedSponsors += 1 ...<br/>WHERE BookedSponsors < MaxSponsors<br/>AND BookedHeadcount + n <= MaxHeadcount
    alt 0 rows updated (or CHECK constraint refuses)
        S-->>H: full
        H-->>D: 409 slot_full (transaction rolls back)
    end
    H->>S: next BookingNo from core.NumberSeries (UPDLOCK)
    H->>S: INSERT Booking (Requested or Approved)<br/>+ OutboxMessage BookingRequested / BookingConfirmed<br/>+ OutboxMessage AvailabilityChanged
    alt donor already holds this slot-day (UX_Bookings_DonorPerSlot)
        S-->>H: duplicate key
        H-->>D: 409 already_booked (place given back by rollback)
    end
    H->>S: COMMIT
    H->>R: release lock (only if our token)
    H-->>D: 201 BookingNo + status
    E->>J: wake the outbox now (the minute's run is the guarantee)
    J->>S: claim batch WITH (UPDLOCK, READPAST, ROWLOCK)
    J->>N: SMS / push to donor and staff (DedupKey: never twice)<br/>SignalR to group of the institution's slug
    J->>S: mark processed
```

If Redis is down the booking continues without the lock: layers 2 and 3 alone still make overbooking
impossible. Source: `Application/Bookings/Engine/BookMealHandler.cs`,
`Infrastructure/Caching/RedisSlotLocks.cs`, `Infrastructure/Persistence/SqlSlotInventory.cs`,
`Infrastructure/Messaging/OutboxProcessor.cs`.

---

## 6. Session refresh — rotation with reuse detection

Each refresh token can be spent once. If a spent token comes back more than 10 seconds after it was
rotated, two parties hold it, so the whole family is revoked. Every dead session gets the same
answer (`401 session_expired`), so a replayed copy learns nothing about why it failed.

```mermaid
sequenceDiagram
    autonumber
    participant C as Client (web cookie / mobile body)
    participant E as POST /api/auth/session/refresh
    participant H as SessionHandler
    participant S as SQL Server

    C->>E: refresh token
    E->>H: RefreshAsync
    H->>S: find auth.RefreshTokens by SHA-256 hash
    alt unknown or expired
        H-->>C: 401 session_expired
    else already rotated, more than 10 s ago
        H->>S: revoke whole family (ReuseDetected)
        H-->>C: 401 session_expired — sign in again
    else already rotated, within 10 s (two tabs)
        H-->>C: 409 session_superseded — family survives
    end
    H->>S: user active? SecurityStamp unchanged?
    alt suspended / password changed
        H->>S: revoke family
        H-->>C: 401 session_expired
    end
    Note over H: re-check institution membership<br/>(a revoked role drops out of the new token)
    H->>S: BEGIN TRANSACTION
    H->>S: UPDATE RefreshTokens SET RevokedAt, 'Rotated'<br/>WHERE Id = @id AND RevokedAt IS NULL
    alt 0 rows (another request won the race)
        H-->>C: 409 session_superseded
    end
    H->>S: INSERT successor (same FamilyId), link ReplacedByTokenId
    H->>S: COMMIT
    H-->>C: new access JWT + new refresh token
```

Source: `Application/Access/Session/SessionHandler.cs`, `Api/Endpoints/SessionEndpoints.cs`,
`Api/Endpoints/MobileSessionEndpoints.cs`.

---

## 7. Institution onboarding

### 7a. Registration request states

```mermaid
stateDiagram-v2
    [*] --> Draft: applicant signs up (phone OTP)
    Draft --> Submitted: legal details, office bearers,<br/>documents, declaration
    Submitted --> UnderReview: platform admin starts review
    UnderReview --> InfoRequested: admin asks questions
    InfoRequested --> UnderReview: applicant answers all
    UnderReview --> Approved: checklist + mandatory documents verified
    UnderReview --> Rejected: reason from REJECT_REASON
    Draft --> Withdrawn
    Submitted --> Withdrawn
    InfoRequested --> Withdrawn
    Approved --> [*]
    Rejected --> [*]
    Withdrawn --> [*]
```

### 7b. Approve — one transaction

```mermaid
sequenceDiagram
    autonumber
    actor A as Platform admin
    participant H as ApproveRegistrationHandler
    participant S as SQL Server

    A->>H: POST /api/admin/registrations/{requestNo}/approve<br/>(ONBOARDING.REQUESTS.APPROVE, RequiresMfa)
    H->>S: BEGIN TRANSACTION
    H->>S: decision locks (request, slug, owner)
    H->>S: load request; mandatory documents + checklist done?<br/>decider ≠ applicant?
    alt not ready
        H-->>A: error, nothing written
    end
    H->>S: InstitutionCode from core.NumberSeries + free slug
    H->>S: INSERT org.Institutions (Active)
    H->>S: copy verified documents → InstitutionDocuments
    H->>S: enable modules (type defaults + plan)
    H->>S: clone template roles + their permissions
    H->>S: applicant → member (IsOwner) + OWNER role<br/>bump Users.PermissionsVersion
    H->>S: request → Approved; status histories
    H->>S: audit log row (ids only, no personal data)
    H->>S: OutboxMessage InstitutionApproved
    H->>S: COMMIT (any failure rolls back all, number included)
    H-->>A: InstitutionCode + slug
```

Source: `Application/Onboarding/Approval/ApproveRegistrationHandler.cs`, `Domain/Onboarding/RegistrationRequest.cs`.
