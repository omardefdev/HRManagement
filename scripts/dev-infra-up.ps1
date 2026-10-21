<#
.SYNOPSIS
    BLD-002: start infrastructure only (recommended dev workflow).
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Push-Location $RepoRoot
try {
    docker compose up -d postgresql rabbitmq keycloak minio clamav
    Write-Host "Infrastructure up. Run services locally: scripts/dev-run-hrcore.ps1" -ForegroundColor Green
}
finally {
    Pop-Location
}
