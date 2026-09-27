# Plan 01 — Authentication, MFA step-up, i18n foundation, design system

Branch: `2-authentication-and-i18n-foundation` (cut from `1-permission-engine-frontend-authorization-setup`,
because every slice below depends on `IPermissionService` and `[HasPermission]`, which live only on that branch).

Status: **in progress, and blocked.** In slice 5.1 tokens and DI wiring are done; the signup and
sign-in handlers and the endpoints are not, and both handlers wait on **Q6** (§3) — signup also on
Q4 and on the Donor role. 5.2–5.8 are untouched apart from one 5.8 test.
**135 tests passing**, `dotnet format` clean, build clean with warnings as errors.
Last updated 2026-09-27. Update this line and the 5.1 boxes whenever either changes — a fresh
session trusts them and will rebuild anything they claim is missing.

Delivered so far, with the commit that added it:

| Commit | What landed |
|---|---|
| `1ca0b65` | Application abstractions: `ISmsSender`, `IOtpService`, `IPasswordPolicy`, `IPhoneNumberNormalizer`, `OtpPurpose`, `OtpSendResult`, `OtpVerifyResult`, `PasswordValidationResult`. Plus the two packages from A1 and A8. |
| `a06f829` | A1 confirmed, and the three responsibilities it transfers from `UserManager` written down (see A1). |
| `2a68f4f` | `PhoneNumberNormalizer` — 6 tests. |
| `03ad08c` | `SethikaPasswordPolicy` — 17 tests. |
| `1dc4aec` | `OtpService` + `OtpCacheKey` + `OtpOptions`, and `RedisFixture` for the tests — 14 tests. |
| `f6f4c4a` | `SmsSenderBase`, `ConsoleSmsSender`, `NotifyLkSmsSender`, `SmsOptions` — 8 tests. |
| `6201b6d` | `ITokenService`, `TokenSubject`, `IssuedTokens`; `JwtTokenService` + `JwtOptions`. `SethikaJwt` and `SethikaClaimTypes` moved from `Api/Security` to `Infrastructure/Security`, and `SethikaJwt.CreateSigningKey` is now used by `Program.cs` too. `Microsoft.IdentityModel.JsonWebTokens` referenced from Infrastructure — 22 tests, 6 of them through the real host. |
| `df18505` | `AddSethikaAccess`, called from `Program.cs`: every service above registered, options bound, the SMS sender chosen by `Sms:Provider` and the host refusing to start with the console sender or code logging outside Development. Lockout 5 → 15 min; hasher at 210 000 iterations (see A1) — 8 tests, all booting the real host. |

All of the above is registered in DI and resolvable from the running host (`df18505`), but no
endpoint calls any of it yet — that happens with the endpoints at the end of 5.1.

---

## 0. How to resume this from a cold start

```bash
git checkout 2-authentication-and-i18n-foundation
cat docs/plans/01-AUTH.md          # this file
cat CLAUDE.md docs/DECISIONS.md    # the rules this plan must obey
dotnet build Sethika.sln && dotnet test Sethika.sln   # the count in the status line must pass before you add anything
```

Then read §1 (what already exists — do not rebuild it), §2 (decisions and why), §3 (open questions that
block work), and start at the first unchecked box in §5.

Rules that override any instinct: `src/Sethika.Database` is the only place schema changes are made
(no EF migrations, ever), every endpoint carries `[HasPermission]`, permission decisions are never
re-derived in C#, and secrets live only in User Secrets.

---

## 1. What already exists (do not rebuild)

### Database — all of this is already in the SQL project and published

| Table | What it is for | Rows today |
|---|---|---|
| `auth.Users` | password hash, security stamp, `PermissionsVersion`, lockout, `PreferredLanguage`, `MfaRequired`, masked Email/Phone/NIC | 2 (test data) |
| `auth.RefreshTokens` | `TokenHash CHAR(64)`, `FamilyId`, `JwtId`, `ActiveInstitutionId`, `ReplacedByTokenId`, `RevokedReason` | 0 |
| `auth.PasswordHistory` | reuse blocking | 0 |
| `auth.LoginHistory` | success/failure, `FailureReason`, `MfaUsed`, IP, UA | 0 |
| `auth.UserMfaMethods` | `TOTP`/`SMS`/`Email`, `SecretEncrypted VARBINARY(512)` | 0 |
| `auth.RecoveryCodes` | hashed single-use backup codes | 0 |
| `auth.UserConsents` → `core.LegalDocuments` | versioned consent with `ContentSha256` | 0 |
| `auth.Portals/Modules/Screens/ScreenElements/Permissions/PermissionActions/PermissionDependencies` | RBAC catalogue | **0 except Portals(4) and PermissionActions(10)** |
| `auth.Roles / RolePermissions / UserRoles / UserPermissionOverrides` | assignments, all four temporal | 0 |
| `auth.vw_EffectivePermissions` | deny-wins + module-enabled intersection, in SQL | — |
| `org.InstitutionMembers`, `org.StaffInvitations` | membership, invites | 0 |

