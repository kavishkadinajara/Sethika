# Plan F — Mobile app (donors first)

Status: **F.1 and F.2 built** (branch `ccr-79f10240-uk4n3v`, merged); **F.3 and F.4 built** (branch
`mobile-booking`, awaiting review); F.5 and F.7 can start (plan 03 merged), F.6 waits for Phase B. Added to scope on 2026-10-24 after the supervisor suggested that a
mobile application part would strengthen the project. It is built **in parallel** with the backend
phases: the slices that need no new API start now, and each later slice starts when the API it calls
has merged into `develop` (§7).
Last updated 2026-10-25. Update this line and the §8 boxes whenever either changes.

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
| `POST /api/auth/mobile/login`, `.../otp/sign-in`, `.../signup` | **Built (F.2).** The same handlers as `/api/auth/login`, `/otp/sign-in`, `/signup`, mapped a second time by `AuthEndpoints.MapSessionStarters`; the response is `MobileSessionResponse` (the web's body plus `refreshToken`, `refreshTokenExpiresAt`) and no `Set-Cookie`. Same rate-limit rules, so the same budgets. Code requests, code checks and password reset start no session, so the app calls the ordinary `/api/auth/otp/request`, `/otp/verify`, `/password/reset`. |
| `POST /api/auth/mobile/session/refresh`, `.../logout`, `.../logout-all`, `.../switch-institution` | **Built (F.2).** `{ refreshToken }` in the body. `SessionEndpoints.RotateAsync` is shared; same `409 session_superseded` / `401 session_expired`, with no cookie cleared. These routes never read a cookie, which is why they need no CSRF token (`MobileSessionEndpointsTests.TheMobileRoutesNeverReadTheWebCookie`). |
| `PUT /api/me/devices` | Register or refresh the current device's push token (upsert on `ExpoPushToken`, moving it to the caller if another user had it). Signed-in only. |
| `DELETE /api/me/devices/{token}` | Called on logout; also done server-side on logout-all. |
| Everything else | The app calls the existing endpoints (`/api/institutions`, `/api/bookings/...`, `/api/institution/...`) unchanged. |

## 5. Screens (en/si/ta, 320 px wide up, light and dark)

Tabs: **Explore** (institutions, later "near me"), **My bookings**, **Profile** (language, notifications,
sign out). Stack screens: institution page → month availability → book; booking detail with cancel.
Staff with the right permission see a **Today** tab: today's bookings for the active institution and a
"served" button. English uses Outfit; Sinhala and Tamil use the phone's own font (Noto Sans Sinhala / Tamil on Android, the system faces on iOS) rather than bundling more fonts.

## 6. Out of scope

Slot and calendar management, onboarding and review, roles and permissions screens, reports,
donations and PayHere (Phase D), recurring bookings and the Memorial Dana Planner (Phase C), offline
booking, an iOS store release, tablet layouts. Phase E's leader schedule (accept / decline on the
phone) is a natural follow-up but not promised.

## 7. Work breakdown and when each slice can start

| Slice | Needs | Can start |
|---|---|---|
| F.1 Scaffold `mobile/`: Expo Router, theme tokens, i18n en/si/ta, API client with base URL from config, lint + type-check + Jest | nothing | **Done** |
| F.2 Mobile sessions: §4 auth endpoints + tests over HTTP; sign-in / OTP / signup screens; secure store; single-flight silent refresh (same rules as `web/lib/auth/session-client.ts`) | auth (merged) | **Done** |
| F.3 Explore + institution page + month availability | plan 03 §5.5 GET endpoints | **Built** (`mobile-booking`) |
| F.4 Book, my bookings, cancel | plan 03 §5.5 booking endpoints | **Built** (`mobile-booking`) |
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

F.1 + F.2, 2026-10-24: lint, typecheck and Jest clean (25 tests); `MobileSessionEndpointsTests` 13
over HTTP, three mutations all killed, full backend suite green (628); `npx expo export --platform
android` bundles; the session client was driven end to end against a running API (signup, cold-start
refresh, spent token refused, logout). **Still owed:** the walk-through on a real Android phone (no
device in the build environment), and a native review of the `App.*` Sinhala and Tamil.

