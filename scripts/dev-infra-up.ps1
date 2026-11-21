<#
.SYNOPSIS
    BLD-002: start infrastructure only (recommended dev workflow).
.DESCRIPTION
    Starts postgresql, rabbitmq, keycloak and clamav. MinIO is opt-in (QA-034 / D-QA-7): it is started only when
    MINIO_IMAGE (a vetted, digest-pinned image) is set in the environment or in .env, through
    docker-compose.object-storage.yml. Without it File.Storage uses the Local provider.
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Push-Location $RepoRoot
try {
    if (-not (Test-Path '.env')) {
        throw ".env not found in $RepoRoot. Create it first (for example: bash scripts/prepare-ci-compose-env.sh)."
    }

    $minioImage = $env:MINIO_IMAGE
    if (-not $minioImage) {
        $line = Get-Content '.env' | Where-Object { $_ -match '^\s*MINIO_IMAGE\s*=\s*\S' } | Select-Object -First 1
        if ($line) { $minioImage = ($line -split '=', 2)[1].Trim() }
    }

    $services = @('postgresql', 'rabbitmq', 'keycloak', 'clamav')
    if ($minioImage) {
        docker compose -f docker-compose.yml -f docker-compose.override.yml -f docker-compose.services.yml `
            -f docker-compose.object-storage.yml `
            up -d @services minio
    }
    else {
        docker compose up -d @services
        Write-Host "MinIO skipped (MINIO_IMAGE not set); File.Storage uses the Local provider." -ForegroundColor Yellow
    }
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed with exit code $LASTEXITCODE" }

    Write-Host "Infrastructure up. Run services locally: scripts/dev-run-hrcore.ps1" -ForegroundColor Green
}
finally {
    Pop-Location
}
