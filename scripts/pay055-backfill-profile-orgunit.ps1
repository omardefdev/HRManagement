# PLAN-55 (P-8): lists, and with -Apply fills, the empty organisational unit and legal regime of ACTIVE pay profiles, each from the
# employee's own latest pay profile that carries the value. Builds before PLAN-55 made a contract activation's version without them.
# It never overwrites a value, touches nothing else, and a second run finds nothing. One agency per call: the caller's.
#
# Calls POST /api/payroll/admin/backfill-profile-orgunit through the gateway with a token of an administrator (policy
# Payroll.Repair, by default hr-admin). Dry run by default. The POST needs an antiforgery token: the script fetches it first from
# GET /api/payroll/admin/antiforgery, keeps the cookie that answer sets, and sends the token in the header the answer names.
#
# Credentials are never stored and never printed. Three ways, the first that is given wins:
#   -AccessToken or HRM_REPAIR_ACCESS_TOKEN: a bearer token of the administrator;
#   -CookieHeader or HRM_REPAIR_COOKIE: the Cookie header of his signed-in gateway session. Production and the demo keep the
#     password grant closed, so this is the way there (as scripts/msg040-replay-lost-events.ps1);
#   -Username/-Password, or PAY055_ADMIN_USERNAME / PAY055_ADMIN_PASSWORD: a password grant, where the realm allows one. On a
#     local acceptance stack only, the password may come from the untracked .acceptance/credentials.json (as the acceptance
#     harness does) and the gateway client secret from .env.
#
# Usage:
#   pwsh -NoProfile -File scripts/pay055-backfill-profile-orgunit.ps1                # dry run: counts and profile ids
#   pwsh -NoProfile -File scripts/pay055-backfill-profile-orgunit.ps1 -Apply         # a dry run, then fills exactly what it listed
# A real run is refused (409) while a payroll calculation of the agency is in progress.
#   ... -GatewayBaseUrl https://gateway.example -KeycloakBaseUrl https://sso.example -Username ops-admin
#   ... -GatewayBaseUrl https://<APP_DOMAIN> -CookieHeader "<the hr-admin's session>"     # Production and the demo
#
# The report (JSON) and the list of profiles (CSV, with each profile's former empty values: the undo list) are written to -OutDir.
param(
    [switch]$Apply,
    [string]$GatewayBaseUrl = $env:GATEWAY_BASE_URL,
    [string]$KeycloakBaseUrl = $env:KEYCLOAK_BASE_URL,
    [string]$Realm = "hrmanagement",
    [string]$ClientId = "hr-gateway-client",
    [string]$ClientSecret = $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET,
    [string]$Username = $env:PAY055_ADMIN_USERNAME,
    [string]$Password = $env:PAY055_ADMIN_PASSWORD,
    [string]$AccessToken = $env:HRM_REPAIR_ACCESS_TOKEN,
    [string]$CookieHeader = $env:HRM_REPAIR_COOKIE,
    [string]$OutDir = "",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot "artifacts/pay055-backfill" }
if (-not $GatewayBaseUrl) { $GatewayBaseUrl = "http://localhost:6200" }
if (-not $KeycloakBaseUrl) { $KeycloakBaseUrl = "http://localhost:6388" }
$token = $AccessToken
if (-not $token -and -not $CookieHeader) {
    if (-not $Username) { $Username = "acc-a-directory-checker" }   # the acceptance persona with hr-admin

    if (-not $ClientSecret) {
        $dotEnv = Join-Path $RepoRoot ".env"
        if (Test-Path $dotEnv) {
            $line = Get-Content $dotEnv | Where-Object { $_ -match '^\s*KEYCLOAK_HR_GATEWAY_CLIENT_SECRET=' } | Select-Object -First 1
            if ($line) { $ClientSecret = ($line -split '=', 2)[1].Trim().Trim('"').Trim("'") }
        }
    }
    if (-not $Password) {
        $credentials = Join-Path $RepoRoot ".acceptance/credentials.json"
        if (Test-Path $credentials) {
            $json = Get-Content -Raw $credentials | ConvertFrom-Json
            $entry = $json.passwords.PSObject.Properties | Where-Object { $_.Name -eq $Username } | Select-Object -First 1
            if ($entry) { $Password = [string]$entry.Value }
        }
    }
    if (-not $Password) { throw "No password for '$Username': pass -Password or set PAY055_ADMIN_PASSWORD." }

    $tokenForm = @{ grant_type = "password"; client_id = $ClientId; username = $Username; password = $Password }
    if ($ClientSecret) { $tokenForm["client_secret"] = $ClientSecret }
    $token = (Invoke-RestMethod -Method Post -Uri "$($KeycloakBaseUrl.TrimEnd('/'))/realms/$Realm/protocol/openid-connect/token" -Body $tokenForm).access_token
    $Password = $null
    $tokenForm = $null
}
$AccessToken = $null

