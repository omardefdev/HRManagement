<#
.SYNOPSIS
    BLD-002-ext: stop dev profile app services (keeps postgres/keycloak/minio/rabbit/clamav running).
.EXAMPLE
    .\scripts\dev-profile-down.ps1 -Profile logistics
#>
param(
    [Alias('Profile')]
    [ValidateSet('logistics', 'payroll', 'contracts')]
    [string]$DevProfile = 'logistics',
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$profileName = "dev-$DevProfile"

Push-Location $RepoRoot
try {
    $compose = @(
        '-f', 'docker-compose.yml',
        '-f', 'docker-compose.services.yml',
        '-f', 'docker-compose.local.yml',
        '-f', 'docker-compose.dev-profiles.yml'
    )

    $profileOverride = "docker-compose.dev-$DevProfile.yml"
    if (Test-Path (Join-Path $RepoRoot $profileOverride)) {
        $compose += @('-f', $profileOverride)
    }

    $compose += @('--profile', $profileName, 'stop')

    docker compose @compose
    if ($LASTEXITCODE -ne 0) { throw "docker compose stop failed for profile $profileName" }

    Write-Host "Dev profile '$profileName' app services stopped. Infrastructure still running." -ForegroundColor Green
}
finally {
    Pop-Location
}
