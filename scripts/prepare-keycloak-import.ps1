#Requires -Version 5.1
<#
.SYNOPSIS
  Renders the Keycloak import. PLAN-39: a compatibility wrapper around scripts/keycloak/render-realm.ps1 (the realm
  manifest docker/keycloak/realm-manifest.json plus the wiring matrix deploy/wiring/service-wiring.json).

.DESCRIPTION
  -Profile Local (default)   the local developer realm from docker/keycloak/realm-hrmanagement-dev.template.json
                             (localhost, hradmin, lab secrets) into docker/keycloak/import; then run
                             scripts/sync-keycloak-dev-realm.ps1 for the matrix clients and scopes. Before PLAN-39 this
                             profile was called Demo; callers without -Profile (CI, dev-profile-up.ps1) are unchanged.
  -Profile Acceptance        render-realm.ps1 -Profile Acceptance (every persona, lab secrets) into docker/keycloak/import
  -Profile Demo (VpsDemo)    render-realm.ps1 -Profile Demo for -AppDomain into artifacts/vps/import (the deploy bundle)
  -Profile Production        render-realm.ps1 -Profile Production for -AppDomain into artifacts/vps/import
  Demo and Production carry ${NAME} placeholders only, resolved by Keycloak from its own environment on the server.
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [ValidateSet("Local", "Acceptance", "Demo", "VpsDemo", "Production")]
    [string]$Profile = "Local",
    [string]$DemoPassword = "HrAdmin123!",
    [switch]$RotateAcceptanceCredentials,
    # Demo and Production: the public application name (APP_DOMAIN) and the output directory (git-ignored).
    [string]$AppDomain = "",
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
$renderer = Join-Path $RepoRoot "scripts/keycloak/render-realm.ps1"

if ($Profile -in @("Demo", "VpsDemo", "Production")) {
    $target = if ($Profile -eq "Production") { "Production" } else { "Demo" }
    if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $RepoRoot "artifacts/vps/import" }
    & $renderer -Profile $target -AppDomain $AppDomain -RepoRoot $RepoRoot -OutFile (Join-Path $OutDir "realm-hrmanagement.json")
    return
}

if ($Profile -eq "Acceptance") {
    $outPath = Join-Path $RepoRoot "docker/keycloak/import/realm-hrmanagement.json"
    & $renderer -Profile Acceptance -EnvFile (Join-Path $RepoRoot ".env") -RepoRoot $RepoRoot -OutFile $outPath -RotateAcceptanceCredentials:$RotateAcceptanceCredentials
    Write-Host "Local credentials and account guide: $(Join-Path $RepoRoot '.acceptance') (passwords not printed, excluded from Git)"
    return
}

# Profile Local: the developer template, unchanged since PLAN-35.
function Read-DotEnv([string]$Path) {
    if (-not (Test-Path $Path)) { throw ".env not found at $Path. Copy .env.example and fill KEYCLOAK_HR_GATEWAY_CLIENT_SECRET." }
    $result = @{}
    Get-Content $Path | ForEach-Object {
        if ($_ -match '^\s*#' -or $_ -match '^\s*$') { return }
        $i = $_.IndexOf('=')
        if ($i -lt 1) { return }
        $key = $_.Substring(0, $i).Trim()
        $value = $_.Substring($i + 1).Trim()
        if ($value.StartsWith('"') -and $value.EndsWith('"') -and $value.Length -ge 2) {
            $value = $value.Substring(1, $value.Length - 2)
        }
        $result[$key] = $value
    }
    return $result
}

$vars = Read-DotEnv (Join-Path $RepoRoot ".env")
$gatewaySecret = $vars['KEYCLOAK_HR_GATEWAY_CLIENT_SECRET']
if ([string]::IsNullOrWhiteSpace($gatewaySecret)) {
    throw "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET is missing in .env"
}

$templatePath = Join-Path $RepoRoot "docker/keycloak/realm-hrmanagement-dev.template.json"
$importDir = Join-Path $RepoRoot "docker/keycloak/import"
$outPath = Join-Path $importDir "realm-hrmanagement.json"
New-Item -ItemType Directory -Force -Path $importDir | Out-Null

$templateJson = Get-Content $templatePath -Raw -Encoding UTF8
$templateJson = $templateJson.Replace("__KEYCLOAK_HR_GATEWAY_CLIENT_SECRET__", $gatewaySecret)
$templateJson = $templateJson.Replace("__DEMO_HRADMIN_PASSWORD__", $DemoPassword)
$templateJson = $templateJson.Replace("__SVC_SECRET__", "dev-svc-secret-local-only")

$null = $templateJson | ConvertFrom-Json
[System.IO.File]::WriteAllText($outPath, $templateJson, (New-Object System.Text.UTF8Encoding $false))
Write-Host "Wrote $outPath"
Write-Host "Local user: hradmin / $DemoPassword (role hr-admin)"
