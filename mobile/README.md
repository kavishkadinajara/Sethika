# Sethika mobile app (Phase F)

The donor app: React Native + Expo (SDK 58), TypeScript strict, Expo Router. It is another client of
the same ASP.NET Core API as `web/` and holds **no business logic** — every rule lives in the API.
Design and scope: [`docs/plans/PHASE-F-MOBILE.md`](../docs/plans/PHASE-F-MOBILE.md).

## What is built (F.1 to F.4)

- Sign in with a password or a texted code; donor signup (number → code → details → consent).
- Tabs, open to anyone: Explore (the listed institutions, with a filter), My bookings (asks a
  visitor to sign in), Profile (language; sign in, or sign out and sign out on all devices).
- An institution's page at `/i/{slug}`: its meals and a month of each meal's days as bowls
  (empty, filling, full), with the reason for every day that is not open.
- Booking from a chosen day, asking for a sign-in only then and coming back to the same day;
  My bookings (coming up, earlier) with cancel where the API allows it.
- English, Sinhala and Tamil; light and dark, in the web's colours.
- Sessions through `/api/auth/mobile/*`: the refresh token is kept in the iOS Keychain / Android
  Keystore (`expo-secure-store`) and sent in the request body, never as a cookie
  (`src/auth/session-client.ts`, plan F3).

## Run it

1. Start the API so the phone can reach it (from the repo root):
   `dotnet run --project src/Sethika.Api --launch-profile http --urls http://0.0.0.0:5182`
2. `npm install` in this folder.
3. Tell the app where the API is. An emulator needs nothing: Android's emulator reaches the host
   as `10.0.2.2:5182` and the iOS simulator as `localhost:5182`. A **real phone** on the same
   Wi-Fi needs the computer's LAN address in `mobile/.env.local` (git-ignored):
   `EXPO_PUBLIC_API_BASE_URL=http://192.168.1.20:5182`
4. `npx expo start`, then press `a` for an Android emulator, or scan the QR code with the
   **Expo Go** app on a phone.

`EXPO_PUBLIC_*` values are compiled into the app, so never put a secret there.

With the API in Development the SMS codes are printed in the API's console (`Sms:Provider = Console`).

## Checks

```bash
npm run lint        # ESLint (eslint-config-expo)
npm run typecheck   # tsc --noEmit
npm test            # Jest (jest-expo): the session client, i18n, the booking logic and screens
```

CI runs all three (`.github/workflows/ci.yml`, job `mobile`).

## Adding packages

Use `npx expo install <package>`, not `npm install`: it picks the version that matches the Expo
SDK. Native modules beyond Expo Go's set need a development build (`npx eas-cli build --profile
development`). That is why Dependabot does not watch this folder — an SDK upgrade is
`npx expo install expo@^<next> --fix`, done by hand.

## Strings

`src/i18n/messages/{en,si,ta}.json` are ICU messages, formatted with `intl-messageformat` like the
web. The sections shared with the web (`Login`, `Signup`, `Errors`, `Booking`, `Discover`,
`MyBookings`, …) are **copied from `web/messages`**: change a shared string there first, then copy
it here. `App.*` is the app's own. Month and weekday names always come from `Booking.dates`, never
from `Intl` (`src/bookings/dates.ts` says why).

Component tests render with `test-renderer` (already installed by jest-expo) through
`src/test-utils/render.tsx`, and stub the API at the app's own boundary
(`src/test-utils/api-stub.ts`).
Every Sinhala and Tamil string is a draft until reviewed (`docs/translations-to-review.md`).

## Building an APK for testers

`npx eas-cli build --platform android --profile preview` (needs a free Expo account and an
`eas.json`, added when the first build is made).

Push needs a development build, the EAS project id in `app.json` (`extra.eas.projectId`) and
`google-services.json` (`android.googleServicesFile`). Without them the app runs with no push: Expo
Go skips it entirely, and a build with no project id fails to get a token, quietly.
