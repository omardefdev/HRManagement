<#
.SYNOPSIS
    IAM-004: Re-apply per-service PostgreSQL roles on an existing Postgres instance (idempotent).

.EXAMPLE
    pwsh scripts/apply-postgres-service-roles.ps1
    pwsh scripts/apply-postgres-service-roles.ps1 -UseDockerExec
#>
param(
    [string]$PostgresHost = $(if ($env:POSTGRES_HOST) { $env:POSTGRES_HOST } else { "localhost" }),
    [int]$PostgresPort = [int]($(if ($env:POSTGRES_PORT) { $env:POSTGRES_PORT } else { "5433" })),
    [string]$PostgresUser = $(if ($env:POSTGRES_USER) { $env:POSTGRES_USER } else { "hrmanagement" }),
    [string]$PostgresPassword = $(if ($env:POSTGRES_PASSWORD) { $env:POSTGRES_PASSWORD } else { $env:PGPASSWORD }),
    [string]$PostgresAppPassword = $(if ($env:POSTGRES_APP_PASSWORD) { $env:POSTGRES_APP_PASSWORD } else { $PostgresPassword }),
    [string]$ContainerName = "hrm-postgresql",
    [switch]$UseDockerExec
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$initScript = Join-Path $root "docker/init-service-roles.sh"

if (-not (Test-Path $initScript)) {
    throw "Missing $initScript"
}

if ($UseDockerExec -or ($PostgresHost -eq "localhost" -and (docker ps --format "{{.Names}}" 2>$null) -match $ContainerName)) {
    Write-Host "Applying service roles via docker exec on $ContainerName..." -ForegroundColor Cyan
    docker exec `
        -e POSTGRES_USER=$PostgresUser `
        -e POSTGRES_PASSWORD=$PostgresPassword `
        -e POSTGRES_APP_PASSWORD=$PostgresAppPassword `
        -e POSTGRES_ALLOW_APP_DDL=false `
        $ContainerName `
        bash /docker-entrypoint-initdb.d/02-init-service-roles.sh
    if ($LASTEXITCODE -ne 0) { throw "init-service-roles.sh failed with exit code $LASTEXITCODE" }
    Write-Host "Service roles applied." -ForegroundColor Green
    exit 0
}

if (-not (Get-Command bash -ErrorAction SilentlyContinue)) {
    throw "bash not found; use -UseDockerExec or install Git Bash/WSL."
}

Write-Host "Applying service roles via bash against ${PostgresHost}:${PostgresPort}..." -ForegroundColor Cyan
$env:POSTGRES_USER = $PostgresUser
$env:POSTGRES_PASSWORD = $PostgresPassword
$env:POSTGRES_APP_PASSWORD = $PostgresAppPassword
$env:POSTGRES_ALLOW_APP_DDL = "false"
$env:PGHOST = $PostgresHost
$env:PGPORT = "$PostgresPort"
# PLAN-38: the wrapper runs deploy/bootstrap/ensure-databases.sql and ensure-roles.sql from the repository.
$env:HRM_BOOTSTRAP_SQL_DIR = Join-Path $root "deploy/bootstrap"

bash $initScript
if ($LASTEXITCODE -ne 0) { throw "init-service-roles.sh failed with exit code $LASTEXITCODE" }
Write-Host "Service roles applied." -ForegroundColor Green
