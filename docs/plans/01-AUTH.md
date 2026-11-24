# Plan 01 — Authentication, MFA step-up, i18n foundation, design system

Branch: `2-authentication-and-i18n-foundation` (cut from `1-permission-engine-frontend-authorization-setup`,
because every slice below depends on `IPermissionService` and `[HasPermission]`, which live only on that branch).

Status: **in progress. The backend slices are done: 5.1–5.5 and 5.9** — signup, OTP, login,
lockout, refresh rotation with reuse detection, institution switching, logout, MFA step-up, rate
limiting, the RBAC catalogue with level ceilings, the `bootstrap` command, and custom roles with rank
anti-escalation, all over HTTP. **5.6 is done**: the web half (next-intl routing via `proxy.ts`,
`messages/{en,si,ta}.json`, fonts, brand theme; `730bd06`), `PUT /api/me/language` with the
LanguageSwitcher saving to it (`1006af8`, `dbc37ac`), and the OTP SMS from `notify.Templates` in the
user's language (`c89e634`, `1b97c75`). **5.7's primitives are done**
(`components/ui`, `/design` review page; `4309393`), and the design language and performance
budget every screen follows are in `docs/DESIGN.md` (`3fcb41c`). **5.7's `lib/auth/` and rewrite
boxes are done** (`58914a6`: session client, fetch wrapper, `/api` rewrite, permissions loaded in the
browser; 16 Vitest tests in `web/`), but checked only against its contracts in unit tests: the live
walk-through with the API is still owed (see the 5.7 boxes). **5.7's screens are done** (`4c82ac0`:
`/signup`, `/login`, `/stepup` and its dialog, `/select-institution`; 32 Vitest tests).
**Six cloud branches merged on 2026-10-04/05** (`cloud/*`, merge commits up to `adf903f`): the
A19 grace-after-logout fix (5.2), `PUT /api/me/language` and `notify.Templates` (5.6, done), the
platform-claim forgery and OTP-over-HTTP tests (5.8), password change (A21, 5.8 done), the password
reset (5.10) and sign-in by SMS code (A22, `/login/otp`). **5.8 is done.** 5.10's API is done, its last
three mutations killed on 2026-10-11; its screens and the other 5.7 screens still owe one live
walk-through in a browser (an HTTP-level one through the rewrite passed on 2026-10-04).
**A review of the whole slice on 2026-10-11 (§8) fixed six gaps** - SMS sign-in for `MfaRequired`
accounts, a lockout that a right guess in a parallel burst could clear, OTP codes stored without
their expiry in one step, unthrottled logout, password history, and A11's read-back of
`PreferredLanguage` - each with tests and every mutation killed.
**367 tests passing** (Domain 1, Application 15, integration 351, against SQL Server 2022 and
Redis), 84 Vitest tests, `dotnet format` clean, build clean with warnings as errors, `next build`
clean.
Last updated 2026-10-11. Update this line and the 5.1 boxes whenever either changes — a fresh
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
| `58fa000` | `ISethikaDbContext` (A16) and `AddSethikaApplication`; `SignInHandler` with `DummyPasswordHash`, atomic lockout counting, one transaction per outcome, rehash on sign-in, institution list from `UserRoles` (A7); `LoginAttempt` and `RefreshToken` entities — 20 tests. |
| `bc069dd` | `auth.RoleLevels`, `auth.Roles.RoleLevelId` with composite `FK_Roles_Level`; seeds `07_RoleLevels`, `08_SystemRoles` (DONOR), `09_LegalDocuments` (0.1-draft, en/si/ta). Published to `Sethika`; `Sethika_Test` recreated from the dacpac — 6 tests. |
| `d12394d` | Verification tickets (A17): `VerifyAsync` issues one, `RedeemTicketAsync` spends it once via a Lua compare-and-delete — 5 tests. |
| `03bfca5` | `SignUpDonorHandler`: five tables in one save, ticket spent only after the form checks, duplicates reported only after it, consent by content hash; `DonorProfile`, `UserConsent`, `LegalDocument`, `OutboxMessage` entities — 10 tests. |
| `c59db76` | `IRateLimiter` + `RedisRateLimiter` (fixed window, fails open), `RateLimitKey` (hashed subjects), `RedisWindowCounter` (INCR and expiry in one Lua step). Fixes OtpService's send counter, which could outlive its window and block a phone for good — 8 tests. |
| `94b1c29` | The four auth endpoints and `GET /api/legal/current`, rate-limited per client and per identity (`AuthRateLimits`); A2 cookies; `{ "code" }` errors; `UseForwardedHeaders` (A18); `LoginName` shared by sign-in and its limit — 14 tests over HTTP. |
| `f4fabd8` | `SessionHandler`: rotation claimed by a conditional UPDATE, reuse detection with a 10-second grace (A19) whose refusal is `409` and leaves the cookies alone, stamp check, institution re-validated on every refresh; `SecurityStamps.RotateAsync`; `auth.RefreshTokens.SecurityStamp` (new column); session endpoints under `/api/auth/session` with CSRF (A2 amended) — 14 tests over HTTP. |
| `7c79832` | MFA step-up: `StepUpHandler` (code to the account's own confirmed phone, purpose `stepup`), `RedisStepUpGrants` (`stepup:{userId}:{jti}`, 15 min, fails closed), `PermissionAuthorizationHandler` refuses `RequiresMfa` without a grant, `StepUpAuthorizationResultHandler` answers `403 stepup_required`; A20 — 9 tests over HTTP, 7 handler unit tests. |
| `92b6187` | Level ceilings: `auth.RoleLevelPermissions` (+ history, temporal) with composite FKs and `CK_RoleLevelPermissions_Scope`, the ceiling in `vw_EffectivePermissions`; `10_RbacCatalogue.sql` (3 modules, 4 screens, 9 permissions, 6 dependencies, ceilings, OWNER/MANAGER/STAFF templates), guarded by THROW on any unknown code; the fixture uses the seeded catalogue — 9 tests. |
| `1739db6` | `bootstrap` (Q5): `BootstrapPlatformAdminHandler` + `BootstrapCommand`, `PLATFORM_ADMIN` system role in `08_SystemRoles.sql` — 10 tests, and the real `dotnet run -- bootstrap` smoke-tested. |
| `2a3208a` | Custom roles (5.9): `RoleManagementHandler` (create, set permissions, assign, revoke - rank, ceiling and holding checked in the transaction against the view), `RoleLevelQueries` (Dapper), `/api/institution/role-levels` and `/roles…` endpoints; read-only EF mappings for the catalogue, ceilings and the view — 16 tests over HTTP. |
| `730bd06` | Web i18n and design tokens (5.6 web half, 5.7 first box): `next-intl` 4.14 with `proxy.ts`, routes under `app/[locale]/`, `messages/{en,si,ta}.json` typed against `en.json`, `docs/translations-to-review.md`, `lib/fonts.ts` (Outfit, Fraunces italic, Noto Sans Sinhala/Tamil), the brand tokens as the Tailwind 4 theme, a placeholder `/login`. `npm run lint` and `npm run build` clean. |
| `4309393` | UI primitives (5.7, second box): `components/ui` — `Button`, `Input`, `Field`, `Label`, `Card`, `Sheet`, `Toast` (+ `useToast`, `Toaster` in the root layout), `Spinner`, `OtpInput`, `LanguageSwitcher` — on Radix via shadcn/ui conventions (`components.json`, `lib/utils.ts`), semantic colour tokens with measured contrast in `globals.css`, and a noindex `/[locale]/design` page showing every state. Packages: `radix-ui`, `class-variance-authority`, `clsx`, `tailwind-merge`. Checked in Chromium at 320px in en/si/ta: no horizontal overflow, no clipped text, every control ≥ 44px. |
| `3fcb41c` | Design language and load budget (refines 5.7's primitives): `docs/DESIGN.md` (bowl shapes, one focus glow, green-tinted depth, two type voices; usability rules; performance budget). English pages no longer fetch Noto fonts, Fraunces is one static weight, Radix Toast loads at idle, client providers ship only the namespaces they need (`i18n/pick.ts`). |
| `a2959d5` | Light and dark themes: follows the device by default, `ThemeSwitcher` (Auto / Light / Dark) saved in the `sethika_theme` cookie, applied before first paint by an inline `<head>` script and kept across locale changes by `ThemeSync`; components moved to semantic colour tokens with measured contrast in both themes. |
| `58914a6` | Browser session (5.7 `lib/auth/` and rewrites): `lib/auth/session-client.ts` (token in memory, silent refresh with CSRF, one fetch wrapper with single-flight refresh, A19 superseded retry, A20 step-up hook, logout / logout-all / switch-institution), `next.config.ts` rewrite to `API_BASE_URL`, permissions loaded client-side (`lib/permissions/server.ts` removed), root layout no longer dynamic so every locale's pages are prerendered. Vitest 4.1 in `web/` - 16 tests, each guarded rule mutation-checked. |
| `ba683fb` | `/api/me/*` moved from `Program.cs` into `Endpoints/MeEndpoints.cs`, one authenticated-only group. No behaviour change. |
| `2d8d2a4` | `InstitutionRoleEndpointsTests` logins get their own client address: from loopback they shared one login-client budget, and two full runs inside 15 minutes failed 11 tests with 429. |
| `1006af8` | `PUT /api/me/language` (5.6): `PreferredLanguageHandler` through `ISethikaDbContext`, tracked so `RowVersion` is in the UPDATE (one retry, then `409 concurrency_conflict`); `400 unsupported_language`; 204 and no write when unchanged — 7 tests over HTTP. |
| `dbc37ac` | `LanguageSwitcher` saves a signed-in user's choice through `savePreferredLanguage` (`lib/auth/api.ts`, session fetch wrapper, `keepalive`); a failure is logged only and never blocks the switch — 4 Vitest tests. |
| `c89e634` | `Scripts/Seed/11_NotifyTemplates.sql`: `OTP_SIGNUP` and `OTP_STEPUP`, SMS, en/si/ta, MERGE on (Code, Channel, Language), body compared as binary, `IsActive` left to operators — 3 tests. |
| `1b97c75` | `OtpService` reads its SMS from `notify.Templates` (Dapper, no cache); English fallback with a warning, and no send at all (error logged, `Unavailable`) when English is missing too. `BuildMessage` deleted — 6 tests, including PUT language → Sinhala step-up SMS end to end. |
| `4c82ac0` | Auth screens (5.7): `/signup` (phone → code → details → consent, every API error code mapped to a translated message where it can be fixed), `/login`, the step-up dialog wired to `lib/auth`'s hook plus `/stepup`, `/select-institution`; a session panel and a real `RequiresMfa` demo call on the home page. Prerendered per locale; checked at 320 px in en/si/ta against mocked responses shaped per the contracts. |
| `0e770a4` | A19 edge (5.2): a spent token presented within the grace window is `409 session_superseded` only while its family still has a live token (not revoked, not expired, stamp unchanged) - otherwise `401 session_expired` with the cookies cleared, as for any ended session. Same rule for a refresh that loses its claim to a concurrent logout. One extra read, no schema change - 6 tests over HTTP, 6 mutations all killed. |
| `50bf0e9` | Platform-claim forgery (5.8): a donor's token plus `sethika:platform` under eight names, one per request, as header, query and JSON body, on `/api/me/permissions` and `/api/bookings/calendar` - same status, body and database session; the session context and RLS-visible rows are read on the app's own connections by a recording `IDbConnectionFactory`. Wrong key, swapped payload and `alg: none` give 401 with no session opened; the API's own key is the control - 10 tests. |
| `044976e` | OTP over HTTP (5.8): five wrong codes give `attemptsRemaining` 4, 3, 2, 1 then `otp_attempts_exhausted`, and the right code is then `otp_not_found`; a fresh code after burning works at once; the per-number hourly cap gives 429 with `Retry-After` whichever client asks, while another number is unaffected. Limits read from `OtpOptions`; Redis keys cleaned per test - 3 tests. |
| `1c245c8` | Sign-in split for reuse, no behaviour change: `PasswordVerifier` (dummy hash, lockout, atomic failure count, history row for every refusal) and `SessionStarter` (token pair, new refresh family, success row, added to the caller's transaction). |
| `c4d3ebb` | Password change (A21): `ChangePasswordHandler` and `POST /api/me/password` - current password through `PasswordVerifier`, then unchanged, then the policy; one transaction for the hash, `SecurityStamps.RotateAsync` (now returns the stamp), the failure-count reset, a `PasswordChanged` outbox row (user id only) and the calling device's new session; per-user rate limit `password-change-user`; `ApiErrors.PasswordRejected` shared with signup. |
| `8f7dd40` | 13 tests over HTTP (`PasswordChangeEndpointsTests`), 15 mutations, all killed in the end - two only after the tests were fixed (see the commit). |
| `202e6da` | `/account/password` (current, new, confirm; field errors from the API's codes; toast; stays signed in), form logic in `lib/auth/password-change.ts`, `PasswordGuidance` shared with signup, `PasswordChange.*` in en/si/ta (drafts listed for review). Walked through in Chromium against the real API, SQL Server and Redis. |
| `ff8cd9d` | 13 Vitest tests for that form logic and `changePassword`; 11 mutations, all killed. |
| `04e9ae1` | Password reset backend (5.10): `/otp/request` and `/otp/verify` accept `reset`; `POST /api/auth/password/reset`; `PasswordResetHandler`, `IOtpService.SendDecoyAsync`, `IPasswordResetTickets` + `RedisPasswordResetTickets`; per-purpose rate-limit rules; the reset SMS in en/si/ta. 13 Redis tests, run green. |
| `115ccbe` | `PasswordResetEndpointsTests`: 16 HTTP cases against SQL Server and Redis (17 with the gateway-failure case added after it). **Written, not run** (no SQL Server where they were written). |
| `bf5e618` | `/forgot-password` (phone, then code, then new password) and the "Forgot password?" link on `/login`; `StepProgress` and `PasswordGuidance` shared with signup; 21 Vitest tests. |
| `33abfcb` | Sign-in by SMS code, API (A22): `/api/auth/otp/request` takes purpose `login`; `POST /api/auth/otp/sign-in`; `OtpSignInHandler`; `IOtpService.SendUndisclosedAsync` (cooldown, cap and stored code for every number, SMS only to an active, unlocked, phone-confirmed account, `Otp:UndisclosedSendDuration` floor of 1.5 s); `SignInHandler.CompleteAsync` + `SessionStart` shared by both ways in; three rate-limit rules; the sign-in SMS text per language. Also the 5.8 signup burn-and-429 test over HTTP. |
| `2718e1a` | 14 tests over HTTP for sign-in by code (`OtpSignInEndpointsTests`). Every mutation in the commit message killed except removing one of the two burn checks alone, which is unobservable in sequence (see A21). |
| review (§8) | `MfaRequired` accounts refused SMS sign-in (A22); lockout-clearing race closed in sign-in and password change; OTP hash and expiry in one Lua step; logout and logout-all rate-limited; `auth.PasswordHistory` written and read (last five refused, change and reset; `IX_PasswordHistory_User_Time`); `GET /api/me` and the web app landing a signed-in user in the account's language (A11). 10 HTTP tests, 11 Vitest tests, 22 mutations (15 .NET, 7 web) all killed. |
| `ec72f37` | `/login/otp` (phone → code), linked from `/login`; signup's phone and code steps extracted to `PhoneStep` and `CodeStep` and shared; `requestSignInCode` / `signInWithCode` in `lib/auth/api.ts` (3 Vitest tests); `LoginOtp.*`, `Login.useCode`, `Errors.otp_invalid` in en/si/ta. |

All of the above is reachable over HTTP.

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
| `auth.Portals/Modules/Screens/ScreenElements/Permissions/PermissionActions/PermissionDependencies` | RBAC catalogue | **0 except Portals(4) and PermissionActions(10)** — seeded since 5.5 by `10_RbacCatalogue.sql` |
| `auth.Roles / RolePermissions / UserRoles / UserPermissionOverrides` | assignments, all four temporal | 0 |
| `auth.vw_EffectivePermissions` | deny-wins + module-enabled intersection, in SQL | — |
| `org.InstitutionMembers`, `org.StaffInvitations` | membership, invites | 0 |

The refresh-token schema was already designed for rotation-with-breach-detection: `FamilyId` +
`ReplacedByTokenId` + `RevokedReason` is exactly the shape needed. **One thing was missing:** 5.2 says
refresh must "check `SecurityStamp` still matches", but the row did not record the stamp, so there
was nothing to compare. `auth.RefreshTokens.SecurityStamp` was added for it.

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
(`lib/permissions/*`, `components/permissions/*`). Since `730bd06`: next-intl routing (`proxy.ts`,
`i18n/`, `app/[locale]/`), `messages/{en,si,ta}.json`, fonts in `lib/fonts.ts`, and the brand tokens in
`app/globals.css` as the Tailwind theme. Since `4309393`: the primitives in `components/ui` and the
`/design` review page. Since `58914a6`: `lib/auth/` (the browser session and the API fetch wrapper),
the `/api` rewrite, permissions loaded in the browser, and Vitest (`npm test`). **No auth pages yet
(`/login` is a placeholder).**

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
| Rotate `SecurityStamp` on password change, MFA change or role change | `SecurityStamps.RotateAsync` (planned as `IUserSecurityService.RotateStampAsync`; built as a static helper in `f4fabd8`), called from every such path - today logout-all and password change | Old refresh tokens keep working after a password change, so "log out everywhere" silently does nothing — the one control a user reaches for after a compromise |
| Reset `AccessFailedCount` to 0 after a successful sign-in | The sign-in handler, in the same transaction as the `LastLoginAt` write | Failures accumulate across sessions until a legitimate user with the correct password is locked out |
| Maintain `NormalizedEmail` on every write | `NormalizedEmail.From`, used by signup and sign-in, later by email change | The stored value stops being canonical. **Corrected 2026-09-27:** duplicates are *not* the consequence on this database — the column's collation is `SQL_Latin1_General_CP1_CI_AS`, so `UX_Users_NormalizedEmail` and the lookup treat `Test@x.com` and `test@x.com` as one address with or without the normaliser. A mutation test showed it (`03bfca5`). The normaliser matters for anything comparing outside SQL Server, or if the column is ever given a case-sensitive collation. |

These are not hypothetical: all three are ordinary omissions in hand-rolled auth, which is exactly
why they are written down here instead of being left to memory. §5.8 carries a test for each.

### A2 — Both tokens, and where they live

| Token | Lifetime | Stored | Why |
|---|---|---|---|
| Access (JWT) | 15 min | **JS memory only** — never `localStorage` | XSS can read it, but only for 15 min and cannot mint a new one |
| Refresh | 14 days, rotating | **httpOnly cookie**, `Path=/api/auth/session`, `SameSite=Lax`, `Secure` in prod | JS cannot read it at all |

Consequence: on page reload the access token is gone, so the app calls `/api/auth/session/refresh`
once at boot (silent refresh) before rendering anything that depends on identity.

**Amended 2026-09-27:** the cookie path was `/api/auth/refresh`, but a cookie scoped there is never
sent to logout, which then has no session to end. The cookie is scoped to `/api/auth/session`, and the
four endpoints that need it live under it: `refresh`, `switch-institution`, `logout`, `logout-all`.

Those four are the cookie-authenticated, state-changing endpoints, so they are the ones that need CSRF
protection → **double-submit token**: a non-httpOnly `sethika-csrf` cookie whose value must be echoed
in an `X-CSRF-Token` header, compared in constant time. Every other endpoint is Bearer-authenticated
and therefore not reachable by CSRF at all.

`SecurityStamp` is *not* checked per request — that would put a DB read in front of every call and
defeat a stateless JWT. It is checked on refresh. The honest consequence: revoking a user leaves at
most a 15-minute window on their current access token. Write that sentence in the report.

### A3 — The frontend talks to the API through a Next.js rewrite, not cross-origin

`next.config.ts` gets `rewrites()` mapping `/api/*` → `NEXT_PUBLIC_API_BASE_URL`. The browser then only
ever sees one origin, so the refresh cookie is same-site (`SameSite=Lax` works, no `SameSite=None`, no
`Secure`-over-http problem in dev) and there is no CORS configuration to get wrong.

This must be decided now: doing it later means changing every fetch call and re-testing the cookie.

**Amended 2026-10-01:** the variable is **`API_BASE_URL`, not `NEXT_PUBLIC_API_BASE_URL`.** A rewrite is
resolved by the Next.js server, never the browser, so the browser does not need the API's address, and a
`NEXT_PUBLIC_` name would be inlined into the client bundle for no reason. It defaults to the API's
`http` launch profile, `http://localhost:5182`. Measured, not assumed: **the value is read when
`next build` runs** and baked into the route manifest - building without it and starting with it still
proxied to the default. Set it in the build environment.

Found while checking the rewrite, for A18: `next start` forwards a client's own `X-Forwarded-For`
unchanged and does not add the client's address. The API trusts loopback, so through a self-hosted
`next start` on the same machine any client can choose its per-client rate-limit identity, and without
the header every client shares the proxy's. On Vercel the platform sets the header; that, and the
production `ForwardedHeaders:KnownProxies`, must be checked on the real deployment. The per-identity
limits and the lockout do not depend on it (A18).

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

*Amended 2026-10-01:* Next.js 16 renamed `middleware.ts` to `proxy.ts`, so next-intl's handler is the
default export of `web/proxy.ts` (next-intl 4.14 supports Next 16). The proxy implements the last three
steps; `PreferredLanguage` joins in front once the web app can read it from the API (5.7, `lib/auth/`).
`/en/...` redirects to the unprefixed path and sets the cookie to `en`, which is how a visitor switches
back to English.

### A12 — Rate limiting is Redis, not the in-process limiter

`Microsoft.AspNetCore.RateLimiting` is per-instance. CLAUDE.md lists Redis rate limiting as a genuine
Redis use and the course requires Redis be used for real. `INCR` + `EXPIRE` fixed window is three
lines; the boundary-burst weakness is acceptable here and the upgrade path (sorted-set sliding window
log) is one method. Note the weakness in the report instead of hiding it.

Keys as built (`c59db76`): `rl:{rule}:{first 128 bits of SHA-256 of the subject}`, one rule per
line of `AuthRateLimits` — `login-name`, `login-client`, `otp-request-client`, `otp-verify-phone`,
`otp-verify-client`, `signup-client`, and `refresh-client` (5.2); step-up (5.3) shares the two
OTP client budgets. The password reset (5.10) has its own: `otp-reset-request-client`,
`otp-reset-verify-client`, `otp-reset-verify-phone` and `password-reset-client`. Sign-in by code (A22) adds `otp-login-request-client`,
`otp-signin-phone` and `otp-signin-client`, the twins of signup's three under their own keys. The subject is hashed so a
typed login cannot make a key of any size, and phone numbers and emails are not readable in
`KEYS rl:*`. The counter and its expiry are set in one Lua step: INCR-then-EXPIRE leaves a counter
that never expires if the process dies between them.

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


### A15 — Role levels: a rank and a permission ceiling, defined by the platform *(decided 2026-09-27)*

Requirement, from the user: institutions create their own roles freely; every role has a level; and
what can be done at each level is defined per level.

Every role belongs to a **level**. A level has a **rank** (1 is the most senior) and a **ceiling** —
the set of permissions any role at that level may hold. A custom role is created at a level and can
only be given permissions inside that level's ceiling. Levels and their ceilings are defined by the
platform, not by each institution.

| Level (seed) | Scope | Rank |
|---|---|---|
| `OWNER` | Institution | 1 |
| `MANAGER` | Institution | 2 |
| `STAFF` | Institution | 3 |
| `PLATFORM_ADMIN` | Platform | 1 |
| `PLATFORM_STAFF` | Platform | 2 |
| `DONOR` | Donor | 1 |

Schema, all in the SQL project:

- `auth.RoleLevels` — `Id`, `Scope`, `Rank`, `Code`, `Name`, `IsActive`; unique `(Scope, Rank)` and
  `(Scope, Code)`, plus `UNIQUE (Id, Scope)` so roles can point at it by composite FK. Reference data,
  seeded by MERGE.
- `auth.RoleLevelPermissions (RoleLevelId, PermissionId)` — the ceiling. **Temporal**, like
  `RolePermissions` (D7): narrowing a ceiling changes what users can do, so "who could do X on date Y"
  needs its history too.
- `auth.Roles.RoleLevelId NOT NULL` with `FK (RoleLevelId, Scope) → auth.RoleLevels(Id, Scope)`, so a
  role's level is always in the role's own scope — the same composite-FK pattern as the tenant FKs.
- The ceiling carries both scopes, with composite FKs to `RoleLevels(Id, Scope)` and
  `Permissions(Id, Scope)` and a named CHECK, so "an institution level never contains a
  `Platform`-scope permission" (CLAUDE.md) is a constraint rather than a convention.

**The ceiling is enforced in `auth.vw_EffectivePermissions`**, not only when a role is saved: a role
grant counts only if the permission is inside that role's level's ceiling. A `RolePermissions` row
outside the ceiling — from a buggy screen, a hand-run script, or a ceiling narrowed later — is simply
not effective. It is the same principle as the module intersection already in the view: the view
decides access, so nothing upstream can talk past it.

Grant overrides (`UserPermissionOverrides`, `Effect = 'Grant'`) belong to no role, so no level
applies to them; they are bounded when they are granted, by the rule below. Deny still wins over all.

**Anti-escalation by rank**, enforced by the role API (§5.9) inside its transaction:

- an actor may create, edit, assign or revoke a role only when the role's rank is **strictly more
  junior** than the actor's own most senior rank in that institution;
- every permission added must be inside the level's ceiling **and** held by the actor — a user can
  only grant what they hold (CLAUDE.md);
- rank-1 institution roles (`OWNER`) are assigned only by platform staff, which is what onboarding
  approval does. So an Owner cannot mint a second Owner, and no one can promote themselves.

Why platform-defined and not per institution type: the type already decides which modules an
institution has, and the view already drops the permissions of disabled modules. One set of
institution levels, intersected with each institution's enabled modules, gives per-type behaviour
without a second per-type table.

### A16 — Command handlers reach EF Core through `ISethikaDbContext` *(decided 2026-09-27, Q6)*

`Sethika.Application` defines `ISethikaDbContext` — the `DbSet`s that commands write, `Database` for
transactions, and `SaveChangesAsync` — and references `Microsoft.EntityFrameworkCore`, the core
package only, with no SQL Server provider. `SethikaDbContext` in Infrastructure implements it, so the
RLS session-context interceptor applies to every handler without the handler knowing it exists.

Handlers are plain classes in their module folder (`Application/Access/SignIn/SignInHandler.cs`),
registered in DI, one public `HandleAsync`. No mediator library: nothing here needs pipeline
behaviours yet, and MediatR is no longer free for commercial use.

The cost, said plainly for the report: Application knows it runs on EF Core. It does not know it
runs on SQL Server — `UPDLOCK`, RLS and the conditional booking `UPDATE` stay in Infrastructure or the
database. Repositories were the alternative; they would add an interface per aggregate and a method
per query shape to hide a DbContext that is already a unit of work.


### A17 — A phone verification reaches signup as a single-use ticket *(decided 2026-09-27)*

The plan listed `/otp/verify` and `/signup` as separate endpoints but not how the second knows the
first succeeded — the code is consumed by the verification, so it cannot simply be sent again.

`IOtpService.VerifyAsync` now issues a **ticket** on success: 32 random bytes, base64url, of which
only the SHA-256 is stored, under `otp:ticket:{purpose}:{e164}`, for 15 minutes. `RedeemTicketAsync`
spends it exactly once, bound to the same number and purpose, with a Lua compare-and-delete —
atomic on the server, where GET-then-DEL from C# would let parallel requests both succeed (GETDEL
would do, but needs Redis 6.2 and the local build is 3.2).

Issuing it inside `VerifyAsync`, rather than from a separate call, means the only way to hold a
ticket is to have verified a code. The same mechanism serves the password-reset flow later.


### A18 — The client address is believed only from a trusted proxy *(decided 2026-09-27)*

Per-client rate limits and the sign-in history need the client's address. Behind the Next.js rewrite
(A3) and Azure's front end, the connection comes from a proxy and the client is in
`X-Forwarded-For` — which any client can also send. `UseForwardedHeaders` therefore believes it only
when the connection comes from a known proxy: loopback by default, which covers local development,
and otherwise the addresses in `ForwardedHeaders:KnownProxies`.

**Deployment prerequisite**, alongside A13 and A14: configure the production proxy addresses. Left
empty, every client appears to be the proxy, the per-client limits become one budget shared by all
users, and a busy hour locks everyone out of OTP. The per-identity limits and the account lockout do
not depend on this.

Per-client limits are loose on purpose (20–100 per window): Sri Lankan mobile networks put many
subscribers behind one address, so a tight per-address limit would lock out a whole temple's donors
at once. Per-identity limits are the real control. Write both halves in the report.


### A19 — A spent refresh token presented within 10 seconds is a race, not a theft *(decided 2026-09-27)*

Reuse detection revokes the whole family when a rotated token comes back. But two tabs refreshing at
the same moment both send the old cookie, and the later one would then sign the user out of every
device for nothing more than having two tabs open.

So a rotated token presented within **10 seconds** of its rotation is refused - no tokens - but the
family survives; presented any later, the family is revoked. The cost, for the report: a copy
replayed inside those 10 seconds is not detected. It still gets nothing, and any later replay is
caught. The web client's single-flight refresh (5.7) makes the race rare in one tab; this covers two.

The refusal is **`409 session_superseded`, and it leaves the cookies alone.** The first build answered
it like every dead session, `401` plus clearing the cookies - but the two tabs share one cookie jar,
and a clearing response that lands after the winner's would delete the successor, signing the user
out anyway. The mutation run found it: with the claim's `RevokedAt IS NULL` removed, the parallel
test still passed, because ten requests on their own rarely overlap. The test now holds an update
lock on the token's row until all ten are waiting at the claim, and the race is deterministic.

*Amended 2026-10-04 (`0e770a4`).* "Superseded" tells the client its cookies already hold the
winner's successor, so it is only said while that session is alive: the family still has a token
that is not revoked, not expired, and carries the user's current stamp - the test a refresh
applies. A walk-through found the gap: log out, send the old cookie within 10 seconds, and the
answer was `409` instead of `401`. Now a late token whose session has ended (logout, logout
everywhere, family revoked, successor expired, stamp changed) is answered `401 session_expired`
with the cookies cleared, like every ended session. The question is asked of the family, not of
the direct successor, so a winner that rotated again inside the grace (A to B to C) still counts
as alive; asking B alone would have the late tab clear the shared cookie jar and delete C. The
same check covers a refresh that loses its claim to a concurrent logout. Outside the grace nothing
changed: a replay still revokes the family.

*Amended 2026-11-08 (hardening).* A page left while its refresh is in flight. The server rotates,
the browser cancels the fetch with the page, the successor cookie never lands, and the next page
presents the spent token: `409` inside the grace (no other tab holds the successor, so the retry
gets `409` again), and the whole family revoked as a replay after it. The fix is on the client:
every rotation is sent with `keepalive`, so it outlives the page and its `Set-Cookie` is stored,
and the next page meets an ordinary two-tab race that the superseded retry already settles. The
server and its rules are unchanged. Locally the Next dev proxy passes a cancelled request's abort
on and the API rolls an uncommitted claim back, so the bug only had the moment between commit and
response headers there; a proxy that does not pass aborts on (production's is not known) widens
that to the whole round trip. The browser test holds the token's row with an update lock so the
page is certain to be left mid-refresh, and asserts that the abandoned refresh is still carried
out (two rotations a round, not one); with `keepalive` removed it fails. Not covered: a response
lost on the network, and a phone app killed mid-refresh (its token is in the body, not a cookie).

Rotation itself is claimed by a conditional `UPDATE … WHERE Id = @id AND RevokedAt IS NULL` rather
than `UPDLOCK, ROWLOCK` as 5.2 first said. The guarantee is the same - of any number of requests with
one token, exactly one finds it unrevoked - and it keeps the SQL Server hint out of Application (A16).


### A20 — A step-up lasts until its access token is refreshed: fifteen minutes at most *(decided 2026-09-28)*

5.3's last box asked that step-up be "once per session, not once per action". The second half holds:
one step-up covers every `RequiresMfa` action while it lasts. The first half cannot, beside A5. The
grant is bound to the access token's `jti` so that a refreshed token does not inherit it, and access
tokens live fifteen minutes - so a step-up lasts until the next refresh, less than fifteen minutes if
it was taken late in the token's life. A5's reason wins: whoever refreshes a stolen refresh token must
not come out holding a stepped-up token. The cost, for the report, is one more SMS for every fifteen
minutes of sensitive work. The client softens it (5.7): with under five minutes left on its access
token, it refreshes before asking for a code, so the step-up buys nearly its full window.

`MfaRequired` is not read in 5.3. The rule sits on the permission - CLAUDE.md: `RequiresMfa`
permissions need step-up, whoever holds them - and platform staff and owners are covered because they
hold those permissions. The column becomes "must enrol an MFA method" when TOTP (A6) arrives.

Two edges, both tested: a suspended account's access token still works until it expires (A2), but
it cannot step up, so nothing sensitive is reachable with it; and a grant is looked up only for a
`RequiresMfa` permission, so ordinary requests cost no extra Redis call. The grant store fails
closed, the reverse of the rate limiter: an outage refuses sensitive actions rather than letting
them through unverified.

### A21 — Password change re-authenticates with the current password, through sign-in's lockout *(decided 2026-10-04)*

`POST /api/me/password` is authenticated-only, like `/api/me/permissions`: it acts on the caller's
own account, named by the token, and a permission for it could only ever be held by everyone.

- **The current password goes through sign-in's `PasswordVerifier`**, so a wrong one counts toward
  the same lockout and writes the same history row. Otherwise a stolen access token would be fifteen
  minutes of unlimited guessing beside a sign-in that allows five. Wrong is `400 password_incorrect`;
  a locked account answers byte-for-byte the same, as sign-in answers a lock like a wrong password.
  `400` rather than sign-in's `401`, because a `401` makes the web client refresh and retry.
- **Order:** current password, then `password_unchanged`, then the policy. The policy compares the
  new password with the user's phone, email and name, so answering it first would let a token
  holder probe them. (`password_unchanged` compares the two request fields, so its place is free.)
- **No step-up** on top of the current password. Step-up belongs to `RequiresMfa` permissions (A20)
  and this endpoint has none; an SMS requirement would also leave a user who has lost their phone
  unable to secure the account.
- **One transaction:** hash, `PasswordChangedAt`, `SecurityStamps.RotateAsync`, failure-count reset,
  a `PasswordChanged` outbox row (user id only), and the calling device's new session through
  `SessionStarter`, added after the rotation so it carries the new stamp. The device keeps the
  institution its token acted for. Every other device keeps its access token until it expires (A2)
  and loses its session at its next refresh.
- **Password history** *(added 2026-10-11, §8)*: the new password must not be the current one or any
  of the four before it (`PasswordReuse`, shared with the reset), answered as `password_rejected`
  with `RecentlyUsed`, checked last because it costs a hash per remembered password. The replaced
  hash is added to `auth.PasswordHistory` in the change's transaction.
- **Not done:** a refresh that another tab of the
  same browser has in flight with the old cookie at the moment of the change can be answered `401`,
  which clears the cookies; if that answer lands after the change's, this browser loses its new
  session too and must sign in again. Rare - a tab refreshes on load and every fifteen minutes - and
  closing it needs a cross-tab lock around the change, which was not attempted.

### A22 — Sign-in by SMS code, without telling anyone which numbers have accounts *(decided 2026-10-04)*

`POST /api/auth/otp/request` with purpose `login`, then `POST /api/auth/otp/sign-in
{ phoneNumber, code }`. Anyone can type any number, so both answer a number with no account exactly
as they answer a registered one:

- **Request.** Every number gets the 60-second cooldown, the 3-per-hour cap (`otp:sends:login:…`)
  and a stored code hash; only an account that is active, not locked and has a confirmed phone is
  actually texted, in its `PreferredLanguage`. The SMS - a `notify.Notifications` row and a gateway
  call - is the one step only a real account pays for, so the request answers no sooner than
  `Otp:UndisclosedSendDuration` (1.5 s). A send slower than that is logged: it means the floor must
  rise, because the timing is leaking again. The cost, for the report: every sign-in-code request
  takes at least 1.5 s, which nobody waiting for an SMS notices.
- **Sign-in.** The code is checked whether or not an account matched, and the stored code for an
  unknown number burns after the same five guesses. A wrong, expired or burned code, an unknown
  number and a locked account are one answer, `401 otp_invalid`, with no attempts-left count (it
  would differ between a wrong code and a burned one). A suspended account gets `403
  account_not_active`, as password login gives it - and only to someone holding a valid code.
- **Success** goes through `SessionStarter`, as password sign-in and password change (A21) do:
  institution rule, tokens, a new refresh family bound to the `SecurityStamp`, the history row,
  the same body and cookies. (The branch first reused `SignInHandler.CompleteAsync`; the merge moved
  it onto A21's `SessionStarter`, so the three ways a session starts share one path.)
- **`AccessFailedCount` is not reset by a code sign-in, and wrong codes do not add to it.** A1's reset
  belongs to the password: the count is evidence about guesses at the password, and clearing it
  would hand whoever is guessing a fresh five every time the owner signs in by code. Counting wrong
  codes against it would let anyone lock any account by typing its number.
- **A locked account is not texted** (the lockout outlasts the code, and the SMS is paid for), so a
  password lockout also blocks code sign-in for its fifteen minutes. Someone can therefore stop an
  owner signing in either way for fifteen minutes by guessing wrong passwords - as they already could
  for password sign-in.
- `/api/auth/otp/verify` still refuses purpose `login`: it reports attempts left, which sign-in hides.
- **`MfaRequired` accounts cannot sign in by code** *(added 2026-10-11, §8)*: platform staff and,
  once onboarding sets it, institution owners. They are sent nothing and answered exactly like a
  number with no account, as the password reset (5.10) already answers them; a code held from
  before the flag was set is refused like a wrong one (`FailureReason = 'MfaRequired'`). A code
  alone is one factor, and the step-up code goes to the same phone, so SMS sign-in would have made
  the SIM the whole account. These accounts sign in with their password.

Two honest gaps for the report. `auth.LoginHistory` has no method column, so a successful code
sign-in and a password sign-in write the same row; failures are told apart by `FailureReason`
(`InvalidCode`, `PhoneNotConfirmed`). And the burn has two checks - the fifth wrong guess deletes the
code, and any attempt past five deletes it - so a mutation removing either one alone survives the
sequential tests; the second matters only for concurrent guesses, which no test drives.

---

## 3. Open questions

All six are closed. Q5 was taken at its default in 5.5, on the user's go-ahead (2026-09-28).

| # | Question | Blocks | Default if you say nothing |
|---|---|---|---|
| ~~Q1~~ | ~~SMS provider~~ — **decided 2026-09-27: console sender for development, real provider behind the same interface before deployment. See A14.** | — | — |
| ~~Q2~~ | ~~A1 — Identity primitives instead of full Identity?~~ — **confirmed 2026-09-27. See A1, including the three responsibilities this transfers to us.** | — | — |
| ~~Q3~~ | ~~**A8** — add `libphonenumber-csharp`?~~ — **done: installed in `1ca0b65`.** | — | — |
| ~~Q4~~ | ~~**Legal text.**~~ — **decided 2026-09-27: seed a clearly-marked `0.1-draft` of Terms and Privacy in en/si/ta.** `auth.UserConsents.LegalDocumentId` is a NOT NULL FK, so signup cannot record consent without a document row. Every draft row's title says DRAFT; replacing it is a new `Version` with `IsCurrent = 1`, and existing consents keep pointing at the draft they accepted. | — | — |
| ~~Q6~~ | ~~**Where command handlers get EF Core.**~~ — **decided 2026-09-27: `ISethikaDbContext` in Application. See A16.** | — | — |
| ~~Q5~~ | ~~**First platform admin.**~~ — **done in `1739db6`, as the default below.** | — | A `dotnet run --project src/Sethika.Api -- bootstrap` command, idempotent, reading phone and password from User Secrets. Keeps the credential out of source control and out of the dacpac. |

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
| §5.5 RBAC catalogue seed + role levels + bootstrap admin | ~1.5 days |
| §5.6 i18n foundation | ~1 day |
| §5.7 design system + auth screens | ~2 days |
| §5.8 tests | woven through, plus ~1 day of its own |
| §5.9 custom roles API with rank anti-escalation (added 2026-09-27) | ~1.5 days |

That is **10–11 days, not 5.** Two options: run it as a 9-day slice and compress week 2, or ship
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
- [x] `Sethika.Application/Access/SignUp/SignUpDonorHandler` (`03bfca5`) — one save: `Users` +
      `DonorProfiles` + the DONOR role in `UserRoles` + `UserConsents` + `core.OutboxMessages`. Redeems
      the A17 ticket only after every form check, and reports "already registered" only after that.
      Consent is by the `ContentSha256` of the text the client showed. Does **not** issue tokens: the
      signup endpoint calls `SignInHandler` next, so the new session gets a normal history row.
- [x] `Sethika.Application/Access/SignIn/SignInHandler` (`58fa000`) — verify the hash, apply the
      lockout policy (5 failures → 15 min, counted by one atomic UPDATE), write `auth.LoginHistory`
      on **both** outcomes, store the refresh token's hash. With one institution the token acts for
      it; with several, for none.
- [x] DI wiring in `Sethika.Infrastructure/DependencyInjection.cs` (`AddSethikaAccess`) — bind `OtpOptions`,
      `SmsOptions` and `JwtOptions`, register `IOtpService`, `IPasswordPolicy`,
      `IPhoneNumberNormalizer`, `IPasswordHasher<User>`, `ITokenService` (singleton — it holds the
      signing credentials) and `TimeProvider.System`, and select the SMS sender from `Sms:Provider`.
      Per A14 the host **refuses to start** with the console sender outside Development (and with
      `Otp:LogCodesForDevelopment` on), and `NotifyLkSmsSender` gets its `HttpClient` from
      `IHttpClientFactory`. `Program.cs` still reads `Jwt:SigningKey` itself for bearer validation;
      both sides go through `SethikaJwt.CreateSigningKey`, and a host round-trip test guards the pair.
- [x] Endpoints (`94b1c29`): `POST /api/auth/otp/request`, `/api/auth/otp/verify`, `/api/auth/signup`,
      `/api/auth/login`, plus `GET /api/legal/current`. Anonymous and rate-limited. `LockedOut`
      answers exactly like `InvalidCredentials`; `/otp/request` accepted only the signup purpose
      (since `33abfcb` also `login`, A22);
      `/signup` signs the donor in through `SignInHandler`; login and signup set the A2 cookies and
      `Cache-Control: no-store`. Errors are `{ "code": ... }` for the client to translate — a
      convention every later endpoint should keep.

**User enumeration:** login must answer identically for "no such user" and "wrong password", *including
timing* — so when the user is not found, still verify the supplied password against a fixed dummy hash
before returning. A response that is 200 ms faster for unknown accounts is a working user-enumeration
oracle, and it is exactly the kind of thing an examiner asks about.

### 5.2 — Refresh rotation with breach detection

- [x] `POST /api/auth/session/refresh` (`f4fabd8`) — hash the cookie, find the row, refuse it if expired,
      revoked, its account inactive or its `SecurityStamp` stale; claim it with a conditional UPDATE,
      insert the successor in the same family, link `ReplacedByTokenId`, return a new access token and
      set new cookies. The institution is re-read from `auth.UserRoles` every time, so a revoked role
      drops out of the next token.
- [x] **Reuse detection:** a rotated token presented again revokes the entire family
      (`RevokedReason = 'ReuseDetected'`), except within the 10-second grace of A19, which is
      `409 session_superseded` with the cookies untouched - while the family still has a live
      token; once the session has ended it is `401 session_expired` and the cookies are cleared
      (`0e770a4`, A19 amended).
- [x] `POST /api/auth/session/logout` — revoke this family, clear both cookies.
- [x] `POST /api/auth/session/logout-all` — `SecurityStamps.RotateAsync`: new stamp and every live
      refresh token revoked, in one transaction. Needs a live token, so a revoked copy cannot be used
      to sign the real user out everywhere.
- [x] `POST /api/auth/session/switch-institution` — the slug is checked against `auth.UserRoles`
      (A7); an institution the user does not hold is `403`, rotates nothing and issues nothing.
- [x] Concurrency: ten parallel refreshes with one token — exactly one succeeds, exactly one successor
      is written, the other nine get `409` and no cookies. By conditional UPDATE, not `UPDLOCK` (A19).

### 5.3 — MFA step-up

- [x] `POST /api/auth/stepup/request` and `/verify` (`7c79832`) — Bearer-authenticated, not
      permission-gated (a permission to step up would be circular). The code goes to the account's
      own confirmed phone with `purpose = stepup`, never to a number in the request; no confirmed
      phone is `409 no_verified_phone`, an inactive account `403 account_not_active`.
- [x] On success, `SET stepup:{userId}:{jti}` with a 15-min TTL, and a `LoginHistory` row with
      `Succeeded = 1, MfaUsed = 1`, written before the grant so no grant exists without its row. A
      grant that fails to store is `503`, not `204`.
- [x] `PermissionAuthorizationHandler`: `RequiresMfa(code)` and no grant for this token means refuse.
      No `Fail()`: the refusal is marked, and `StepUpAuthorizationResultHandler` answers it with
      `403 stepup_required`. A permission not held is a plain `403` - step-up could not lift it.
- [x] Once per grant, not once per action — **amended by A20**: the grant ends with its access token,
      so "session" is at most fifteen minutes. `MfaRequired` is not read yet (A20).

### 5.4 — Rate limiting

- [x] `IRateLimiter` in Application, `RedisRateLimiter` in Infrastructure (A12) — `c59db76`.
- [x] An endpoint filter that applies it, keyed per route; `429` with `Retry-After` — `94b1c29`
      (`RateLimitPerClient`, `RateLimitBy<TBody>`).
- [x] Applied to OTP request/verify, login, refresh and signup — refresh and switch-institution use
      `refresh-client` (`f4fabd8`). Logout and logout-all joined that budget on 2026-10-11 (§8):
      each looks a cookie up before there is an account to limit by.
- [x] Redis down means **fail open with a logged warning**, not closed. A cache outage must not lock
      everyone out — the same principle already applied to the permission cache.

### 5.5 — RBAC catalogue seed and bootstrap admin

- [x] `Scripts/Seed/10_RbacCatalogue.sql` (`92b6187`) — INSTITUTION portal only, because only its
      screens exist: modules `INSTITUTION`, `BOOKINGS` (placeholder), `ACCESS`; screens
      `INSTITUTION.PROFILE`, `BOOKINGS.CALENDAR`, `ACCESS.ROLES`, `ACCESS.MEMBERS`; nine permissions,
      `<screen>.<action>`, with `ACCESS.ROLES.EDIT`, `ACCESS.MEMBERS.EDIT` and
      `BOOKINGS.CALENDAR.APPROVE` `RequiresMfa`; six dependencies (every action needs its screen's
      VIEW, assigning roles needs `ACCESS.ROLES.VIEW`). Matched on Code, never Id; an unknown code
      THROWs and stops the deploy instead of a JOIN dropping it. Dependencies, ceilings and template
      grants are synced exactly, deletions included; modules, screens and permissions are never
      deleted.
- [x] Ceilings: OWNER and MANAGER may hold every institution permission (rank, not the ceiling,
      stops a manager managing managers); STAFF the profile and calendar VIEWs and
      `BOOKINGS.CALENDAR.APPROVE` (the access screens' VIEWs were removed on 2026-10-11: they list
      other staff and their phone numbers).
      Platform and donor levels are empty until their screens exist. Templates `OWNER` (all nine),
      `MANAGER` (all but `INSTITUTION.PROFILE.EDIT`), `STAFF` (the profile and calendar VIEWs), as
      `IsSystem = 1` so the fixture reset keeps them; the seed THROWs if a template grant falls
      outside its ceiling, which the view would otherwise drop without a word. Reviewed by the user
      on 2026-10-11: MANAGER may be given `INSTITUTION.PROFILE.EDIT`, STAFF may be given
      `BOOKINGS.CALENDAR.APPROVE`, and the MANAGER template keeps the access EDITs by default.
- [x] Role levels per A15 (`92b6187`): `auth.RoleLevelPermissions` + history, temporal, with composite
      FKs to `RoleLevels(Id, Scope)` and `Permissions(Id, Scope)` (new `UQ_Permissions_IdScope`), and
      the ceiling as a JOIN in `auth.vw_EffectivePermissions`. **Stricter than A15 said:**
      `CK_RoleLevelPermissions_Scope` is `PermissionScope = 'Self' OR PermissionScope = LevelScope`,
      so a platform level cannot hold institution permissions either, not only the reverse.
- [x] `Scripts/Seed/09_LegalDocuments.sql` per Q4 (`bc069dd`).
- [x] The `bootstrap` command per Q5 (`1739db6`): `Bootstrap:PhoneNumber`, `Password`, `FullName`
      from configuration, never arguments (shell history). Creates a `PlatformStaff` account with
      `PhoneConfirmed` (step-up needs it) and `MfaRequired`, holding the new `PLATFORM_ADMIN` system
      role. Idempotent - a rerun changes nothing, not even the password. Makes only the first
      administrator: refuses while another active one exists, but a suspended or deleted one does
      not block it, which is the recovery path. Never converts a donor or institution account.
- [x] D10 re-checked after publishing to both databases: `SqlPackage /Action:Script` against
      `Sethika` gives zero schema statements, only the 17 seed MERGEs.

**The test fixture changed with this slice.** `RlsTestData.sql` used to create its own BOOKINGS
catalogue and delete every permission on each reseed; it now uses the seeded one, deletes only
non-system roles' grants, and two permission-engine tests delete only the fixture role's grants.
Anything that touches reference data in a test runs in a rolled-back transaction.

### 5.6 — i18n foundation

- [x] `next-intl`, `proxy.ts` (Next.js 16's name for `middleware.ts`), `messages/{en,si,ta}.json`,
      `localePrefix: 'as-needed'` (A11). Sinhala and Tamil are drafts until a native speaker signs off
      `docs/translations-to-review.md`.
- [x] `PUT /api/me/language` writing `auth.Users.PreferredLanguage` (`1006af8`) — authenticated-only,
      like `/api/me/permissions`; `en`/`si`/`ta` or `400 unsupported_language`; 204, idempotent.
      `LanguageSwitcher` calls it for a signed-in user (`dbc37ac`), beside the navigation, never in
      its way. A11's first resolution step was added on 2026-10-11 (§8): `GET /api/me` returns the
      account's name and language; `logIn` and `signInWithCode` read it before resolving and
      `useLandAfterSignIn` lands the user in it, and `SessionBoot` moves a page restored from the
      refresh cookie to it. **Still not done:** no component test for the switcher or the boot move
      (Vitest here is node-only) and no live browser check.
- [x] `notify.Templates` seeded per language for the OTP SMS — a Sinhala-preferring user must receive
      the Sinhala OTP message, which is why `Templates` has a `Language` column. `11_NotifyTemplates.sql`
      (`c89e634`) and `OtpService` reading it (`1b97c75`). The si and ta bodies are drafts in
      `docs/translations-to-review.md`. `OTP_RESET` was added when the password reset (5.10) was
      merged, from the text its branch had in `OtpService.BuildMessage`. `OTP_LOGIN` was added the
      same way when sign-in by code (A22) was merged; its Tamil body is 73 characters, two segments,
      flagged for the reviewers.
- [x] Fonts per A10. Verified on the placeholder `/login` in en, si and ta with Chromium's
      `CSS.getPlatformFontsForNode`: Latin renders in Outfit (Fraunces for the tagline), Sinhala in
      Noto Sans Sinhala, Tamil in Noto Sans Tamil. Check again on the real form in 5.7.
- [x] Rule from here on: **no hard-coded user-facing string in any component.** Cheap now, expensive in
      week 3.

### 5.7 — Design system and auth screens

- [x] `next/font` for Outfit, Fraunces and Noto Sans Sinhala/Tamil; the brand tokens already in
      `globals.css` become the Tailwind theme.
- [x] Primitives on shadcn/ui, restyled to the Sethika palette — not default shadcn, which is the whole
      point of "unique UI": `Button`, `Input`, `Field`, `Label`, `Card`, `Sheet`, `Toast`, `Spinner`,
      `OtpInput`, `LanguageSwitcher`. All in `components/ui`; review them at `/design`, `/si/design`,
      `/ta/design`. Toast is Radix Toast and `OtpInput` is hand-written, so `sonner` and `input-otp`
      are not dependencies.
- [x] Screens: `/signup` (phone → OTP → details → consent), `/login`, `/stepup`,
      `/select-institution` — `4c82ac0`. **`/login/otp` (phone → code) followed in `ec72f37`**, once
      the API had sign-in by code (A22, `33abfcb`); it shares `PhoneStep` and `CodeStep` with signup.
      Walked through live on 2026-10-04 at 320 px against the API with SQL Server and Redis: from
      `/si/login`'s link, the Sinhala sign-in SMS arrived (console sender), a wrong code showed
      `otp_invalid`, the right one signed in, and a reload stayed signed in; an unknown number got the
      same screens and error and no SMS; signup's phone → code → details still worked.
      Checked so far without the API: lint, build, 32 unit tests, and a browser pass at 320 px in
      en/si/ta against mocked responses shaped per the C# contracts (no overflow, no clipped text,
      44 px targets, focus moves to each step heading, request bodies match `SignUpBody`,
      `OtpRequestBody` and `SwitchInstitutionBody`).
      **Still owed, needs the API with SQL Server and Redis running:**
      1. Signup end to end: the SMS (console sender) arrives in the page's language, a wrong code
         shows the attempts left, the ticket carries to signup, both legal documents load and
         their hashes are accepted, and the new donor lands signed in.
      2. Reload on any page and stay signed in (silent refresh through the rewrite, A2/A3).
      3. The same flows in `/si/...` and `/ta/...`: Sinhala and Tamil text in Noto, the SMS in
         that language.
      4. A step-up: as a user holding `BOOKINGS.CALENDAR.APPROVE`, press "Approve booking" on the
         home page, enter the code from the console, and see the call succeed after the dialog.
      5. Sign out, sign out everywhere (another browser loses its session at its next refresh),
         and switching institution as a member of two.
- [x] `lib/auth/` — access token in memory (A2), silent refresh at boot, one fetch wrapper that
      refreshes once on `401` and retries, queueing concurrent calls so a page with five requests does
      not fire five refreshes. A refresh answered `409 session_superseded` means another tab rotated
      first: retry the refresh once with the cookie the browser now holds (A19); `401` means sign in.
      A `403 stepup_required` opens the step-up dialog, then retries the request; with under five
      minutes left on the access token, refresh before asking for the code (A20).
      Done in `58914a6` and unit-tested against the API's contracts (16 tests, each rule mutation-checked).
      The step-up dialog itself comes with the screens; `session.setStepUpHandler` is its hook.
      **Still owed, needs the API with SQL Server and Redis running:** a real silent refresh after a
      reload (cookie path `/api/auth/session` and `Set-Cookie` through the rewrite), two tabs
      refreshing at once ending in one session (A19), a `RequiresMfa` call answered `stepup_required`
      and retried, logout clearing both cookies, and `/api/me/permissions` loading after sign-in.
- [x] `next.config.ts` rewrites per A3 (`API_BASE_URL`, amended in A3). Forwarding of cookies, the
      CSRF and Authorization headers and `Set-Cookie` checked with an echo server, not the API.
- [x] `/account/password` (`202e6da`, A21): current, new and confirm; each API code on the field that
      can fix it; a toast on success, and the device stays signed in on the session the API answers
      with. Walked through in Chromium against the real API, SQL Server and Redis via `next start` and
      the rewrite, at 320 px in en/si/ta: field errors, success, reload still signed in, a second
      browser signed out at its next load.
- [x] Faith-neutral imagery only. No religious symbol on a shared screen. The auth screens use the
      brand's bowl mark and nothing else; checked again whenever a screen adds imagery.

### 5.8 — Tests

Unit (`Sethika.Application.Tests`): password policy; the refresh-rotation state machine as a pure
function; OTP attempt counting; rate-limit window maths; E.164 normalisation, including `07…`, `+94…`
and `94…` collapsing to one value; step-up expiry.

Integration (`Sethika.Api.IntegrationTests`), against real SQL Server and Redis:

- [x] signup → OTP → login → authenticated call, end to end (`AuthEndpointsTests`, HTTP only)
- [x] wrong password five times means locked; the correct password during lockout is still refused
      (`SignInHandlerTests`, including five wrong guesses in parallel)
- [x] unknown user and wrong password are indistinguishable in body, status **and** timing —
      timing at the handler (`58fa000`), status and body over HTTP, with a locked account too (`94b1c29`)
- [x] refresh rotates, and the old token stops working (`SessionEndpointsTests`)
- [x] **replaying a rotated refresh token revokes the whole family** — the security test that matters most
- [x] a token signed with a different key gives 401; an expired one gives 401
      (`JwtTokenServiceEndpointTests`, with a 14-minute-old token accepted as the control)
- [x] `sethika:platform` cannot be forged via header, query or body (`JwtTokenServiceEndpointTests`,
      `50bf0e9`). No platform-scoped endpoint exists yet, so a `[HasPermission]` endpoint stands in;
      add the first real platform endpoint to the theory when it lands.
- [x] `switch-institution` to an institution the user holds no role in gives 403, and no token is issued
- [x] a `RequiresMfa` permission gives 403 before step-up, 200 after, and 403 again once the TTL expires
      (`StepUpEndpointsTests`, with the TTL itself asserted at fifteen minutes)
- [x] OTP: a wrong code five times burns it; a fourth send within the hour gives 429
      (`AuthEndpointsTests`, `044976e`). The fifth wrong code answers `otp_attempts_exhausted`,
      not `otp_incorrect` with 0 left.
- [x] sign-in by code (A22, `OtpSignInEndpointsTests`, `2718e1a`): a registered number signs in and
      gets password login's response; an unknown number gets the same answer to the request, the
      same time, no SMS, and cannot sign in; five wrong codes burn it; a code works once; a signup
      code cannot sign in and a sign-in code cannot be used for signup; a fourth request in the hour
      is 429 for both; locked, suspended and unconfirmed accounts; `AccessFailedCount` untouched
- [x] two parallel refreshes with the same token — exactly one succeeds
- [x] signup writes exactly one outbox row, and rolls back all five tables when any part fails
      (`SignUpDonorHandlerTests`)

The three responsibilities A1 transfers from `UserManager` to us, one test each — these guard
omissions, so each must be mutation-tested by removing the call and watching the test fail:

- [x] changing a password rotates `SecurityStamp`, and every refresh token issued before it is refused —
      `POST /api/me/password` (`c4d3ebb`) calls `RotateAsync`; `PasswordChangeEndpointsTests` (`8f7dd40`):
      another device's refresh token gives 401, the cookie in the response refreshes, the old password
      no longer signs in and the new one does. Mutation: `RotateAsync` removed -> that test and the
      revocation test go red. Ran green against SQL Server 2022 and Redis 7.0.
- [x] a successful sign-in resets `AccessFailedCount`, so four failures then a success then four more
      failures does not lock the account
- [x] signing up as `Test@x.com` and then `test@x.com` is rejected as a duplicate, not accepted as a
      second account — and the mutation test showed the collation, not the normaliser, is what
      rejects it here (see A1)

Every one of these must be mutation-tested: break the behaviour on purpose and confirm the test goes
red. Two vacuous-test incidents on this project were caught that way; assume nothing.


### 5.9 — Custom roles API with rank anti-escalation *(added 2026-09-27)*

Backend only; the role-management screens come with week 2's screens.

- [x] `GET /api/institution/role-levels` (`2a3208a`) — the levels strictly below the caller's rank, each
      with its ceiling intersected with the caller's own effective permissions. A Dapper read; the
      commands check again in their transactions, so a stale editor cannot grant more than it showed.
- [x] `POST /api/institution/roles`, `PUT /api/institution/roles/{code}/permissions`,
      `POST /api/institution/roles/{code}/members` and `DELETE …/members/{user}`, under
      `ACCESS.ROLES.CREATE`, `ACCESS.ROLES.EDIT` and `ACCESS.MEMBERS.EDIT` - both EDITs `RequiresMfa`.
      Every one acts for the institution in the token, never one in the request. Roles are named by
      code; a member by their user id, the `sub` their own token already carries (`auth.Users` has no
      other public identifier).
- [x] The three A15 rules checked inside the transaction against rows read in it - the actor's
      roles, the ceiling, `auth.vw_EffectivePermissions` - never the permission cache. Each refusal is
      a `403` naming the offending permissions (`rank_not_junior`, `outside_ceiling`, `not_held`,
      `own_roles`); nothing is dropped from a request. When a role's permissions are replaced, only
      the ones being **added** must be in the ceiling and held: taking one away is within anyone who
      may manage the role. Read committed, not serializable: the window between check and write is a
      few statements, and the cost of closing it is a lock hint Application does not take (A16).
- [x] Setting a role's permissions bumps `Institutions.PermissionsVersion`; assigning or revoking bumps
      the member's `Users.PermissionsVersion`; each writes a `RolePermissionsChanged` outbox row (ids
      only) in the same transaction. Creating a role bumps nothing - nobody holds it yet.
- [x] Assignment only to someone who already holds a role in the institution, and never to oneself.
      Bringing a new person in is an invitation they accept (`org.StaffInvitations`, a later plan),
      not something done to their account. Revoking deactivates the row; the temporal history keeps it.
- [x] Tests, against real SQL (`InstitutionRoleEndpointsTests`): a Manager cannot create a Manager or
      Owner role; cannot grant a permission outside the level's ceiling, nor one they do not hold;
      cannot assign themselves anything; cannot reach another institution's role; a member's
      permissions change at once on edit, assignment and revocation; editing and assigning need a
      step-up. `RbacCatalogueTests` (`92b6187`) covers the ceiling-in-the-view cases.

### 5.10 — Forgotten password, reset by SMS *(added 2026-10-04)*

The phone number is the account's identity, so the reset goes by SMS, not email (§7). Built in
`04e9ae1` (API), `115ccbe` (HTTP tests) and `bf5e618` (screens). Its SQL Server tests were
written where SQL Server could not be installed and first ran after the merge (`525b8f1`):
`PasswordResetEndpointsTests`, 17 tests, green against `Sethika_Test` and Redis in two full runs,
after two test fixes in `e9d3465` and one in the commit after it (warming the host before timing,
and allowing 95% of the floor for Windows timer resolution).

Choices, and why:

- **No sign-in after a reset: `204`, then the user signs in with the new password.** Every
  session from before the reset has just been ended on purpose. Signing in again proves the new
  password was set the way the user meant it, and the reset endpoint stays out of the cookie and
  session code (A2).
- **No enumeration, at any step.** `/otp/request` with `reset` answers `202` for every number.
  Only an account that is active, not deleted, phone-confirmed and not `MfaRequired` gets an SMS.
  Every other number gets `IOtpService.SendDecoyAsync`: the same cooldown, per-number send count
  and stored code as a real send, without the SMS. So the 429 lands on the same request, and a
  guessed code is `otp_incorrect` with the same attempts left, whether or not the number has an
  account. Every reset request waits out a one-second floor (`PasswordResetHandler.RequestFloor`),
  so the SMS hand-off does not show in the timing. **Limit, for the report:** a gateway slower than
  the floor would show through again. Delivering the SMS through the outbox, off the request, is
  the real fix, once the outbox processor exists. For the same reason, a gateway that *throws* for
  a real account is logged and answered `202`, not `500`. `SmsSenderBase` has already marked the
  row `Failed` in `notify.Notifications`.
- **`MfaRequired` accounts (platform staff) cannot reset this way,** and are refused exactly like
  an unknown number. SMS alone would be one factor for the most powerful accounts. Their recovery
  is the `bootstrap` command (Q5), which already treats a suspended or deleted administrator as the
  recovery case.
- **The reset ticket is the A17 pattern, bound to an account, not a number.** Verifying a reset
  code returns 32 random bytes. Only their SHA-256 is stored, as `pwreset:{hash}` →
  `{userId}|{securityStamp}`, for ten minutes. The reset request carries no phone, so the ticket
  has to name the account itself. The number-bound A17 ticket the code check also issues is
  dropped, as step-up drops its own. Any stamp change after the ticket was issued (logout-all,
  another reset) makes it worthless. If a decoy code is ever guessed, the guesser gets a ticket
  that was never stored. It looks like any other ticket and no reset accepts it.
- **The ticket is looked at, then spent.** The password policy runs first, with signup's errors and
  signup's personal data (phone, email, name). A refused password costs a correction, not another
  SMS, the same order signup keeps. Then a Lua GET+DEL spends the ticket before anything is
  written, so it is gone even if a later step fails. The write is
  `UPDATE … WHERE Id = @id AND SecurityStamp = @ticketStamp` plus the eligibility conditions, so
  a logout-all or suspension landing in between still wins.
- **One transaction:** the new hash and `PasswordChangedAt`, `AccessFailedCount = 0`,
  `LockoutEnd = NULL`, `SecurityStamps.RotateAsync` (revoked reason `PasswordReset`), and a
  `PasswordReset` outbox row holding the user id only (the `DonorSignedUp` precedent). Its consumer
  is the "your password was changed" SMS, which does not exist yet. `audit.AuditLogs` is not
  written, because nothing in the codebase writes it yet. `auth.PasswordHistory` is written and
  read since 2026-10-11 (A21's password-history bullet).
- **Rate limits:** reset has its own per-client request and verify budgets and its own per-phone
  verify budget (`otp-reset-*`), so signups from one address cannot use up the budget their users
  need to recover an account. OtpService's per-number send counter was already keyed per purpose.
  `/password/reset` is limited to 20 an hour per client, because each attempt that gets past the
  ticket check costs a 210 000-iteration hash.
- **The SMS goes in the account's `PreferredLanguage`,** not the page's. `notify.Templates` is not
  seeded: `OTP_RESET`, added when the branches were merged (see the 5.6 templates box).

- [x] `/api/auth/otp/request` and `/otp/verify` accept `reset`; `POST /api/auth/password/reset
      { ticket, newPassword }` answers `204` (`04e9ae1`). Anonymous and rate-limited like the rest of
      `/api/auth`: the ticket is the gate.
- [x] Full reset over HTTP: request, verify, reset. The old password fails, the new one signs in, and
      a refresh token from before the reset gives `401`, revoked `PasswordReset`. One outbox row,
      ids only. (`AForgottenPasswordIsResetBySms_AndEverySessionFromBeforeItEnds`)
- [x] An unknown number and an `MfaRequired` account get the same status, body and timing as a real
      number, no SMS, and the same answer to a guessed code. An `MfaRequired` account cannot reset
      even with a ticket planted in Redis. Suspended, deactivated and deleted accounts get no code.
      An SMS gateway that throws for a real account is still answered `202`
      (`AGatewayFailureIsAnsweredLikeEveryOtherNumber`).
- [x] A ticket works once. A ticket from before a logout-all is refused. A refused password does not
      spend the ticket.
- [x] A signup code or ticket cannot be used for a reset, and a reset code or ticket cannot be used
      for signup.
- [x] Five wrong codes burn the code. A fourth request within the hour is `429`, for a real number and
      an unknown one alike.
- [x] A locked account can reset and then signs in with the new password. A reset restarts the
      failure count.
- [x] The mutations listed in `115ccbe` (remove `RotateAsync`, a reusable ticket, no purpose check, an
      SMS for unknown numbers, `MfaRequired` let through, the lockout not cleared, the failure count
      not reset), each turning its test red, against SQL Server and Redis:
      `RotateAsync` removed, `MfaRequired` let through and the lockout not cleared are killed. The
      reusable ticket (the Lua `DEL` removed) **survives**: the second use is refused anyway,
      because the ticket carries the old `SecurityStamp` and the reset rotates it. The stamp
      condition in the `UPDATE` is what makes a ticket single-use; the `DEL` is a second line. The
      last three ran on 2026-10-11 and are killed: the code checked as a signup code
      (`SignupAndResetCodesAndTicketsDoNotCross`), a real SMS for an unknown number
      (`UnknownAndMfaRequiredNumbersGetTheSameAnswerAsARealOne_AndNoSms`), and the failure count not
      reset (`AResetStartsTheFailureCountAgain`).
- [x] Password history (§8): a reset to the forgotten password, or to any of the four before it, is
      `password_rejected` with `RecentlyUsed` and does not spend the ticket; the replaced hash is
      remembered (`TheForgottenPasswordCannotBeSetAgain_AndTheRefusalDoesNotSpendTheTicket`).
- [ ] Web (`bf5e618`): "Forgot password?" on `/login` → `/forgot-password`. Phone, then code, then
      new password twice. Success lands on `/login` with a toast. Strings in en/si/ta, with si/ta
      drafts in `docs/translations-to-review.md`. Lint, build and 21 Vitest tests are green (11
      mutations killed), and a 320 px pass in all three languages was made against mocked
      responses. **Still owed:** the same walk against the live API, with the SMS from the console
      sender arriving in the account's language.

Already green against real Redis (`04e9ae1`, run twice): the decoy keeps the real cooldown, send
cap and guess answer and sends nothing; signup and reset codes have separate keys and caps; the
reset SMS names its purpose in all three languages; the ticket store redeems once (also under eight
concurrent redemptions), peeks without spending, keeps only a hash with a TTL, and fails closed.
Ten mutations, all killed.

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
acceptance, impersonation (`auth.ImpersonationSessions` exists but is Phase-optional), the
role-management **screens** (the API and its anti-escalation are §5.9), a screen for the platform to
edit levels and ceilings (seeded only, for now), and enforcing `auth.PermissionDependencies` at grant
time. All have tables already; none block week 2.

## 8. Review of the slice, 2026-10-11

Every handler, endpoint and the browser session client were read again against this plan before
the PR. Six findings, each fixed with a test and its mutations killed (15 .NET, 7 web):

| # | Finding | Fix | Test (mutation killed) |
|---|---|---|---|
| 1 | **SMS sign-in made the SIM the whole account for `MfaRequired` users.** The reset refused them for exactly this reason; A22 did not, and the step-up code goes to the same phone. | Not sent a code, answered like an unknown number; a code from before the flag is refused (A22). | `AnMfaRequiredAccountIsSentNoCode_…`, `ACodeHeldByAnMfaRequiredAccountIsRefusedLikeAWrongOne` |
| 2 | **A right password in a parallel burst could clear the lockout the wrong ones set.** Every request read the account unlocked; the success path's reset was unconditional. Same in password change. | The reset `UPDATE` is conditional on `LockoutEnd` being past; zero rows rolls back and answers `LockedOut` with its history row. | `ARightPasswordCannotClearALockSetWhileItWasBeingChecked` (sign-in), `ARightCurrentPasswordCannotClearALock…` (change), both forced with an `UPDLOCK` held while the lock is set |
| 3 | **An OTP hash and its expiry were two Redis calls.** A process dying between them leaves a live code that never expires - the case `RedisWindowCounter` already guards for the send counter. | One Lua step (`HMSET` + `PEXPIRE`; Redis 3.2 has no multi-field `HSET`). | `OtpServiceTests` TTL assertions (3 fail with `PEXPIRE` removed). The crash itself cannot be driven by a test. |
| 4 | **Logout and logout-all were not rate-limited** - an anonymous hash and query per request. | Both spend `refresh-client`. | `LogoutsSpendTheRefreshBudgetOfTheirClient` |
| 5 | **`auth.PasswordHistory` was never written** (A21 "not done"), and a reset could set the forgotten password again. | `PasswordReuse`: current + last four refused in change and reset; `IX_PasswordHistory_User_Time` added in the SQL project. | `TheLastFivePasswordsCannotComeBack_TheSixthCan`, `TheForgottenPasswordCannotBeSetAgain_…` |
| 6 | **A11's account language was never read back.** | `GET /api/me` (Dapper); sign-in lands in the account's language; boot moves to it. | `MeLanguageEndpointsTests` (3 new), `account-locale.test.ts`, `api.test.ts` |

Checked and left as they are, with the reason:

- **Refresh families have no absolute lifetime.** Each rotation sets a fresh 14 days, so a device
  used once a fortnight stays signed in indefinitely. Common practice, but a cap (e.g. 90 days from
  the family's first token) is a small change in `SessionHandler` if the report wants one.
- **Spent and expired refresh tokens are never deleted.** A cleanup job belongs with Hangfire,
  which does not exist yet.
- **Step-up verify is limited per client, not per user.** The per-code burn (5) and the per-number
  send cap (3 an hour) already bound guessing to 15 an hour per account.
- **Still owed and not something a review can close:** the live browser walk-throughs of 5.7 and
  5.10, a native speaker's sign-off on `docs/translations-to-review.md`, and the user's review of
  the `10_RbacCatalogue.sql` ceilings.
