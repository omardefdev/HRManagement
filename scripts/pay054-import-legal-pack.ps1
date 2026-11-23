# PLAN-54 (G-PAY-01): imports a payroll legal framework pack into an agency, as a user, through the gateway.
# This is the first step of a production agency's first payroll calculation (Seed:Profile=None seeds no framework):
#   1. this script: a holder of Payroll.MasterData imports the pack -> a Draft version;
#   2. a second person (Payroll.Approve) approves it on Payroll > Legal frameworks > the version;
#   3. a third person (the legal attestor, Payroll:Legal:AttestorPolicy) attests it;
#   4. the approver or the attestor publishes it. Then a run of a period on or after its effective date calculates.
# It calls GET /api/payroll/frameworks/antiforgery, then POST /api/payroll/frameworks/import with the token.
#
# Credentials are never stored and never printed: pass -Username/-Password, or set PAY054_USERNAME / PAY054_PASSWORD.
# On a local acceptance stack only, the password may come from the untracked .acceptance/credentials.json and the
# gateway client secret from .env (as scripts/hcm025-repair-stuck-approvals.ps1).
#
# Usage (PowerShell 5.1 or 7):
#   powershell -ExecutionPolicy Bypass -File scripts/pay054-import-legal-pack.ps1 -PackPath docs/syr/payroll-framework-packs/law50-interim.json
#   ... -Code LAW_50_2004 -EffectiveFrom 2027-01-01 -GatewayBaseUrl https://gateway.example -KeycloakBaseUrl https://sso.example -Username payroll-clerk
#   ... -TargetDraftId <guid>      # replace the content of an existing draft instead of creating a new version
param(
    [string]$PackPath = "",
    [string]$Code = "",
    [string]$EffectiveFrom = "",
    [string]$TargetDraftId = "",
    [string]$GatewayBaseUrl = $env:GATEWAY_BASE_URL,
    [string]$KeycloakBaseUrl = $env:KEYCLOAK_BASE_URL,
    [string]$Realm = "hrmanagement",
    [string]$ClientId = "hr-gateway-client",
    [string]$ClientSecret = $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET,
    [string]$Username = $env:PAY054_USERNAME,
    [string]$Password = $env:PAY054_PASSWORD,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }
if (-not $PackPath) { $PackPath = Join-Path $RepoRoot "docs/syr/payroll-framework-packs/law50-interim.json" }
if (-not (Test-Path $PackPath)) { throw "Pack not found: $PackPath" }
if (-not $GatewayBaseUrl) { $GatewayBaseUrl = "http://localhost:6200" }
if (-not $KeycloakBaseUrl) { $KeycloakBaseUrl = "http://localhost:6388" }
if (-not $Username) { $Username = "e2e-payroll-processor" }   # the acceptance persona with payroll-processor

# The envelope is built as text: PowerShell's JSON conversion would turn the pack's large amounts into floating point.
if ($Code -and $Code -cnotmatch '^[A-Z][A-Z0-9_]{2,99}$') { throw "Code must be upper-case letters, digits and underscores (3 to 100)." }
if ($EffectiveFrom -and $EffectiveFrom -notmatch '^[0-9]{4}-[0-9]{2}-[0-9]{2}$') { throw "EffectiveFrom must be yyyy-MM-dd." }
if ($TargetDraftId) { $TargetDraftId = ([guid]$TargetDraftId).ToString("D") }

$packText = [System.IO.File]::ReadAllText((Resolve-Path $PackPath).Path, (New-Object System.Text.UTF8Encoding($false))).Trim()
if ($packText.Length -gt 0 -and [int][char]$packText[0] -eq 0xFEFF) { $packText = $packText.Substring(1) }
if (-not $packText.StartsWith("{")) { throw "The pack must be one JSON object." }
$parts = New-Object System.Collections.Generic.List[string]
$parts.Add('"pack":' + $packText)
if ($Code) { $parts.Add('"code":"' + $Code + '"') }
if ($EffectiveFrom) { $parts.Add('"effectiveFrom":"' + $EffectiveFrom + '"') }
if ($TargetDraftId) { $parts.Add('"targetDraftId":"' + $TargetDraftId + '"') }
$body = (New-Object System.Text.UTF8Encoding($false)).GetBytes("{" + ($parts -join ",") + "}")

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
if (-not $Password) { throw "No password for '$Username': pass -Password or set PAY054_PASSWORD." }

