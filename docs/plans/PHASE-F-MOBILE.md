# Plan F — Mobile app (donors first)

Status: **Designed, not started.** Added to scope on 2026-10-24 after the supervisor suggested that a
mobile application part would strengthen the project. It is built **in parallel** with the backend
phases: the slices that need no new API start now, and each later slice starts when the API it calls
has merged into `develop` (§7). It gets a number and a branch when work starts.
Last updated 2026-10-24. Update this line and the §8 boxes whenever either changes.

---

## 0. What the app promises (the report must match this list)

1. A **donor** can sign up and sign in (phone OTP, password), in English, Sinhala or Tamil.
2. A donor can **browse institutions**, see a slot's availability for a month, **book**, see
   **my bookings** and **cancel** one.
3. A donor gets **push notifications**: booking confirmed / declined / cancelled and the day-before
   reminder, in their language, never twice.
4. A donor can **find institutions near them** using the phone's location (once Phase B search exists).
5. An institution's staff can open **today's sponsors** and **mark a booking served**, if their role
   allows it. Everything else on the institution side (slot setup, onboarding, review, roles,
   reports) stays on the web.

Anything not on this list is out of scope (§6). If the build drops an item, change the report, not
just the code.

## 1. What it reuses

| Existing piece | How Phase F uses it |
|---|---|
| The one ASP.NET Core API | The app is another client of the same endpoints. **No business logic in the app** (the A2 rule, applied to mobile) and no mobile-only business endpoints. |
| `[HasPermission]`, `vw_EffectivePermissions`, the permission list the web loads | The app hides what the user cannot do, exactly like the web. The API check is still the security. |
| Session handling (`SessionHandler`): rotation, `FamilyId` reuse detection, A19 grace, `SecurityStamp` | Unchanged. Only how the refresh token travels changes (F3). |
| Outbox + Hangfire + `DedupKey`; `notify.Templates` / `notify.Notifications` already allow `Channel = 'Push'` | Push is one more channel in the same processor (F4). |
| `auth.Users.PreferredLanguage`; `web/messages/{en,si,ta}.json` | Same language codes. The app keeps its own message files; strings the two share are copied, not imported across folders. |
| Branding tokens (Estate Green, Offering Gold, Deep Ink, Rice Ivory; Outfit) | Same colours in light and dark, same faith-neutral rule. |

## 2. Decisions

| # | Decision | Why |
|---|----------|-----|
| F1 | **React Native with Expo (SDK current at start), TypeScript strict, Expo Router**, in a new top-level folder `mobile/` beside `web/` | Same language and React skills as `web/`, one codebase for Android and iOS, and Expo handles push and builds without native tooling on the developer's machine. Rejected: Flutter (a second language, Dart, and nothing shared with `web/`); .NET MAUI (same language as the API, but a small ecosystem and harder maps and push); a PWA only (cheapest, but weak as "a mobile application" in the report, and iOS push is limited). |
| F2 | **Donor-first.** The institution side is limited to today's sponsors and marking a booking served | Donors are the ones on phones; institution admin work is form-heavy and already built on the web. Two full apps would double the screens for little report value. |
| F3 | **Mobile sessions carry the refresh token in the body, not a cookie.** New endpoints under `/api/auth/mobile/...` return `{ accessToken, refreshToken }` and accept the refresh token in the request body; the app keeps it in `expo-secure-store` (Keychain / Keystore). These endpoints **never read or set cookies**, so they need no CSRF token. Same table, same rotation, same reuse detection, `DeviceInfo` filled from the app. | The web's httpOnly cookie (A2) protects against script reading the token; a native app has no injected page script and no shared cookie jar, and cookies in React Native are unreliable. A body token can only be obtained with the user's credentials or a valid refresh token, so offering it does not weaken the web session. The web endpoints stay exactly as they are. |
| F4 | **Push through the Expo Push Service** (it forwards to FCM and APNs). New table `notify.MobileDevices`; the outbox processor gets a push sender that calls Expo's HTTP API with `IHttpClientFactory` (no new NuGet package). A `DeviceNotRegistered` receipt disables the row. | Free, one API for both platforms, no Firebase SDK in the backend. `notify.PushSubscriptions` stays for web push (VAPID): its shape (`Endpoint`, `P256dh`, `AuthKey`) does not fit a device token. |
| F5 | SMS stays the guaranteed channel for booking messages; push is **in addition**, controlled by `notify.UserPreferences` | Not every donor installs the app, and plan 03 promises the SMS. Turning SMS off for app users is a later cost decision, not this phase's. |
| F6 | The app has **no offline writes**. Reads may show the last loaded data with a "last updated" time | A booking is decided by the server under a lock (plan 03 C2). Queuing one offline would promise a place the server may refuse. |
| F7 | Distribution: an **Android APK from EAS Build** for the demo and testers; iOS through a development build only if a Mac/Apple account is available | An APK installs on any Android phone without a store account. Push needs a development build, not Expo Go (Expo Go dropped remote push on Android). |
| F8 | Map library is chosen in the nearby slice (F.6), not now. Default: a list sorted by distance first, map second | `react-native-maps` needs a Google Maps key on Android; MapLibre with an OSM-based tile source avoids it but needs a development build. The list works without either. |

