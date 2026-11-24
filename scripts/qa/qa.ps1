<#
.SYNOPSIS
  The QA environment (docs/qa/QA-PLAN.md sections 4, 5 and 9): its own database, Redis db, file
  folder and SignalR channel, so a QA cycle never touches the development data, and texts never
  leave the machine.

.EXAMPLE
  ./scripts/qa/qa.ps1 reset       # build the dacpac, publish Sethika_QA fresh, flush the QA Redis db
  ./scripts/qa/qa.ps1 api         # run the API against Sethika_QA on :5190 (foreground)
  ./scripts/qa/qa.ps1 web         # run next dev on :3000, proxying /api to the QA API (foreground)
  ./scripts/qa/qa.ps1 check       # entry checks: API up, on Sethika_QA, console SMS only
  ./scripts/qa/qa.ps1 personas    # build T1-T6 and every persona through the product
  ./scripts/qa/qa.ps1 test F01    # run the QA specs (optional filter, e.g. a feature id)

.NOTES
  Order for a cycle: reset -> api (own terminal) -> web (own terminal) -> check -> personas -> test.
  Settings come from qa-env.json next to this script. Environment variables win over the API's
  user-secrets, so the developer's own settings are never edited.
#>
param(
  [Parameter(Mandatory = $true, Position = 0)]
  [ValidateSet("reset", "api", "web", "check", "personas", "test")]
  [string]$Command,
  [Parameter(ValueFromRemainingArguments = $true)]
  [string[]]$Rest
)

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$qa = Get-Content (Join-Path $PSScriptRoot "qa-env.json") -Raw | ConvertFrom-Json
$apiUrl = "http://localhost:$($qa.apiPort)"
$webUrl = "http://localhost:$($qa.webPort)"
$filesRoot = Join-Path $env:LOCALAPPDATA $qa.filesFolder
$redisCli = "C:\Program Files\Redis\redis-cli.exe"

