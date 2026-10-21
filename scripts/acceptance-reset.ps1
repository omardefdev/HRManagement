#Requires -Version 5.1
param(
    [switch]$RotateCredentials
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$projectName = "hrm-acceptance"
if ($projectName -ne "hrm-acceptance") {
    throw "Safety check failed: only the hrm-acceptance Compose project may be reset."
}

& "$PSScriptRoot/prepare-keycloak-import.ps1" -Profile Acceptance -RotateAcceptanceCredentials:$RotateCredentials
. "$PSScriptRoot/acceptance-local-env.ps1"
Import-AcceptanceLocalEnvironment -RepoRoot $root

$composeFiles = @(
    "docker-compose.yml",
    "docker-compose.services.yml",
    "docker-compose.dev-profiles.yml",
    "docker-compose.full-stack-gateway.yml",
    "docker-compose.acceptance.yml"
)
$composeArgs = $composeFiles | ForEach-Object { "-f", $_ }

Write-Host "Stopping Acceptance v2 and deleting only project-scoped volumes for $projectName..."
docker compose -p $projectName @composeArgs --profile full-stack down --volumes --remove-orphans
if ($LASTEXITCODE -ne 0) { throw "Acceptance reset failed with exit code $LASTEXITCODE" }

Write-Host "Acceptance v2 data volumes were reset. Local credentials were preserved: $(-not $RotateCredentials)"
