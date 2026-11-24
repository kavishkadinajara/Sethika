# Deployment

Production since 2026-11-24. Web: **https://sethika.kavishkadinajara.com** (Vercel).
API: `https://sethika-api-nhnfg.azurewebsites.net` (Azure App Service). The browser only ever talks
to the web's origin; `/api/*` is proxied to the API from the web server (plan A3), so the refresh
cookie stays first-party and the API needs no CORS.

## What runs where

| Part | Service | Name | Size |
|---|---|---|---|
| Web (Next.js) | Vercel, project `sethika` | `sethika.kavishkadinajara.com`, `sethika.vercel.app` | Hobby |
| API (+ Hangfire, SignalR) | Azure App Service, Linux, .NET 10 | `sethika-api-nhnfg` on plan `plan-sethika` | B1 |
| Database | Azure SQL Database | `sethika-sql-nhnfg` / `Sethika` | S0 (10 DTU) |
| Redis | Azure Managed Redis | `sethika-cache-nhnfg` (port 10000, TLS) | Balanced B0 |

All Azure resources are in the resource group `rg-sethika`, region **Central India** (the closest
region to Sri Lanka in which the subscription had App Service quota). DNS for the web's domain is on
Cloudflare: `sethika` CNAME to Vercel, *DNS only* (not proxied), and a `_vercel` TXT record that
proves the domain to Vercel.

Choices worth knowing:
- **SQL S0, not the serverless free offer.** Hangfire polls the database every 15 seconds, so a
  serverless database never pauses and the free monthly allowance would be gone in two or three days.
- **Azure Managed Redis, not Azure Cache for Redis**, which no longer accepts new caches (it is
  being retired). *EnterpriseCluster* policy, so the app sees one Redis as it does locally (SignalR
  backplane, multi-key operations); access keys on; eviction off, so locks and one-time codes are
  never evicted under memory pressure.
- **App Service:** WebSockets on (SignalR), Always On (Hangfire), health check `/health`, HTTPS
  only, FTPS off, TLS 1.2.

## The API's database user

The API does not connect as the server admin. It uses a contained database user, `sethika_api`:
`db_datareader`, `db_datawriter`, `EXECUTE` (procedures, functions, table types), owner of the
`HangFire` schema with `CREATE TABLE` (so Hangfire can create and upgrade its own tables there and
nowhere else), and `UNMASK` on the masked columns the API reads in its work. That is all ten of
them (`sys.masked_columns`): `auth.Users` Email, NicNumber, PhoneNumber; `booking.Bookings`
DonorPhone; `booking.ServiceAppointments` DonorPhone; `giving.Donations` DonorPhone;
`org.InstitutionDonors` Phone; `org.RegistrationRequestOfficeBearers` NicNumber;
`org.RegistrationRequests` ApplicantNic; `org.ReligiousLeaders` Phone. A person querying the
database with any other login sees them masked.

```sql
CREATE USER sethika_api WITH PASSWORD = '<generated>';
ALTER ROLE db_datareader ADD MEMBER sethika_api;
ALTER ROLE db_datawriter ADD MEMBER sethika_api;
GRANT EXECUTE TO sethika_api;
CREATE SCHEMA HangFire AUTHORIZATION sethika_api;
GRANT CREATE TABLE TO sethika_api;
GRANT UNMASK ON auth.Users(PhoneNumber) TO sethika_api;   -- and the other nine columns above
```

## Configuration

App settings on the App Service (encrypted at rest by Azure; never in the repository):

| Setting | Value |
|---|---|
| `ConnectionStrings__Sql` | `sethika_api` on `Sethika`, `Encrypt=True` |
| `ConnectionStrings__Redis` | `<host>:10000,password=<key>,ssl=True,abortConnect=False` |
| `Jwt__SigningKey` | 64 random bytes, base64 (production only, not the development key) |
| `Payments__CurrentKeyId`, `Payments__Keys__prod1` | 32 random bytes, base64 |
| `Payments__PayHere__Sandbox` | `true` |
| `Payments__PayHere__ApiPublicUrl` / `WebPublicUrl` | the API's address / `https://sethika.kavishkadinajara.com` |
| `Sms__Provider`, `Sms__TextLkApiToken` | `TextLk` (the console sender is refused outside Development) |
| `Push__Provider` | `Expo` |
| `Files__LocalRoot` | `/home/data/sethika-files` (App Service keeps `/home` across restarts) |

Vercel: `API_BASE_URL` = the API's address (server-only; never `NEXT_PUBLIC_`).
The generated passwords and keys are kept outside the repository by the project owner.

