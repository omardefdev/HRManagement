# Starts full stack (if needed), verifies health, runs Playwright E2E.
# Requires: Docker, .NET 10 SDK, E2E_KEYCLOAK_USER / E2E_KEYCLOAK_PASSWORD in environment or .env

param(
    [switch]$SkipComposeUp,
    [string]$GatewayBaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $SkipComposeUp) {
    Write-Host "Starting full stack..."
    docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait
}

& "$PSScriptRoot/verify-staging-stack.ps1" -GatewayBaseUrl $GatewayBaseUrl

$env:RUN_PLAYWRIGHT_E2E = "true"
$env:GATEWAY_BASE_URL = $GatewayBaseUrl

dotnet build src/Tests/HRManagement.E2E/HRManagement.E2E.csproj -c Release
$e2eBin = "src/Tests/HRManagement.E2E/bin/Release/net10.0"
pwsh "$e2eBin/playwright.ps1" install --with-deps
# Smoke + journeys
dotnet test src/Tests/HRManagement.E2E/HRManagement.E2E.csproj -c Release --no-build

Write-Host "Staging E2E completed."