The refresh-token schema was already designed for rotation-with-breach-detection: `FamilyId` +
`ReplacedByTokenId` + `RevokedReason` is exactly the shape needed, so §5.2 is implementing a design
that is already in the tables, not inventing one.

### Backend code

- `SessionContext` / `SessionContextConnectionInterceptor` / `SqlConnectionFactory` — stamp
  `UserId`/`InstitutionId`/`IsPlatform` with `@read_only = 1` on every connection open. Both EF and Dapper.
- `PermissionService` (Dapper → view, Redis cache, versions in the key), `PermissionCacheKey`,
  `PermissionSet` (`Allows`, `RequiresMfa`).
- `HasPermissionAttribute` / `PermissionPolicyProvider` / `PermissionAuthorizationHandler` /
  `PermissionRequirement` / `HttpCurrentUserContext` in `Api/Security`. `SethikaClaimTypes` and
  `SethikaJwt` were here too and now live in `Infrastructure/Security`, beside the token service
  that writes them (`6201b6d`).
- `Program.cs` — JWT bearer validation is **already configured and working**. What is missing is
  anything that *issues* a token.
- Packages: `Dapper`, `Microsoft.EntityFrameworkCore.SqlServer`, `StackExchange.Redis`,
  `Microsoft.AspNetCore.Authentication.JwtBearer`. Nothing else.

### Frontend

`web/` is a bare Next.js 16 + React 19 + Tailwind 4 app plus the permission plumbing
(`lib/permissions/*`, `components/permissions/*`) and brand tokens in `app/globals.css`.
**No auth pages, no i18n, no component library, no fonts wired.**

### Tests

50 passing: `Sethika.Application.Tests` (13), `Sethika.Api.IntegrationTests` (36, incl. 8 RLS and
9 permission-engine tests against real SQL Server + Redis), 1 dependency-rule test per project.
Integration tests share one `SethikaTestDatabase` collection fixture with parallelisation disabled.

---

## 2. Decisions, with the reasoning

### A1 — ASP.NET Core Identity: take the primitives, not the stores *(confirmed 2026-09-27)*

CLAUDE.md's stack line says "ASP.NET Core Identity + JWT". Full Identity means `UserManager<T>` over
`IdentityDbContext`, or about eight hand-written store interfaces (`IUserStore`, `IUserPasswordStore`,
`IUserEmailStore`, `IUserPhoneNumberStore`, `IUserLockoutStore`, `IUserSecurityStampStore`,
`IUserRoleStore`, `IUserTwoFactorStore`) mapped onto columns EF Core already maps.

`auth.Users` is not Identity's schema and cannot become it — the SQL project is the source of truth and
the table is referenced by masked columns, RLS-adjacent FKs and temporal role tables.

**Decision:** reference `Microsoft.Extensions.Identity.Core` (small; not the EF Core Identity package)
and use exactly three things from it — `IPasswordHasher<User>` / `PasswordHasher<User>`
(PBKDF2-HMAC-SHA512, versioned hash format), `PasswordOptions` and `LockoutOptions`
for policy values. `SignInManager` is not used: it is cookie-oriented and we issue JWTs.
`UserManager`'s token providers are not used: OTP is in Redis and refresh tokens are ours.

**Iteration count — corrected 2026-09-27.** This section used to say Identity's hasher runs 210 000
iterations. It does not: Identity 10's default is **100 000**, which a test reading the count back out
of a real hash showed. 210 000 — OWASP's current figure for PBKDF2-HMAC-SHA512 — is set explicitly
through `PasswordHasherOptions` in `AddSethikaAccess` (`df18505`) and pinned by
`HashesPasswordsWithPbkdf2HmacSha512At210000Iterations`. Write the report from this paragraph.

Viva answer: *"Identity's password hashing and policy primitives over the project's own schema; the
full Identity stores were not adopted because the database schema is owned by the SQL project."*