**Every one-time code the production API sends is a real text and costs Text.lk credit.** Run the
browser tests against the local API, never against production.

## Release pipeline

A release is a merge into `main`.

1. **CI** (`.github/workflows/ci.yml`) runs on the push: backend build, format and tests against a
   real SQL Server and Redis; web lint, type check and build; mobile lint, type check and tests.
2. **Deploy** (`.github/workflows/deploy.yml`) starts only when that CI run passed, and deploys
   only what the merge changed:
   - **Database**, when `src/Sethika.Database/` changed: builds the dacpac, opens the SQL
     firewall for the runner's address, publishes with `BlockOnPossibleDataLoss=true` (a change
     that would drop or narrow data fails the release instead of running) and
     `DropObjectsNotInSource=false`, and closes the firewall again even on failure.
   - **API**, when anything else under `src/` changed: after the database when both changed,
     `dotnet publish`, deploy to the App Service, then `/health` must answer.
   - "Run workflow" on the Actions tab deploys either part by hand.
3. **Web**: Vercel's Git integration builds `web/` on every push to `main` (production branch)
   and skips the build when nothing under `web/` changed and for every other branch.

Azure sign-in from GitHub is OpenID Connect: the app registration `github-sethika-deploy` trusts
only tokens for this repository's `production` environment, and holds Contributor on `rg-sethika`
only. No Azure password is stored in GitHub. The `production` environment's secrets are
`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` and `SQL_ADMIN_CONNECTION_STRING`
(used by the schema publish only).

## Mobile app and push notifications

The Android app is built by **EAS Build** (Expo project `@kavi09d/sethika`) as an APK for direct
install: `cd mobile && npx eas-cli@latest build -p android --profile production`. The `production`
profile points the app straight at the Azure API (no proxy, so SignalR gets WebSockets) and uses the
EAS `production` environment. EAS keeps the Android signing key, so every build installs over the
previous one. EAS archives the whole repository; the root `.easignore` keeps everything but
`mobile/` out of the upload.

Push goes **API outbox → Expo Push Service → Firebase Cloud Messaging → phone**:

| Piece | Where it lives |
|---|---|
| Firebase project `sethika-10955`, Android app `com.sethika.app` | Firebase console |
| `google-services.json` (which Firebase project the app belongs to) | EAS secret file variable `GOOGLE_SERVICES_JSON` (environment `production`); `mobile/app.config.ts` reads it at build time. Never in the repository |
| FCM V1 service-account key (lets Expo send through Firebase) | EAS project credentials, Android, FCM V1 |
| A phone's Expo push token | `notify.MobileDevices`, registered by the app through `PUT /api/me/devices` |

The API needs no push key of its own (`Push:Provider = Expo`). The `push-receipts` job reads Expo's
delivery receipts and disables tokens of phones that no longer exist.

## By hand

The first deployment, and the steps no pipeline repeats:

```bash
# schema (from the repository root)
dotnet build src/Sethika.Database
sqlpackage /Action:Publish /SourceFile:src/Sethika.Database/bin/Debug/Sethika.Database.dacpac \
  /TargetConnectionString:"<admin connection string>" /p:AllowIncompatiblePlatform=true

# API
dotnet publish src/Sethika.Api -c Release -o publish
cd publish && tar -a -c -f ../api.zip * && cd ..      # Windows: not Compress-Archive (see below)
az webapp deploy -g rg-sethika -n sethika-api-nhnfg --src-path api.zip --type zip --clean true

# first platform administrator: the published API's bootstrap command, run once against
# production with the App Service settings plus Bootstrap__PhoneNumber / __Password / __FullName
dotnet Sethika.Api.dll bootstrap
```

`Compress-Archive` in Windows PowerShell 5.1 writes `\` into zip entry names; App Service's
deployment (Linux, rsync) then fails on every file in a subfolder. `tar -a` writes `/`.

## Known limits

- **Client address behind two proxies.** A web request reaches the API through Vercel and then
  Azure's front end. `ForwardedHeaders` trusts one hop (`ForwardLimit = 1`), so the per-address
  limits (sign-in, one-time codes) may see Vercel's address rather than the person's, and people
  using the web then share one budget. To check against real traffic before relying on per-address
  limits in production.
- **SignalR through Vercel.** Vercel's rewrite does not carry WebSockets, so a browser's
  connection to the hub falls back to Server-Sent Events or long polling through the proxy. Live
  updates still arrive, a little later.
- **Single instance.** One B1 instance; the Redis backplane is in place for a second one.
