# Local setup (no Docker)

## 1. Tools
| Tool | Notes |
|------|-------|
| .NET 10 SDK | Required for the API and for building the SQL project (`Microsoft.Build.Sql` needs .NET 8+) |
| SQL Server 2022 **Developer Edition** | Free, full feature set (RLS, temporal tables, data masking, spatial) |
| SqlPackage | `dotnet tool install -g microsoft.sqlpackage` — publishes the `.dacpac` |
| Node.js LTS + pnpm/npm | Next.js frontend |
| WSL2 (Ubuntu) + Redis | Redis has no official Windows build |
| VS Code + "SQL Database Projects" extension | Best editor for the SDK-style SQL project |

**Visual Studio note:** SDK-style SQL projects are GA in VS Code and the `dotnet` CLI, but only in preview in
Visual Studio 2022, and Visual Studio 2026 opens only the original SQL project format. Use VS Code or the CLI
for `Sethika.Database`; use any IDE for the C# projects.

## 2. Redis in WSL2
```bash
sudo apt update && sudo apt install -y redis-server
sudo service redis-server start
redis-cli ping        # PONG
```
Connection string from Windows: `localhost:6379`.
Alternatives: Memurai (Windows-native, Redis-compatible) or a free Upstash / Redis Cloud database.

## 3. Database
```bash
cd src/Sethika.Database
dotnet build                                  # validates the whole schema, produces bin/Debug/Sethika.Database.dacpac
sqlpackage /Action:Publish \
  /SourceFile:bin/Debug/Sethika.Database.dacpac \
  /Profile:Sethika.Database.local.publish.xml
```
Change the connection string inside `Sethika.Database.local.publish.xml` if you use SQL authentication
or a named instance (`localhost\SQLEXPRESS`).

To see what a publish would change without applying it: `/Action:Script` (writes the SQL) or `/Action:DeployReport`.

## 4. Azure SQL (later)
The project targets SQL Server 2022 (`Sql160`). When publishing to Azure SQL add
`/p:AllowIncompatiblePlatform=true`, or switch the project `DSP` to `SqlAzureV12DatabaseSchemaProvider` for the release build.

**The API's own login and masked columns (plan 02, B12).** Locally the API connects as a sysadmin, so
dynamic data masking never applies. A deployed API must use a restricted login, and that login needs
`GRANT UNMASK` on exactly the masked columns it reads to do its work, and on nothing else:

| Column | Why the API reads it unmasked |
|---|---|
| `auth.Users.PhoneNumber` | the outbox job texts users; a booking snapshots the donor's phone |
| `booking.Bookings.DonorPhone` | `UX_Bookings_DonorPerSlot` compares it; a masked snapshot would make every donor look alike |
| `org.RegistrationRequests.ApplicantNic`, `org.RegistrationRequestOfficeBearers.NicNumber` | the reviewer checks them |

Hangfire creates its `HangFire` schema on first start, which a restricted login cannot: run the first
start once with DDL rights, or install the schema from Hangfire's script.

## 5. Secrets
Keep connection strings in `dotnet user-secrets` (API) and `.env.local` (web). Never commit them.

### Real SMS through Text.lk
Development defaults to `Sms:Provider = Console`: texts, one-time codes included, go to the API
log and to `notify.Notifications` instead of a phone. To send real ones through Text.lk (sender id
`Sethika`, registered there), put the token in User Secrets. Never put it in `appsettings`:

```bash
dotnet user-secrets set "Sms:TextLkApiToken" "<token from the Text.lk dashboard>" --project src/Sethika.Api
dotnet user-secrets set "Sms:Provider" "TextLk" --project src/Sethika.Api
```

Every text then costs real credit, **including every browser-test run** (each test signs people
up). Switch back before running `npm run e2e`:
`dotnet user-secrets remove "Sms:Provider" --project src/Sethika.Api`.
Outside Development the API refuses to start with the console sender.

## 6. Web app and API together
The browser only talks to the Next.js app; `/api/*` is proxied to the API (plan A3).
`API_BASE_URL` (server-only) says where the API is and defaults to the API's `http` launch profile,
`http://localhost:5182`. It is read when `next build` runs, so set it before building, not only
before `next start`:

```bash
dotnet run --project src/Sethika.Api --launch-profile http   # API on :5182
cd web && npm run dev                                         # web on :3000, /api/* -> :5182
cd web && npm test                                            # unit tests (Vitest)
```

## 7. Mobile app (Phase F)
The app in `mobile/` calls the API directly, so the API must be reachable from the phone or
emulator. See `mobile/README.md` for the full steps; in short:

```bash
dotnet run --project src/Sethika.Api --launch-profile http --urls http://0.0.0.0:5182   # listen on the LAN
cd mobile && npm install
echo "EXPO_PUBLIC_API_BASE_URL=http://<this-pc-LAN-IP>:5182" > .env.local              # real phone only
cd mobile && npx expo start                                                             # then press a (Android)
cd mobile && npm test && npm run typecheck && npm run lint
```

Allow port 5182 through Windows Firewall for private networks, or the phone's requests time out.