`IUserRoleStore` is the specific interface that does not fit. Identity addresses a role by name
(`AddToRoleAsync(user, "Manager")`), while `auth.UserRoles` carries
`FK_UserRoles_RoleTenant (RoleId, InstitutionId) → auth.Roles(Id, InstitutionId)` — the composite FK
that stops one institution's role being assigned inside another. Identity's signature has nowhere to
put the institution, so implementing it would mean either throwing or guessing a tenant. Roles are
therefore assigned through EF Core with the institution explicit.

**Password hashing and the lockout policy values are identical either way** — same PBKDF2-HMAC-SHA512,
same configurable iteration count, same versioned hash format — so this choice costs nothing in password
security. What it does cost is three things `UserManager` would otherwise do for free. Each is
now ours to do, so each gets one home in the code and one test:

| Identity would do it | Ours | If it is forgotten |
|---|---|---|
| Rotate `SecurityStamp` on password change, MFA change or role change | `IUserSecurityService.RotateStampAsync`, called from every such path | Old refresh tokens keep working after a password change, so "log out everywhere" silently does nothing — the one control a user reaches for after a compromise |
| Reset `AccessFailedCount` to 0 after a successful sign-in | The sign-in handler, in the same transaction as the `LastLoginAt` write | Failures accumulate across sessions until a legitimate user with the correct password is locked out |
| Maintain `NormalizedEmail` on every write | A single `Normalize` helper used by signup, email change and lookup | `UX_Users_NormalizedEmail` stops catching duplicates, so `Test@x.com` and `test@x.com` become two accounts |

These are not hypothetical: all three are ordinary omissions in hand-rolled auth, which is exactly
why they are written down here instead of being left to memory. §5.8 carries a test for each.

### A2 — Both tokens, and where they live

| Token | Lifetime | Stored | Why |
|---|---|---|---|
| Access (JWT) | 15 min | **JS memory only** — never `localStorage` | XSS can read it, but only for 15 min and cannot mint a new one |
| Refresh | 14 days, rotating | **httpOnly cookie**, `Path=/api/auth/refresh`, `SameSite=Lax`, `Secure` in prod | JS cannot read it at all |

Consequence: on page reload the access token is gone, so the app calls `/api/auth/refresh` once at
boot (silent refresh) before rendering anything that depends on identity.

The refresh endpoint is the only cookie-authenticated, state-changing endpoint, so it is the only one
that needs CSRF protection → **double-submit token**: a non-httpOnly `sethika-csrf` cookie whose value
must be echoed in an `X-CSRF-Token` header. Every other endpoint is Bearer-authenticated and therefore
not reachable by CSRF at all.

`SecurityStamp` is *not* checked per request — that would put a DB read in front of every call and
defeat a stateless JWT. It is checked on refresh. The honest consequence: revoking a user leaves at
most a 15-minute window on their current access token. Write that sentence in the report.

### A3 — The frontend talks to the API through a Next.js rewrite, not cross-origin

`next.config.ts` gets `rewrites()` mapping `/api/*` → `NEXT_PUBLIC_API_BASE_URL`. The browser then only
ever sees one origin, so the refresh cookie is same-site (`SameSite=Lax` works, no `SameSite=None`, no
`Secure`-over-http problem in dev) and there is no CORS configuration to get wrong.

This must be decided now: doing it later means changing every fetch call and re-testing the cookie.

### A4 — OTP lives in Redis, never in SQL

`otp:{purpose}:{e164}` as a Redis HASH, TTL 5 min, fields: `h` = SHA-256 of the code, `att` = attempt
count. Deleted on success.

- The **hash**, not the code — a Redis dump must not reveal live codes.
- `{purpose}` is one of `signup | login | stepup | reset` and is part of the key, so a signup OTP cannot
  be replayed as an MFA step-up. This is the detail that turns OTP from a feature into a control.
- No SQL table means no PII retention and no cleanup job. The audit trail is
  `auth.LoginHistory` + `auth.Users.PhoneConfirmed`, which is what an auditor actually asks for.
- Cost control, because SMS is real money: 60 s resend cooldown, 3 sends per phone per hour,
  5 verify attempts per code, then the code is burned.
- Accepted consequence: **Redis down means signup and step-up are unavailable.** Password login still
  works. That is the right failure mode, and it is deliberately the opposite of the permission cache,
  which degrades to SQL.

### A5 — MFA step-up grant lives in Redis too

