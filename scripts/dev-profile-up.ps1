<#
.SYNOPSIS
    BLD-002-ext: start a dev profile (subset of services + infra).
.EXAMPLE
    .\scripts\dev-profile-up.ps1 -DevProfile logistics
    .\scripts\dev-profile-up.ps1 -DevProfile contracts -Build
    .\scripts\dev-profile-up.ps1 -DevProfile logistics -Build
#>
param(
    [Alias('Profile')]
    [ValidateSet('logistics', 'payroll', 'contracts')]
    [string]$DevProfile = 'logistics',
    [switch]$Build,
    [switch]$NoCache,
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
$env:DOCKER_BUILDKIT = '1'
if (-not $env:COMPOSE_PARALLEL_LIMIT) { $env:COMPOSE_PARALLEL_LIMIT = '2' }

$profileName = "dev-$DevProfile"

Push-Location $RepoRoot
try {
    if (Test-Path (Join-Path $RepoRoot ".env")) {
        Write-Host "==> Preparing Keycloak realm import + syncing live realm..."
        & (Join-Path $RepoRoot "scripts/prepare-keycloak-import.ps1")
        & (Join-Path $RepoRoot "scripts/sync-keycloak-dev-realm.ps1")
    }
    else {
        Write-Warning ".env missing - skip Keycloak sync (run scripts/prepare-keycloak-import.ps1 manually)."
    }

    & "$PSScriptRoot/docker-generate-build-manifest.ps1" -RepoRoot $RepoRoot

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
    elseif ($DevProfile -ne 'logistics') {
        throw "Profile override file missing: $profileOverride (not implemented yet)."
    }

    $compose += @('--profile', $profileName)

    if ($Build) {
        $buildArgs = @('compose') + $compose + @('build')
        if ($NoCache) { $buildArgs += '--no-cache' }
        docker @buildArgs
        if ($LASTEXITCODE -ne 0) { throw "docker compose build failed for profile $profileName" }
    }

    docker compose @compose up -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed for profile $profileName" }

    Write-Host ""
    Write-Host "Dev profile '$profileName' is up (infra + app services)." -ForegroundColor Green
    Write-Host "  Gateway:          http://localhost:5200" -ForegroundColor Cyan
    switch ($DevProfile) {
        'contracts' {
            Write-Host "  HR Contracts:     http://localhost:5200/ui/hr-contracts" -ForegroundColor Cyan
            Write-Host "  Hire Wizard:      http://localhost:5200/ui/hr-contracts/HireWizard" -ForegroundColor Cyan
        }
        'payroll' {
            Write-Host "  Payroll:          http://localhost:5200/ui/payroll" -ForegroundColor Cyan
        }
        default {
            Write-Host "  Admin Logistics:  http://localhost:5200/ui/admin-logistics/Assets" -ForegroundColor Cyan
        }
    }
    Write-Host "  Gateway health:   http://localhost:5200/health" -ForegroundColor Cyan
    $stopHint = "powershell -File scripts/dev-profile-down.ps1 -DevProfile $DevProfile"
    Write-Host "  Stop apps only:   $stopHint" -ForegroundColor DarkGray
}
finally {
    Pop-Location
}
