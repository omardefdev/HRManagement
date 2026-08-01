#Requires -Version 5.1
<#
.SYNOPSIS
  Renders docker/keycloak/import/realm-hrmanagement.json from the local-dev template + .env.
  Run before: docker compose up -d keycloak
#>
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$DemoPassword = "HrAdmin123!"
)

$ErrorActionPreference = "Stop"

$envFile = Join-Path $RepoRoot ".env"
if (-not (Test-Path $envFile)) {
    throw ".env not found at $envFile. Copy .env.example and fill KEYCLOAK_HR_GATEWAY_CLIENT_SECRET."
}

$vars = @{}
Get-Content $envFile | ForEach-Object {
    if ($_ -match '^\s*#' -or $_ -match '^\s*$') { return }
    $i = $_.IndexOf('=')
    if ($i -lt 1) { return }
    $k = $_.Substring(0, $i).Trim()
    $v = $_.Substring($i + 1).Trim()
    if ($v.StartsWith('"') -and $v.EndsWith('"') -and $v.Length -ge 2) {
        $v = $v.Substring(1, $v.Length - 2)
    }
    $vars[$k] = $v
}

$gatewaySecret = $vars['KEYCLOAK_HR_GATEWAY_CLIENT_SECRET']
if ([string]::IsNullOrWhiteSpace($gatewaySecret)) {
    throw "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET is missing in .env"
}

$templatePath = Join-Path $RepoRoot "docker/keycloak/realm-hrmanagement-dev.template.json"
$importDir = Join-Path $RepoRoot "docker/keycloak/import"
$outPath = Join-Path $importDir "realm-hrmanagement.json"

New-Item -ItemType Directory -Force -Path $importDir | Out-Null

$json = Get-Content $templatePath -Raw -Encoding UTF8
$json = $json.Replace("__KEYCLOAK_HR_GATEWAY_CLIENT_SECRET__", $gatewaySecret)
$json = $json.Replace("__DEMO_HRADMIN_PASSWORD__", $DemoPassword)
$json = $json.Replace("__SVC_SECRET__", "dev-svc-secret-local-only")

$null = $json | ConvertFrom-Json
Set-Content -Path $outPath -Value $json -Encoding UTF8 -NoNewline

Write-Host "Wrote $outPath"
Write-Host "Demo user: hradmin / $DemoPassword (role hr-admin)"