`stepup:{userId}:{jti}`, TTL 15 min, set after a successful step-up verification.
`PermissionAuthorizationHandler` gains: if `effective.RequiresMfa(code)` and no step-up key exists for
this token's `jti`, fail with `403` and a body telling the client to step up.

Why not a claim: putting `amr: ["mfa"]` in the token means re-issuing the access token mid-flow and the
client swapping it. Redis lets the grant be handed out without touching the token, and it expires by
itself. Binding it to `jti` means a refreshed token does not silently inherit the step-up.

### A6 — Step-up is SMS OTP in this plan; TOTP is a later slice

Sri Lankan users have phones, not authenticator apps. SMS step-up reuses the whole of A4 and costs
about thirty lines. `auth.UserMfaMethods` and `auth.RecoveryCodes` already exist, so TOTP enrollment can
be added later without a schema change. RFC 6238 is ~40 lines and its Appendix B test vectors make
excellent unit tests — but it is not what gets a Sri Lankan institution owner through the door.

### A7 — A user's institution list comes from `auth.UserRoles`, not `org.InstitutionMembers`

`org.InstitutionMembers` has an RLS **filter** predicate (`sec.fn_Tenant(InstitutionId)`). At login the
session has no `InstitutionId` yet, so that table returns **zero rows** — the classic chicken-and-egg.

Avoid it entirely: `auth.UserRoles` has no RLS predicate and already carries
`(UserId, RoleId, InstitutionId)`, and `org.Institutions` has no filter predicate either. So
"which institutions may I act for" is `auth.UserRoles` (active, inside its validity window) joined to
`org.Institutions` for `Name` / `Slug` / `Status`.

The alternative — a privileged `IsPlatform = 1` connection — would work but creates a general RLS
escape hatch inside the auth module, and every later reviewer would have to re-prove it is still
narrow. Do not add one.

### A8 — Phone numbers are normalised to E.164 before they touch the database

`UX_Users_Phone` is unique on `PhoneNumber`. `0771234567`, `+94771234567` and `94771234567` are the
same human but three different strings, so without normalisation the unique index prevents nothing and
one person ends up with three accounts.

**New package, needs your nod:** `libphonenumber-csharp` (namespace `PhoneNumbers`). A hand-rolled LK
regex would do for local numbers, but `auth.DonorProfiles.CountryCode` exists precisely because
diaspora donors are expected, and getting international parsing right by hand is not a good use of the
month.

Store E.164 (`+94771234567`). Display through the mask (`partial(4, "XXXXX", 3)`) or format client-side.

### A9 — `Guid.CreateVersion7()` for new user ids, generated in C# rather than by the database

`auth.Users.Id` defaults to `NEWSEQUENTIALID()`, but signup must write `Users`, `UserRoles`,
`DonorProfiles`, `UserConsents` and an outbox row in **one transaction**, which needs the id before the
insert. Generating it in C# is the simple path.

Honest trade-off: SQL Server compares `uniqueidentifier` by its last six bytes first, so v7's
big-endian timestamp prefix does **not** give the clustered-index locality `NEWSEQUENTIALID` does. On a
table of thousands of users this is irrelevant; it would matter at millions. Say so in the report rather
than pretending v7 is strictly better.

### A10 — Fonts: Outfit has no Sinhala or Tamil glyphs

Left alone, every Sinhala and Tamil string silently falls back to a system font and the "unique UI"
looks broken in exactly the two languages the proposal now promises. Load Noto Sans Sinhala and Noto
Sans Tamil next to Outfit via `next/font`, with the CSS `font-family` stack ordered so Latin still
resolves to Outfit. Check this on the login screen in all three languages before calling any slice done.

### A11 — `localePrefix: 'as-needed'`, decided now because it is a routing decision

`next-intl` with `en` unprefixed (`/login`) and `si` / `ta` prefixed (`/si/login`, `/ta/login`).
Prefixed routes are what make Phase B's public institution pages indexable per language. Changing this
after the screens exist means touching every route and every link.

Locale resolution order: signed-in user's `auth.Users.PreferredLanguage` → `NEXT_LOCALE` cookie →
`Accept-Language` → `en`.

### A12 — Rate limiting is Redis, not the in-process limiter

`Microsoft.AspNetCore.RateLimiting` is per-instance. CLAUDE.md lists Redis rate limiting as a genuine
Redis use and the course requires Redis be used for real. `INCR` + `EXPIRE` fixed window is three
lines; the boundary-burst weakness is acceptable here and the upgrade path (sorted-set sliding window
log) is one method. Note the weakness in the report instead of hiding it.

