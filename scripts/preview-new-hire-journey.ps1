#Requires -Version 5.1
<#
.SYNOPSIS
  Rebuild the Gateway with the latest new-hire journey catalog and print preview URLs.
.EXAMPLE
  pwsh scripts/preview-new-hire-journey.ps1
  pwsh scripts/preview-new-hire-journey.ps1 -SkipDockerRebuild
#>
param(
    [switch]$SkipDockerRebuild,
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:6200" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host "== new-hire journey preview ==" -ForegroundColor Cyan

Write-Host "`n[1/3] Unit tests (GatewayJourneyCatalogTests)..." -ForegroundColor Yellow
dotnet test "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj" `
    --filter "FullyQualifiedName~GatewayJourneyCatalogTests" `
    --verbosity quiet
if ($LASTEXITCODE -ne 0) { throw "GatewayJourneyCatalogTests failed." }

Write-Host "`n[2/3] Catalog summary..." -ForegroundColor Yellow
$summaryScript = @'
using HRManagement.Gateway.Services;
var j = new GatewayJourneyCatalog().FindById("new-hire");
if (j is null) { Console.Error.WriteLine("new-hire journey not found"); Environment.Exit(1); }
Console.WriteLine($"Title: {j.Title}");
Console.WriteLine($"Description: {j.Description}");
Console.WriteLine($"Steps: {j.Steps.Count} (recruitment={j.Steps.Count(s=>s.PhaseKey==\"recruitment\")}, onboarding={j.Steps.Count(s=>s.PhaseKey==\"onboarding\")})");
Console.WriteLine();
foreach (var g in j.Steps.GroupBy(s => s.PhaseKey ?? "none"))
{
    var title = g.First().PhaseTitleAr ?? g.Key;
    Console.WriteLine($"--- {title} ({g.Count()} steps) ---");
    var i = 1;
    foreach (var s in g)
    {
        var path = string.IsNullOrWhiteSpace(s.GatewayPublicPath) ? (s.UiPath ?? s.ModuleSlug ?? "irshadi") : s.GatewayPublicPath;
        var opt = s.IsRequired ? "" : " [optional]";
        Console.WriteLine($"  {i,2}. {s.Title}{opt} -> {path}");
        i++;
    }
    Console.WriteLine();
}
'@
dotnet run --project "src/Gateway/HRManagement.Gateway/HRManagement.Gateway.csproj" --no-build 2>$null | Out-Null
# Use csharp via dotnet script alternative - inline eval through temp project is heavy; print via grep-friendly approach
$catalogPath = Join-Path $root "src/Gateway/HRManagement.Gateway/Services/GatewayJourneyCatalog.cs"
$recruitment = ([regex]::Matches((Get-Content -Raw $catalogPath), 'PhaseKey = "recruitment"')).Count
$onboarding = ([regex]::Matches((Get-Content -Raw $catalogPath), 'PhaseKey = "onboarding"')).Count
$careers = ([regex]::Matches((Get-Content -Raw $catalogPath), 'GatewayPublicPath = "/careers"')).Count
Write-Host "  recruitment steps : $recruitment"
Write-Host "  onboarding steps  : $onboarding"
Write-Host "  /careers step     : $careers"
Write-Host "  total steps       : $($recruitment + $onboarding)"

if (-not $SkipDockerRebuild) {
    Write-Host "`n[3/3] Rebuilding hr-gateway in hrm-acceptance..." -ForegroundColor Yellow
    $projectName = "hrm-acceptance"
    $env:COMPOSE_PROJECT_NAME = $projectName
    $env:GATEWAY_BASE_URL = $GatewayBaseUrl

    & "$PSScriptRoot/acceptance-local-env.ps1" | Out-Null
    Import-AcceptanceLocalEnvironment -RepoRoot $root

    $composeArgs = @(
        "-p", $projectName,
        "-f", "docker-compose.yml",
        "-f", "docker-compose.services.yml",
        "-f", "docker-compose.dev-profiles.yml",
        "-f", "docker-compose.full-stack-gateway.yml",
        "-f", "docker-compose.acceptance.yml",
        "--profile", "full-stack"
    )

    docker compose @composeArgs build hr-gateway
    if ($LASTEXITCODE -ne 0) { throw "docker compose build hr-gateway failed." }

    docker compose @composeArgs up -d --no-build hr-gateway
    if ($LASTEXITCODE -ne 0) { throw "docker compose up hr-gateway failed." }

    Write-Host "  Waiting for gateway health..." -ForegroundColor DarkGray
    $deadline = (Get-Date).AddMinutes(3)
    do {
        Start-Sleep -Seconds 3
        try {
            $code = (Invoke-WebRequest -Uri ($GatewayBaseUrl.TrimEnd('/') + "/health/live") -TimeoutSec 5 -UseBasicParsing).StatusCode
            if ($code -eq 200) { break }
        } catch { }
    } while ((Get-Date) -lt $deadline)
} else {
    Write-Host "`n[3/3] Skipped Docker rebuild (-SkipDockerRebuild)." -ForegroundColor DarkGray
}

$journeyUrl = ($GatewayBaseUrl.TrimEnd('/') + "/Home/JourneyDetails/new-hire")
$hireUrl = ($GatewayBaseUrl.TrimEnd('/') + "/Home/HireJourney")
$careersUrl = ($GatewayBaseUrl.TrimEnd('/') + "/careers")

Write-Host "`n== Open in browser ==" -ForegroundColor Green
Write-Host "  Journey (new-hire): $journeyUrl"
Write-Host "  Hire journey entry: $hireUrl"
Write-Host "  Public careers:     $careersUrl"
Write-Host "`nLog in with an hr-core account (see .acceptance/accounts.local.md) if redirected to Keycloak."
Write-Host "Expect two phase headers: HCM-001 recruitment (12 steps) then FUN-001 onboarding (9 steps)."

try {
    Start-Process $journeyUrl | Out-Null
    Write-Host "`nBrowser opened." -ForegroundColor Green
} catch {
    Write-Host "`nCould not auto-open browser; copy the URL above." -ForegroundColor Yellow
}
