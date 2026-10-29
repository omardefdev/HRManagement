# PLAN-25 (G-02 / G-14): lists, and with -Apply repairs, approvals left without a workflow instance by builds before
# PLAN-25 (requisitions, offers, committee sessions, appeals with an assigned reviewer, establishment changes) and cancels
# orphan RECRUITMENT-HIRE instances. Calls POST /api/recruitment/admin/repair-approvals through the gateway with a token
# for an hr-admin user (policy HrCoreDelete). Dry run by default.
#
# Credentials are never stored: pass -Username/-Password, or set HCM025_ADMIN_USERNAME / HCM025_ADMIN_PASSWORD. On a local
# acceptance stack only, the password may come from the untracked .acceptance/credentials.json (as the acceptance harness
# does) and the gateway client secret from .env.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File scripts/hcm025-repair-stuck-approvals.ps1                # dry run
#   powershell -ExecutionPolicy Bypass -File scripts/hcm025-repair-stuck-approvals.ps1 -Apply         # repair
#   ... -GatewayBaseUrl https://gateway.example -KeycloakBaseUrl https://sso.example -Username ops-admin
param(
    [switch]$Apply,
    [string]$GatewayBaseUrl = $env:GATEWAY_BASE_URL,
    [string]$KeycloakBaseUrl = $env:KEYCLOAK_BASE_URL,
    [string]$Realm = "hrmanagement",
    [string]$ClientId = "hr-gateway-client",
    [string]$ClientSecret = $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET,
    [string]$Username = $env:HCM025_ADMIN_USERNAME,
    [string]$Password = $env:HCM025_ADMIN_PASSWORD,
    [string]$OutDir = "",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path }
if (-not $OutDir) { $OutDir = Join-Path $RepoRoot "artifacts/hcm025-repair" }
if (-not $GatewayBaseUrl) { $GatewayBaseUrl = "http://localhost:6200" }
if (-not $KeycloakBaseUrl) { $KeycloakBaseUrl = "http://localhost:6388" }
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
if (-not $Password) { throw "No password for '$Username': pass -Password or set HCM025_ADMIN_PASSWORD." }

$tokenForm = @{ grant_type = "password"; client_id = $ClientId; username = $Username; password = $Password }
if ($ClientSecret) { $tokenForm["client_secret"] = $ClientSecret }
$token = (Invoke-RestMethod -Method Post -Uri "$($KeycloakBaseUrl.TrimEnd('/'))/realms/$Realm/protocol/openid-connect/token" -Body $tokenForm).access_token
$Password = $null
$tokenForm = $null

$dryRun = if ($Apply) { "false" } else { "true" }
$uri = "$($GatewayBaseUrl.TrimEnd('/'))/api/hr-core/api/recruitment/admin/repair-approvals?dryRun=$dryRun"
$headers = @{ Authorization = "Bearer $token"; "X-Correlation-ID" = "hcm025-repair-$([guid]::NewGuid().ToString('N'))" }
$report = Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -ContentType "application/json" -Body "{}"

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
$mode = if ($Apply) { "apply" } else { "dryrun" }
$csv = Join-Path $OutDir "repair-$mode-$stamp.csv"
$jsonOut = Join-Path $OutDir "repair-$mode-$stamp.json"
$report.items | Select-Object entityType, entityId, reference, action, result, error, workflowInstanceId |
    Export-Csv -Path $csv -NoTypeInformation -Encoding UTF8
$report | ConvertTo-Json -Depth 6 | Set-Content -Path $jsonOut -Encoding UTF8

Write-Host ("hcm025-repair ({0}): found={1} repaired={2} failed={3}" -f $mode, $report.found, $report.repaired, $report.failed)
foreach ($item in $report.items) {
    Write-Host ("  {0,-26} {1} {2,-24} {3} {4}" -f $item.entityType, $item.entityId, $item.action, $item.result, $item.error)
}
Write-Host "CSV: $csv"
if ($Apply -and $report.failed -gt 0) { exit 2 }