Keys: `rl:otp:send:{e164}`, `rl:otp:verify:{e164}`, `rl:login:{normalizedLogin}`, `rl:login:ip:{ip}`,
`rl:refresh:ip:{ip}`.

### A13 — Data Protection keys must be persisted before production

TOTP secrets (`SecretEncrypted`) and any protected payload are encrypted with the Data Protection API.
On Azure App Service the default key ring is **not** persisted across restarts or scale-out, which makes
every stored secret undecryptable. Production needs keys in Azure Blob Storage protected by Key Vault.
Local dev's file-based ring is fine. This is a deployment prerequisite, recorded here so it is not
discovered after real data exists.

### A14 — SMS: `ISmsSender` with a console sender for development, a real gateway before deployment *(decided 2026-09-27)*

Development uses `ConsoleSmsSender`, which writes the OTP to the log instead of sending it. Signup
therefore works end to end from day one, costs nothing, and integration tests can assert on a real
code without a network call or a provider account.

A gateway **is** required before this is deployed anywhere real, so the abstraction is built for two
implementations from the start, not retrofitted:

- `ISmsSender` lives in Application and exposes one method. Nothing above Infrastructure knows a
  provider exists.
- The implementation is chosen by configuration (`Sms:Provider` = `Console` | `NotifyLk`), and the
  API **refuses to start with `Console` selected outside Development** — a silent console sender in
  production means no user ever receives an OTP and login appears simply broken.
- Every send, whichever sender is used, writes `notify.Notifications` with `Channel = 'SMS'`,
  `SmsSegments` and `CostLkr`. So the delivery log, the dedup key and the per-plan SMS cost tracking
  are exercised by the console sender too, and switching providers changes one class and no data path.
- Provider credentials go in User Secrets locally and host configuration in Azure. Never in
  `appsettings.json`.

Deployment prerequisite, alongside A13: pick and fund a provider (Notify.lk, Text.lk or Dialog),
register the sender ID, and set `Sms:Provider` + the credentials. Sri Lankan gateways require a
registered alphanumeric sender ID, and approval takes days — so start that paperwork before the
week it is needed, not during it.

---

## 3. Open questions

Q1 and Q2 are decided and Q3 is done. **Q6 and Q4 block the rest of 5.1**; Q5 blocks 5.5.

| # | Question | Blocks | Default if you say nothing |
|---|---|---|---|
| ~~Q1~~ | ~~SMS provider~~ — **decided 2026-09-27: console sender for development, real provider behind the same interface before deployment. See A14.** | — | — |
| ~~Q2~~ | ~~A1 — Identity primitives instead of full Identity?~~ — **confirmed 2026-09-27. See A1, including the three responsibilities this transfers to us.** | — | — |
| ~~Q3~~ | ~~**A8** — add `libphonenumber-csharp`?~~ — **done: installed in `1ca0b65`.** | — | — |
| Q4 | **Legal text.** `core.LegalDocuments` is empty, and signup must record consent against a real version plus SHA-256 (`auth.UserConsents.LegalDocumentId` is a NOT NULL FK, so there is no consent row without a document row). Do you have Terms / Privacy text, or should a clearly-marked `0.1-draft` be seeded? | `SignUpDonor` in 5.1, §5.5 | Seed `0.1-draft` in en/si/ta, marked as draft, with a TODO. Consent rows point at the draft version, which is honest and replaceable. |
| Q6 | **Where command handlers get EF Core.** 5.1 puts `SignUpDonor` and `SignIn` in `Sethika.Application` and says EF Core, but Application does not reference EF Core and `SethikaDbContext` is in Infrastructure. These are the codebase's first command handlers, so the answer becomes the pattern for Onboarding and Booking too — which is why it is a question and not a guess. | `SignUpDonor`, `SignIn`, every later command | **(a)** an `ISethikaDbContext` in Application exposing the `DbSet`s, `SaveChangesAsync` and a transaction, with Application referencing `Microsoft.EntityFrameworkCore` (the core package, no SQL Server provider). Least code, and the DbContext already is a unit of work; the cost is that Application knows it runs on EF. **(b)** repository and unit-of-work interfaces in Application with EF behind them — persistence-ignorant, but an interface per aggregate and a method per query shape. Recommend (a). |
| Q5 | **First platform admin.** No platform owner exists, and CLAUDE.md forbids business data in post-deployment. | §5.5 | A `dotnet run --project src/Sethika.Api -- bootstrap` command, idempotent, reading phone and password from User Secrets. Keeps the credential out of source control and out of the dacpac. |