F.3 + F.4, 2026-10-25 (branch `mobile-booking`): lint and typecheck clean; Jest 96 tests in 15
suites (71 new: calendar maths, availability to bowl state, error code to message, upcoming/past,
the sign-in return path, the API client, and the Explore, institution page, booking sheet and My
bookings screens with the API stubbed at the app's client boundary). 34 mutations that compile,
every one seen red (a 35th broke the type-check, so it proved nothing and was replaced); listed in
the commit. `npx expo export --platform android` bundles. No backend change; the API was not run (the
Phase E session holds it). **Still owed:** the real-phone walk-through (below), a native review of
the new `App.*` strings (`docs/translations-to-review.md`, Phase F section), and the Plans table
in `docs/DECISIONS.md` (left to the session that owns that file).

- [ ] `cd mobile && npm run lint && npm run typecheck && npm test` clean
- [ ] Backend part (if any): `dotnet build` with no warnings, tests green
- [ ] Walked through on a real Android phone in all three languages, light and dark
- [ ] This file's status line and the Plans table in `docs/DECISIONS.md` updated

## 9. Decided while building F.3 and F.4 (2026-10-25)

| # | Decision | Why |
|---|----------|-----|
| F9 | **The tabs and institution pages are open to anyone.** `Stack.Protected` is gone from the root layout: Explore, `/i/{slug}`, My bookings and Profile render for a visitor, and each screen that needs an account says so where it is needed (the booking sheet, My bookings, Profile's sign-in buttons). The sign-in screens are pushed on top of whatever the visitor was doing and leave themselves on success with `router.dismissTo(next ?? "/")` (`auth/leave-sign-in.ts`). Signing out leaves the donor where they are, signed out | Plan 03 C10 makes browsing anonymous, and F.2's guard sent every visitor to sign-in first. A guarded sign-in screen also answers its own success with a `<Redirect>` (a replace) to the guard's target, which would have dropped the institution page the donor came from; leaving by `dismissTo` goes back down the stack to it instead |
| F10 | **Back to the same day after sign-in (E33)** two ways: the sheet's sign-in carries `next=/i/{slug}?slot={publicId}&date=yyyy-MM-dd`, which `dismissTo` hands back to the institution page as its params, and the page also remembers the day in memory and reopens the sheet when the session starts. `next` is accepted only if it is an `/i/...` path (`safeReturnPath`) | The params survive the page being created afresh (opened from a link, or the stack rebuilt); the memory covers the page that simply stayed under the sign-in screen. Only public ids travel. Checking `next` stops a crafted link using sign-in to send someone elsewhere |
| F11 | **Month and weekday names come from the messages in all three languages, never from `Intl`.** The names are `Booking.dates` (copied from the web), the order of a date's parts is `App.dates.*` | Hermes implements `Intl` on the phone's own ICU (Android) or Foundation (iOS), so the words depend on the phone's OS version; only `hermesc` ships in `node_modules`, so Hermes could not be run here to check. Node 22's ICU 78 (CLDR 48) already names the Sinhala months දුරුතු ... උඳුවප් where the web and older phones say ජනවාරි ... දෙසැම්බර්. The web keeps `Intl` for English and Tamil because a browser's `Intl` is its own; a phone's is not |
| F12 | Money is written as `LKR 4,500` by the app, not `Intl.NumberFormat` | The same reason as F11, and the ISO code reads the same in all three languages (the web's Sinhala page shows the same, Chromium having no Sinhala data) |
| F13 | **No occasion field** in the booking form | Plan 03 E3: there are no `OCCASION` lookup values, and the API's booking body has no occasion. "In memory of" and notes are there |
| F14 | The bowl is drawn with Views, the calendar runs edge to edge (seven 45 pt days at 320 pt), and a day that is not open can still be tapped: it says why under the calendar ("Friday, 20 November 2026: Full"). A full day is a full bowl and a struck-through date | No SVG library is added for one shape. The touch target needs the whole width. The picture is never the only signal: every day's state is in its accessibility label, in the tapped-day line and in the legend |
| F15 | The booking sheet and the cancel sheet are React Native `Modal`s (page sheet); choosing sign-in closes the sheet first | A screen pushed while a modal is open would open under it |
| F16 | **Component tests use `test-renderer`**, which jest-expo already installs, through a small helper (`src/test-utils/render.tsx`), not React Native Testing Library | Adding RNTL with `npm install` (npm 10 and 11 alike) rewrote `package-lock.json` and dropped `@react-native/jest-preset` (a required peer of jest-expo; Jest stops), `@react-native/babel-preset` and `@react-native/metro-config` (peers of react-native-worklets) with about 30 other entries. That lockfile fragility is its own decision; until it is made, no package is added |
| F17 | The calendar's month arrows stop at this month and at the month of the slot's last bookable day (`MaxAdvanceDays`); opening on a month with no open day moves on once, as on the web | The API judges every day; the arrows only stop the donor paging into months that cannot hold an open day |
