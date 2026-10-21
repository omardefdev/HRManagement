#Requires -Version 5.1
<#
.SYNOPSIS
  Builds and starts the Docker stack needed to UAT FUN-005 Phase 5 (Admin.Logistics extensions).
.DESCRIPTION
  - Renders Keycloak realm (includes svc-admin-logistics)
  - Rebuilds admin-logistics, hr-core, hr-gateway, file-storage
  - Starts full dev stack (docker-compose.yml + services + local overrides)
.EXAMPLE
  .\scripts\fun005ext-docker-up.ps1
  .\scripts\fun005ext-docker-up.ps1 -RecreateKeycloak
#>
param(
    [switch]$RecreateKeycloak,
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
Set-Location $RepoRoot

Write-Host "==> Preparing Keycloak realm import..."
& (Join-Path $RepoRoot "scripts/prepare-keycloak-import.ps1")

$compose = @(
    "docker", "compose",
    "-f", "docker-compose.yml",
    "-f", "docker-compose.services.yml",
    "-f", "docker-compose.local.yml"
)

if ($RecreateKeycloak) {
    Write-Host "==> Recreating Keycloak (required after realm template changes)..."
    & @compose stop keycloak
    & @compose rm -f keycloak
}

if (-not $SkipBuild) {
    Write-Host "==> Building Phase 5 services..."
    & @compose build hr-core-web hr-gateway file-storage-web admin-logistics-web
}

Write-Host "==> Starting stack..."
& @compose up -d --wait

Write-Host ""
Write-Host "FUN-005 Phase 5 stack is up."
Write-Host "  Gateway:          http://localhost:5200"
Write-Host "  Admin.Logistics:  http://localhost:5200/ui/admin-logistics/Assets"
Write-Host "  HR.Core:          http://localhost:5200/ui/hr-core"
Write-Host "  Keycloak:         http://localhost:8088"
Write-Host "  Login:            hradmin / HrAdmin123!"
Write-Host ""
Write-Host "Direct service ports (debug): admin-logistics 5090, hr-core 5010, file-storage 5140"