---

## 4. Honest scope warning

The week-1 line in the original schedule was "auth + i18n + design system". That is three non-trivial
pieces. Realistic solo effort:

| Slice | Effort |
|---|---|
| §5.1 signup + OTP + login + lockout | ~2 days |
| §5.2 refresh rotation + breach detection + logout | ~1 day |
| §5.3 MFA step-up | ~0.5 day |
| §5.4 rate limiting | ~0.5 day |
| §5.5 RBAC catalogue seed + bootstrap admin | ~1 day |
| §5.6 i18n foundation | ~1 day |
| §5.7 design system + auth screens | ~2 days |
| §5.8 tests | woven through, plus ~1 day of its own |

That is **8–9 days, not 5.** Two options: run it as a 9-day slice and compress week 2, or ship
§5.1–§5.5 first (backend auth, provable by integration tests) and do §5.6–§5.7 alongside week 2's
screens. Prefer the second — the backend is what the report's security chapter is built on, and
untested auth is the one thing that cannot be caught up later.

---

## 5. Work breakdown

Each slice ends green: `dotnet build && dotnet test` and, from §5.6, `npm run lint && npm run build`.
One commit per slice, Conventional Commits (CI blocks the PR title otherwise).

### 5.1 — Signup, OTP, password login, lockout

- [x] `Sethika.Application/` — `ISmsSender`, `IOtpService`, `IPasswordPolicy`,
      `IPhoneNumberNormalizer`, and the result types. Interfaces only; no implementation here.
      `ITokenService` landed with `JwtTokenService` below.
- [x] `Sethika.Infrastructure/Security/OtpService.cs` — Redis HASH per A4; the code is 6 digits from
      `RandomNumberGenerator`; compare with `CryptographicOperations.FixedTimeEquals`.
- [x] `Sethika.Infrastructure/Notifications/ConsoleSmsSender.cs` and `NotifyLkSmsSender.cs` (per Q1),
      selected by configuration. Every send also writes `notify.Notifications`, so the audit trail and
      the SMS cost column are populated from day one. The configuration switch landed with the
      DI wiring (`df18505`).
- [x] `Sethika.Infrastructure/Security/PhoneNumberNormalizer.cs` — E.164, default region `LK` (A8).
- [x] `Sethika.Infrastructure/Security/SethikaPasswordPolicy.cs` — Identity's `PasswordOptions`, plus a
      128-character cap and a ban on the password containing the user's own contact details.
- [x] `Sethika.Infrastructure/Security/JwtTokenService.cs` — access token carrying
      `sub` / `jti` / `sethika:uid` / `sethika:inst` / `sethika:platform`, 15 min, plus the refresh
      token (32 random bytes, base64url; only its SHA-256 hex is stored). Minting only: storing the
      hash, rotation and reuse detection belong to the flows in 5.1 and 5.2, inside their own
      transactions. `sethika:inst` is omitted, not 0, when the session acts for no institution.
- [ ] `Sethika.Application/Access/SignUpDonor` — one transaction: `Users` + `DonorProfiles` + the donor
      role in `UserRoles` + `UserConsents` + `core.OutboxMessages`. EF Core, per the commands-use-EF rule.
      **Blocked** on Q6 and Q4, and on the donor role itself: no `Scope = 'Donor'` row exists until
      5.5's `07_RbacCatalogue.sql`, so that one role has to be seeded before signup can be tested.
      Note for 5.5: `CK_Roles_ScopeInstitution` requires `IsTemplate = 0` for `Scope = 'Donor'`, so
      `Donor` is a global role, not one of the template roles listed there.
- [ ] `Sethika.Application/Access/SignIn` — **blocked on Q6.** Verify the hash, apply the lockout policy
      (5 failures → 15 min), write `auth.LoginHistory` on **both** outcomes.
- [x] DI wiring in `Sethika.Infrastructure/DependencyInjection.cs` (`AddSethikaAccess`) — bind `OtpOptions`,
      `SmsOptions` and `JwtOptions`, register `IOtpService`, `IPasswordPolicy`,
      `IPhoneNumberNormalizer`, `IPasswordHasher<User>`, `ITokenService` (singleton — it holds the
      signing credentials) and `TimeProvider.System`, and select the SMS sender from `Sms:Provider`.
      Per A14 the host **refuses to start** with the console sender outside Development (and with
      `Otp:LogCodesForDevelopment` on), and `NotifyLkSmsSender` gets its `HttpClient` from
      `IHttpClientFactory`. `Program.cs` still reads `Jwt:SigningKey` itself for bearer validation;
      both sides go through `SethikaJwt.CreateSigningKey`, and a host round-trip test guards the pair.