function Test-Port([int]$port) {
  [bool](Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function Invoke-QaSql([string]$query) {
  $out = & sqlcmd -S $qa.sqlServer -d $qa.database -E -C -I -b -h -1 -W -Q "SET NOCOUNT ON; EXEC sys.sp_set_session_context @key = N'IsPlatform', @value = 1; $query"
  if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed: $out" }
  # The comma keeps a one-row answer an array: unrolled to a bare string, [0] would be its first char.
  return ,@($out | Where-Object { $_.Trim() -ne "" } | ForEach-Object { $_.Trim() })
}

function Set-QaApiEnvironment {
  $env:ASPNETCORE_ENVIRONMENT = "Development"
  $env:ConnectionStrings__Sql = "Server=$($qa.sqlServer);Database=$($qa.database);Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
  $env:ConnectionStrings__Redis = "$($qa.redisHost):$($qa.redisPort),defaultDatabase=$($qa.redisDb)"
  $env:Sms__Provider = "Console"     # QA-PLAN section 4: no real person is ever texted
  $env:Push__Provider = "Console"
  $env:Files__LocalRoot = $filesRoot
  $env:SignalR__ChannelPrefix = $qa.signalRChannelPrefix
}

switch ($Command) {
  "reset" {
    if (Test-Port $qa.apiPort) { throw "Stop the QA API on :$($qa.apiPort) first: the database cannot be dropped while it is connected." }

    Write-Host "== Building the dacpac"
    & dotnet build (Join-Path $repo "src\Sethika.Database\Sethika.Database.sqlproj") -m:1 -nodeReuse:false -v minimal
    if ($LASTEXITCODE -ne 0) { throw "dacpac build failed" }

    Write-Host "== Publishing $($qa.database) fresh (CreateNewDatabase)"
    $sqlpackage = Join-Path $env:USERPROFILE ".dotnet\tools\sqlpackage.exe"
    if (-not (Test-Path $sqlpackage)) { throw "SqlPackage missing: dotnet tool install -g microsoft.sqlpackage" }
    & $sqlpackage /Action:Publish `
      /SourceFile:"$repo\src\Sethika.Database\bin\Debug\Sethika.Database.dacpac" `
      /TargetServerName:$($qa.sqlServer) /TargetDatabaseName:$($qa.database) `
      /TargetTrustServerCertificate:True /p:CreateNewDatabase=True /p:BlockOnPossibleDataLoss=False
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }

    Write-Host "== Seeds"
    $counts = Invoke-QaSql @"
SELECT CONCAT('Plans=', (SELECT COUNT(*) FROM org.Plans), ' InstitutionTypes=', (SELECT COUNT(*) FROM org.InstitutionTypes),
  ' Permissions=', (SELECT COUNT(*) FROM auth.Permissions), ' TemplateRoles=', (SELECT COUNT(*) FROM auth.Roles WHERE IsTemplate = 1),
  ' SmsTemplates=', (SELECT COUNT(*) FROM notify.Templates), ' Districts=', (SELECT COUNT(*) FROM core.Districts),
  ' Users=', (SELECT COUNT(*) FROM auth.Users), ' Institutions=', (SELECT COUNT(*) FROM org.Institutions));
"@
    Write-Host "   $counts"
    if ($counts -match "Plans=0|Permissions=0|TemplateRoles=0|SmsTemplates=0") { throw "Seeds did not run." }
    if ($counts -notmatch "Users=0 Institutions=0") { throw "A fresh database should have no users or institutions." }

    Write-Host "== Redis db $($qa.redisDb): FLUSHDB"
    $flushed = & $redisCli -h $qa.redisHost -p $qa.redisPort -n $qa.redisDb FLUSHDB
    if ($flushed -ne "OK") { throw "FLUSHDB answered: $flushed" }

    Write-Host "== QA uploads: $filesRoot"
    if (Test-Path $filesRoot) { Remove-Item -Recurse -Force $filesRoot }
    New-Item -ItemType Directory -Force $filesRoot | Out-Null
    Write-Host "Reset done. Next: qa.ps1 api / qa.ps1 web in their own terminals, then qa.ps1 check."
  }

  "api" {
    if (Test-Port $qa.apiPort) { throw ":$($qa.apiPort) is already in use." }
    Set-QaApiEnvironment
    Write-Host "QA API on $apiUrl -> $($qa.database), Redis db $($qa.redisDb), SMS Console"
    & dotnet run --project (Join-Path $repo "src\Sethika.Api") --no-launch-profile --urls $apiUrl
  }

  "web" {
    # A web server already on the port would be proxying to some other API - the dev one.
    if (Test-Port $qa.webPort) { throw ":$($qa.webPort) is already in use (a dev web server?). Stop it first." }
    $env:API_BASE_URL = $apiUrl
    Push-Location (Join-Path $repo "web")
    try { & npm run dev -- -p $qa.webPort } finally { Pop-Location }
  }

  "check" {
    $failed = @()
    try { $null = Invoke-WebRequest "$apiUrl/health" -UseBasicParsing -TimeoutSec 10 } catch { $failed += "API not answering at $apiUrl/health" }
    try { $null = Invoke-WebRequest "$webUrl/api/legal/current?language=en" -UseBasicParsing -TimeoutSec 60 } catch { $failed += "web at $webUrl does not reach an API" }

    # The API and the web proxy must both land in Sethika_QA, and its texts must go to the console:
    # ask each for a signup code for a sentinel number of its own, new each run (one number asked
    # twice within a minute meets the resend cooldown), and look for both texts in this database.
    $tail = Get-Random -Minimum 0 -Maximum 4999
    $sentinels = @{ $apiUrl = "077099{0:D4}" -f ($tail * 2); $webUrl = "077099{0:D4}" -f ($tail * 2 + 1) }
    $before = [long](Invoke-QaSql "SELECT ISNULL(MAX(Id), 0) FROM notify.Notifications;")[0]
    foreach ($base in $sentinels.Keys) {
      try {
        $body = @{ phoneNumber = $sentinels[$base]; purpose = "signup"; language = "en" } | ConvertTo-Json
        $null = Invoke-WebRequest "$base/api/auth/otp/request" -Method Post -Body $body -ContentType "application/json" -UseBasicParsing -TimeoutSec 30
      } catch { $failed += "OTP request through $base failed: $($_.Exception.Message)" }
    }
    Start-Sleep -Seconds 2
    foreach ($base in $sentinels.Keys) {
      $last9 = $sentinels[$base].Substring(1)
      $arrived = [int](Invoke-QaSql "SELECT COUNT(*) FROM notify.Notifications WHERE Id > $before AND Channel = 'SMS' AND RIGHT(Recipient, 9) = '$last9';")[0]
      if ($arrived -ne 1) { $failed += "the code asked for through $base did not reach $($qa.database): pointed at another database?" }
    }
    $real = [int](Invoke-QaSql "SELECT COUNT(*) FROM notify.Notifications WHERE Channel = 'SMS' AND ProviderMessageId IS NOT NULL AND ProviderMessageId NOT LIKE 'console-%';")[0]
    if ($real -ne 0) { $failed += "$real SMS rows in $($qa.database) went through a real gateway" }

    if ($failed.Count) { $failed | ForEach-Object { Write-Host "FAIL  $_" -ForegroundColor Red }; exit 1 }
    Write-Host "OK  API $apiUrl and web $webUrl on $($qa.database); every SMS went to the console sender." -ForegroundColor Green
  }

  "personas" {
    Push-Location (Join-Path $repo "web")
    try { & npx playwright test --config playwright.qa.config.ts --project=setup } finally { Pop-Location }
    exit $LASTEXITCODE
  }

  "test" {
    Push-Location (Join-Path $repo "web")
    try { & npx playwright test --config playwright.qa.config.ts --project=qa --project=qa-390 --project=qa-320 @Rest } finally { Pop-Location }
    exit $LASTEXITCODE
  }
}