$gateway = $GatewayBaseUrl.TrimEnd('/')
$base = "$gateway/api/payroll/admin"
$headers = @{ "X-Correlation-ID" = "pay055-backfill-$([guid]::NewGuid().ToString('N'))" }
if ($token) { $headers["Authorization"] = "Bearer $token" }

# The antiforgery token of the POST. The service scopes its cookie to its UI path (/ui/payroll) and this call goes through
# /api/payroll, so the cookie is put into the session for the gateway itself. The token is sent as a header only: it never
# reaches the report.
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
if ($CookieHeader) {
    # Cookies go through the session: Windows PowerShell 5.1 drops a Cookie entry in -Headers without a word.
    foreach ($part in ($CookieHeader -split ';\s*' | Where-Object { $_ })) {
        $pair = $part -split '=', 2
        $session.Cookies.Add([Uri]$gateway, (New-Object Net.Cookie($pair[0].Trim(), $pair[1], '/')))
    }
    $CookieHeader = $null
}
$issued = Invoke-WebRequest -UseBasicParsing -Method Get -Uri "$base/antiforgery" -Headers $headers -WebSession $session
$antiforgery = $issued.Content | ConvertFrom-Json
$cookie = [regex]::Match(($issued.Headers['Set-Cookie'] -join ','), '(\.AspNetCore\.Antiforgery\.[^=;,\s]+)=([^;,\s]+)')
if ($cookie.Success) {
    $session.Cookies.Add([Uri]$gateway, (New-Object Net.Cookie($cookie.Groups[1].Value, $cookie.Groups[2].Value, '/')))
}
$postHeaders = @{}
foreach ($key in $headers.Keys) { $postHeaders[$key] = $headers[$key] }
$postHeaders[[string]$antiforgery.headerName] = [string]$antiforgery.requestToken

# Always a dry run first. With -Apply, the real run is then given the dry run's own list: it changes those profiles and no other.
$report = Invoke-RestMethod -Method Post -Uri "$base/backfill-profile-orgunit?dryRun=true" -Headers $postHeaders -WebSession $session -ContentType "application/json" -Body "{}"
if ($Apply) {
    $listed = @(@($report.items) | ForEach-Object { [string]$_.profileId })
    Write-Host ("pay055-backfill (dryrun before apply): fillable={0}" -f $listed.Count)
    if ($listed.Count -gt 0) {
        $body = @{ profileIds = $listed } | ConvertTo-Json -Depth 3
        $report = Invoke-RestMethod -Method Post -Uri "$base/backfill-profile-orgunit?dryRun=false" -Headers $postHeaders -WebSession $session -ContentType "application/json" -Body $body
    }
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$mode = if ($Apply) { "apply" } else { "dryrun" }
$csv = Join-Path $OutDir "backfill-$mode-$stamp.csv"
$jsonOut = Join-Path $OutDir "backfill-$mode-$stamp.json"
# The undo list: each listed profile with what was empty before (a filled column here was empty in the profile).
@($report.items) | Select-Object profileId, employeeId, orgUnitId, orgUnitFromProfileId, regimeCode, regimeFromProfileId |
    Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonOut -Encoding UTF8

$notFillable = @($report.notFillableProfileIds).Count
Write-Host ("pay055-backfill ({0}): active profiles with an empty unit={1}, with an empty regime={2}; fillable={3}, changed={4}, not fillable={5}" -f `
    $mode, $report.activeProfilesWithEmptyUnit, $report.activeProfilesWithEmptyRegime, $report.found, $report.changed, $notFillable)
foreach ($item in @($report.items)) {
    $unit = if ($item.orgUnitId) { "unit" } else { "-" }
    $regime = if ($item.regimeCode) { "regime" } else { "-" }
    Write-Host ("  profile {0} employee {1} fills: {2} {3}" -f $item.profileId, $item.employeeId, $unit, $regime)
}
if ($notFillable -gt 0) {
    Write-Host ("  {0} active profile(s) have an empty unit or regime that no profile of the same employee carries: not changed (their ids are in the JSON report)." -f $notFillable)
}
Write-Host "CSV (the undo list): $csv"
Write-Host "JSON: $jsonOut"
$late = @($report.notInTheDryRun).Count
if ($late -gt 0) { Write-Host ("  {0} profile(s) became fillable after the dry run and were left alone: run the script again." -f $late) }
if ($Apply -and $report.changed -ne $report.found) { exit 2 }