## 3. Schema (add to `src/Sethika.Database`; D10 rules apply)

| Table | Key columns and rules |
|---|---|
| `notify.MobileDevices` | `Id INT IDENTITY`, `UserId → auth.Users`, `ExpoPushToken VARCHAR(200) NOT NULL` (unique), `Platform VARCHAR(10)` (named CHECK: `'Android' OR 'iOS'`), `AppVersion VARCHAR(20)`, `CreatedAt`, `LastSeenAt` (`DATETIME2(3)` + `SYSUTCDATETIME()`), `DisabledAt NULL`, `DisabledReason NVARCHAR(100) NULL`. Index `(UserId) WHERE DisabledAt IS NULL`. Not tenant-owned (a donor's device), so no `InstitutionId` and no RLS predicate, like `auth.RefreshTokens`. |

New `notify.Templates` rows with `Channel = 'Push'` for the plan 03 booking events × en/si/ta
(Sethika's own wording, not drafts; si/ta go to `docs/translations-to-review.md`).

## 4. API additions (all go in the owning module's plan when built)

| Endpoint | Rule |
|---|---|
| `POST /api/auth/mobile/sign-in`, `.../otp/verify`, `.../signup/complete` | Same handlers as the web endpoints; the response carries the refresh token in the body instead of `Set-Cookie`. Same rate limits. |
| `POST /api/auth/mobile/session/refresh`, `.../logout`, `.../logout-all`, `.../switch-institution` | Refresh token in the body. Same `SessionHandler`, same `409 session_superseded` / `401 session_expired`. |
| `PUT /api/me/devices` | Register or refresh the current device's push token (upsert on `ExpoPushToken`, moving it to the caller if another user had it). Signed-in only. |
| `DELETE /api/me/devices/{token}` | Called on logout; also done server-side on logout-all. |
| Everything else | The app calls the existing endpoints (`/api/institutions`, `/api/bookings/...`, `/api/institution/...`) unchanged. |

## 5. Screens (en/si/ta, 320 px wide up, light and dark)

Tabs: **Explore** (institutions, later "near me"), **My bookings**, **Profile** (language, notifications,
sign out). Stack screens: institution page → month availability → book; booking detail with cancel.
Staff with the right permission see a **Today** tab: today's bookings for the active institution and a
"served" button. Sinhala and Tamil use Noto Sans Sinhala / Noto Sans Tamil next to Outfit.

## 6. Out of scope

Slot and calendar management, onboarding and review, roles and permissions screens, reports,
donations and PayHere (Phase D), recurring bookings and the Memorial Dana Planner (Phase C), offline
booking, an iOS store release, tablet layouts. Phase E's leader schedule (accept / decline on the
phone) is a natural follow-up but not promised.

## 7. Work breakdown and when each slice can start

| Slice | Needs | Can start |
|---|---|---|
| F.1 Scaffold `mobile/`: Expo Router, theme tokens, i18n en/si/ta, API client with base URL from config, lint + type-check + Jest | nothing | **now** |
| F.2 Mobile sessions: §4 auth endpoints + tests over HTTP; sign-in / OTP / signup screens; secure store; single-flight silent refresh (same rules as `web/lib/auth/session-client.ts`) | auth (merged) | **now** |
| F.3 Explore + institution page + month availability | plan 03 §5.5 GET endpoints | when plan 03 merges |
| F.4 Book, my bookings, cancel | plan 03 §5.5 booking endpoints | when plan 03 merges |
| F.5 Push: `notify.MobileDevices`, device endpoints, outbox push sender, push templates, dedup tests | plan 03 §5.6 notifications | when plan 03 merges |
| F.6 Near me (location permission, distance list, then map) | Phase B search + Redis GEO | when Phase B merges |
| F.7 Staff "Today" tab + mark served | plan 03 institution API + a `served` transition | when plan 03 merges |

Branch: one per slice group, numbered like the others when cut (F.1–F.2 first). Rules for working in
parallel:

- The app **never drives an API change in another module's branch.** If a screen needs something the
  API does not give, it is added to that module's plan and built there.
- Backend changes made for Phase F (F.2, F.5) come with their own tests, like every other slice.
- Local testing from a phone: the API must listen on the LAN address (or a tunnel), not `localhost`.

## 8. Definition of done (per slice)

- [ ] `cd mobile && npm run lint && npm run typecheck && npm test` clean
- [ ] Backend part (if any): `dotnet build` with no warnings, tests green
- [ ] Walked through on a real Android phone in all three languages, light and dark
- [ ] This file's status line and the Plans table in `docs/DECISIONS.md` updated
