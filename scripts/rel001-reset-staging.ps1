# REL-001: repeatable staging reset (lab compose path by default).
# Sequence: down -> up -> migrate (optional) -> seed -> verify.
# Does NOT import production dumps. Lab Keycloak secrets only.
#
# Usage:
#   powershell -File scripts/rel001-reset-staging.ps1
#   powershell -File scripts/rel001-reset-staging.ps1 -SkipSeed -DryRun
#   powershell -File scripts/rel001-reset-staging.ps1 -Employees 1000

param(
    [switch]$DryRun,
    [switch]$SkipCompose,
    [switch]$SkipSeed,
    [switch]$SkipVerify,
    [switch]$SkipMigrate,
    [int]$Employees = 1000,
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:5000" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Step([string]$msg) {
    Write-Host ""
    Write-Host ("=== REL-001: {0} ===" -f $msg) -ForegroundColor Cyan
}

if ($env:REL001_ALLOW_PROD_DUMP -eq "1") {
    Write-Error "REL001_ALLOW_PROD_DUMP is forbidden. Unset the variable."
}

Step "Parity gate"
if ($DryRun) {
    Write-Host "[dry-run] would run rel001-parity-diff.ps1"
} else {
    & "$PSScriptRoot/rel001-parity-diff.ps1"
}

if (-not $SkipCompose) {
    Step "Compose reset (lab)"
    if ($DryRun) {
        Write-Host "[dry-run] docker compose down -v && up -d --wait"
    } else {
        docker compose -f docker-compose.yml -f docker-compose.services.yml down -v
        docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait
    }
} else {
    Step "SkipCompose - assume stack already up"
}

if (-not $SkipMigrate) {
    Step "Migrations"
    $migrate = Join-Path $root "scripts/run-migrations.sh"
    if ($DryRun) {
        Write-Host "[dry-run] would run migrations if configured"
    } elseif (Test-Path $migrate) {
        if (Get-Command bash -ErrorAction SilentlyContinue) {
            bash $migrate
        } else {
            Write-Host "bash/run-migrations.sh not available - services may migrate at startup"
        }
    } else {
        Write-Host "No run-migrations.sh - relying on service startup migrate"
    }
}

if (-not $SkipSeed) {
    Step "Synthetic seed"
    if ($DryRun) {
        Write-Host ("[dry-run] rel001-seed-synthetic.ps1 -Employees {0}" -f $Employees)
    } else {
        & "$PSScriptRoot/rel001-seed-synthetic.ps1" -Employees $Employees
    }
    Write-Host "Keycloak: import realm-hr-services.json, realm-qa004-e2e.json, then realm-rel001-staging.json (lab only)."
}

if (-not $SkipVerify) {
    Step "Verify stack"
    if ($DryRun) {
        Write-Host ("[dry-run] verify-staging-stack.ps1 -GatewayBaseUrl {0}" -f $GatewayBaseUrl)
    } else {
        & "$PSScriptRoot/verify-staging-stack.ps1" -GatewayBaseUrl $GatewayBaseUrl
    }
}

Step "Done"
Write-Host ("REL-001 reset completed (DryRun={0}). Next: run-staging-e2e.ps1 -SkipComposeUp for rehearsal." -f $DryRun)