$tokenForm = @{ grant_type = "password"; client_id = $ClientId; username = $Username; password = $Password }
if ($ClientSecret) { $tokenForm["client_secret"] = $ClientSecret }
$token = (Invoke-RestMethod -Method Post -Uri "$($KeycloakBaseUrl.TrimEnd('/'))/realms/$Realm/protocol/openid-connect/token" -Body $tokenForm).access_token
$Password = $null
$tokenForm = $null

# The gateway forwards /api/payroll/** to the payroll service unchanged, and the service's route is api/payroll/frameworks.
$api = "$($GatewayBaseUrl.TrimEnd('/'))/api/payroll/frameworks"
$headers = @{ Authorization = "Bearer $token"; "X-Correlation-ID" = "pay054-import-$([guid]::NewGuid().ToString('N'))" }

# The antiforgery token and its cookie: every write of the frameworks API validates them. The service scopes the cookie to its
# UI path (/ui/payroll) and the API sits under /api/payroll, so the cookie is sent back by hand, not by a web session.
$issued = Invoke-WebRequest -UseBasicParsing -Method Get -Uri "$api/antiforgery" -Headers $headers
$antiforgery = $issued.Content | ConvertFrom-Json
$headers[[string]$antiforgery.headerName] = [string]$antiforgery.requestToken
$cookiePairs = New-Object System.Collections.Generic.List[string]
foreach ($setCookie in @($issued.Headers["Set-Cookie"])) {
    foreach ($m in [regex]::Matches([string]$setCookie, '(\.AspNetCore\.Antiforgery\.[A-Za-z0-9_\-]+)=([^;,\s]+)')) {
        $cookiePairs.Add($m.Groups[1].Value + "=" + $m.Groups[2].Value)
    }
}
if ($cookiePairs.Count -eq 0) { throw "The antiforgery endpoint set no cookie." }
$headers["Cookie"] = ($cookiePairs -join "; ")

$status = 0
$answer = $null
try {
    $response = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$api/import" -Headers $headers -ContentType "application/json; charset=utf-8" -Body $body
    $status = [int]$response.StatusCode
    $answer = $response.Content | ConvertFrom-Json
}
catch {
    $failed = $_.Exception.Response
    if (-not $failed) { throw }
    $status = [int]$failed.StatusCode
    $text = ""
    if ($_.ErrorDetails -and $_.ErrorDetails.Message) { $text = $_.ErrorDetails.Message }
    elseif ($failed.GetType().GetMethod("GetResponseStream")) {
        $reader = New-Object System.IO.StreamReader($failed.GetResponseStream(), [System.Text.Encoding]::UTF8)
        $text = $reader.ReadToEnd()
    }
    if ($text) { try { $answer = $text | ConvertFrom-Json } catch { $answer = $null } }
}
$token = $null
$headers = $null

if ($status -ne 201) {
    Write-Host ("pay054-import: REFUSED (HTTP {0}). Nothing was written." -f $status)
    if ($answer) {
        if ($answer.error) { Write-Host ("  " + $answer.error) }
        foreach ($e in @($answer.errors)) { if ($e) { Write-Host ("  {0}: {1}" -f $e.path, $e.message) } }
    }
    if ($status -eq 403) { Write-Host "  The user needs the Payroll.MasterData policy (hr-admin or payroll-processor by default)." }
    exit 2
}

Write-Host ("pay054-import: imported {0} v{1} as a {2} (id {3})." -f $answer.code, $answer.versionNumber, $answer.status, $answer.frameworkId)
Write-Host "Next steps, by three different people (Payroll > Legal frameworks > the version):"
Write-Host "  1. Approve       - a holder of Payroll.Approve who did not import or edit the version."
Write-Host "  2. Legal attest  - the attestor (Payroll:Legal:AttestorPolicy, by default Payroll.Approve), neither an editor nor the approver."
Write-Host "  3. Publish       - the approver or the attestor. Then a run of a period on or after the effective date calculates."
Write-Host "An interim value is approved, attested, published and calculated only while the STR-002 exception it cites is active."