- [ ] Endpoints: `POST /api/auth/otp/request`, `/api/auth/otp/verify`, `/api/auth/signup`,
      `/api/auth/login`. `[AllowAnonymous]`, and each one rate-limited.

**User enumeration:** login must answer identically for "no such user" and "wrong password", *including
timing* — so when the user is not found, still verify the supplied password against a fixed dummy hash
before returning. A response that is 200 ms faster for unknown accounts is a working user-enumeration
oracle, and it is exactly the kind of thing an examiner asks about.

### 5.2 — Refresh rotation with breach detection

- [ ] `POST /api/auth/refresh` — read the httpOnly cookie, hash it, look the row up, and inside one
      transaction: check it is neither expired nor revoked, check `SecurityStamp` still matches, insert
      the successor row, set `ReplacedByTokenId` + `RevokedAt` + `RevokedReason = 'Rotated'` on the old
      row, return a new access token and set the new cookie.
- [ ] **Reuse detection:** a presented token that hashes to an already-revoked row means the token was
      stolen and replayed. Revoke the entire `FamilyId` (`RevokedReason = 'ReuseDetected'`) and force
      re-login. This is what `FamilyId` is in the schema for.
- [ ] `POST /api/auth/logout` — revoke this family, clear the cookie.
- [ ] `POST /api/auth/logout-all` — revoke every family for the user, rotate `SecurityStamp`.
- [ ] `POST /api/auth/switch-institution` — re-validate the requested `InstitutionId` against
      `auth.UserRoles` (A7); **never** trust the body. Rotate the refresh token with the new
      `ActiveInstitutionId` and issue a matching access token. A user who can name an institution id
      must not thereby gain it.
- [ ] Concurrency: the rotation transaction takes the row with `UPDLOCK, ROWLOCK`, so two parallel
      refreshes cannot both succeed. Prove it with a parallel-load test.

### 5.3 — MFA step-up

- [ ] `POST /api/auth/stepup/request` and `/verify` — reuse §5.1's OTP with `purpose = stepup`.
- [ ] On success, `SET stepup:{userId}:{jti}` with a 15-min TTL, and `MfaUsed = 1` in `auth.LoginHistory`.
- [ ] Extend `PermissionAuthorizationHandler`: `RequiresMfa(code)` and no step-up key means fail.
      Keep the existing shape — return without `Succeed`, do not call `Fail()`.
- [ ] `MfaRequired = 1` users (platform staff, institution owners) step up once per session before
      anything sensitive, not once per action.

### 5.4 — Rate limiting

- [ ] `IRateLimiter` in Application, `RedisRateLimiter` in Infrastructure (A12).
- [ ] An endpoint filter that applies it, keyed per route; `429` with `Retry-After`.
- [ ] Applied to OTP request/verify, login, refresh and signup.
- [ ] Redis down means **fail open with a logged warning**, not closed. A cache outage must not lock
      everyone out — the same principle already applied to the permission cache.

### 5.5 — RBAC catalogue seed and bootstrap admin

- [ ] `Scripts/Seed/07_RbacCatalogue.sql` — idempotent MERGEs for `auth.Modules`, `Screens`,
      `ScreenElements`, `Permissions`, covering only the screens that exist after this plan
      (Access/Auth, Institutions, a Bookings placeholder), plus `PermissionDependencies`
      (EDIT requires VIEW) and the template roles (`Owner`, `Manager`, `Staff`, `Donor`).
      This is reference data, so post-deployment is the correct home for it.
- [ ] `Scripts/Seed/08_LegalDocuments.sql` per Q4.
- [ ] The `bootstrap` command per Q5.
- [ ] After every seed change, re-run `SqlPackage /Action:Script` and confirm the output contains
      **only** the seed MERGEs and no schema statements (DECISIONS.md D10). Any `IN`, `BETWEEN` or
      unnamed CHECK added here breaks convergence.

### 5.6 — i18n foundation

- [ ] `next-intl`, `middleware.ts`, `messages/{en,si,ta}.json`, `localePrefix: 'as-needed'` (A11).
- [ ] `PUT /api/me/language` writing `auth.Users.PreferredLanguage`.
- [ ] `notify.Templates` seeded per language for the OTP SMS — a Sinhala-preferring user must receive
      the Sinhala OTP message, which is why `Templates` has a `Language` column.
- [ ] Fonts per A10. Verify the login screen in en, si and ta.
- [ ] Rule from here on: **no hard-coded user-facing string in any component.** Cheap now, expensive in
      week 3.

### 5.7 — Design system and auth screens

- [ ] `next/font` for Outfit, Fraunces and Noto Sans Sinhala/Tamil; the brand tokens already in
      `globals.css` become the Tailwind theme.
- [ ] Primitives on shadcn/ui, restyled to the Sethika palette — not default shadcn, which is the whole
      point of "unique UI": `Button`, `Input`, `Field`, `Label`, `Card`, `Sheet`, `Toast`, `Spinner`,
      `OtpInput`, `LanguageSwitcher`.
- [ ] Screens: `/signup` (phone → OTP → details → consent), `/login`, `/login/otp`, `/stepup`,
      `/select-institution`.
- [ ] `lib/auth/` — access token in memory (A2), silent refresh at boot, one fetch wrapper that
      refreshes once on `401` and retries, queueing concurrent calls so a page with five requests does
      not fire five refreshes.
- [ ] `next.config.ts` rewrites per A3.
- [ ] Faith-neutral imagery only. No religious symbol on a shared screen.

### 5.8 — Tests

Unit (`Sethika.Application.Tests`): password policy; the refresh-rotation state machine as a pure
function; OTP attempt counting; rate-limit window maths; E.164 normalisation, including `07…`, `+94…`
and `94…` collapsing to one value; step-up expiry.

Integration (`Sethika.Api.IntegrationTests`), against real SQL Server and Redis:

- [ ] signup → OTP → login → authenticated call, end to end
- [ ] wrong password five times means locked; the correct password during lockout is still refused
- [ ] unknown user and wrong password are indistinguishable in body, status **and** timing
- [ ] refresh rotates, and the old token stops working
- [ ] **replaying a rotated refresh token revokes the whole family** — the security test that matters most
- [x] a token signed with a different key gives 401; an expired one gives 401
      (`JwtTokenServiceEndpointTests`, with a 14-minute-old token accepted as the control)
- [ ] `sethika:platform` cannot be forged via header, query or body (extends the existing test)
- [ ] `switch-institution` to an institution the user holds no role in gives 403, and no token is issued
- [ ] a `RequiresMfa` permission gives 403 before step-up, 200 after, and 403 again once the TTL expires
- [ ] OTP: a wrong code five times burns it; a fourth send within the hour gives 429
- [ ] two parallel refreshes with the same token — exactly one succeeds
- [ ] signup writes exactly one outbox row, and rolls back all five tables when any part fails

The three responsibilities A1 transfers from `UserManager` to us, one test each — these guard
omissions, so each must be mutation-tested by removing the call and watching the test fail:

- [ ] changing a password rotates `SecurityStamp`, and every refresh token issued before it is refused
- [ ] a successful sign-in resets `AccessFailedCount`, so four failures then a success then four more
      failures does not lock the account
- [ ] signing up as `Test@x.com` and then `test@x.com` is rejected as a duplicate, not accepted as a
      second account

Every one of these must be mutation-tested: break the behaviour on purpose and confirm the test goes
red. Two vacuous-test incidents on this project were caught that way; assume nothing.

---

## 6. Definition of done

1. `dotnet build Sethika.sln` clean (warnings are errors).
2. `dotnet test Sethika.sln` green, five consecutive runs — this suite has had ordering flakiness before.
3. `dotnet format --verify-no-changes` clean.
4. `cd web && npm run lint && npm run build` clean.
5. `SqlPackage /Action:Script` against the published database shows seed MERGEs only, no schema statements.
6. A real browser walk-through: sign up with a phone, receive the OTP (console or SMS), set a password,
   log in, reload the page and stay logged in, switch to Sinhala and see Sinhala text in a Sinhala font,
   hit a `RequiresMfa` action and be asked to step up.
7. PR into `1-permission-engine-frontend-authorization-setup` (or `main`, if that merged first), title in
   Conventional Commits form, description filling `## What` / `## Why` / `## How to verify`.

## 7. What this plan deliberately does not do

Password reset by email (phone reset only for now), TOTP enrollment, recovery codes, staff-invitation
acceptance, impersonation (`auth.ImpersonationSessions` exists but is Phase-optional), role-management
screens and the anti-escalation check that belongs with them, and enforcing
`auth.PermissionDependencies` at grant time. All have tables already; none block week 2.
