# Full acceptance verification run (AC-01..AC-37).
# Raises stack via acceptance-up.ps1, runs dotnet acceptance tests, rebuilds summary.
#
# Usage:
#   pwsh scripts/acceptance-run-all.ps1
#   pwsh scripts/acceptance-run-all.ps1 -SkipStackUp -Filter "FullyQualifiedName~Ac01"

param(
    [switch]$SkipStackUp,
    [switch]$SkipSeedIdempotency,
    [switch]$PreserveEvidence,
    [switch]$UseLocalBaseImages,
    [string]$Filter = "",
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:6200" }),
    [string]$KeycloakBaseUrl = $(if ($env:KEYCLOAK_BASE_URL) { $env:KEYCLOAK_BASE_URL } else { "http://localhost:6388" }),
    [string]$EvidenceDir = $(if ($env:ACCEPTANCE_EVIDENCE_DIR) { $env:ACCEPTANCE_EVIDENCE_DIR } else { "artifacts/acceptance/evidence" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Read-DotEnvValue {
    param([Parameter(Mandatory)][string]$Name)
    $path = Join-Path $root ".env"
    if (-not (Test-Path -LiteralPath $path)) { return $null }
    $line = Get-Content -LiteralPath $path |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Name))=" } |
        Select-Object -First 1
    if (-not $line) { return $null }
    return $line.Split("=", 2)[1].Trim().Trim('"').Trim("'")
}

if (-not (Test-Path -LiteralPath (Join-Path $root ".acceptance/credentials.json"))) {
    & "$PSScriptRoot/prepare-keycloak-import.ps1" -Profile Acceptance
}
. "$PSScriptRoot/acceptance-local-env.ps1"
Import-AcceptanceLocalEnvironment -RepoRoot $root

if (-not $SkipStackUp) {
    & "$PSScriptRoot/acceptance-up.ps1" -GatewayBaseUrl $GatewayBaseUrl -UseLocalBaseImages:$UseLocalBaseImages
}

$env:RUN_ACCEPTANCE = "true"
$env:GATEWAY_BASE_URL = $GatewayBaseUrl
$env:KEYCLOAK_BASE_URL = $KeycloakBaseUrl
$env:COMPOSE_PROJECT_NAME = "hrm-acceptance"
$env:POSTGRES_HOST = "localhost"
$env:POSTGRES_PORT = "6433"
if (-not $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET) {
    $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET = Read-DotEnvValue "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"
}
if (-not $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET) {
    throw "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET is missing from the process and .env."
}
if (-not $env:POSTGRES_PASSWORD) {
    $env:POSTGRES_PASSWORD = Read-DotEnvValue "POSTGRES_PASSWORD"
}
if (-not $env:POSTGRES_APP_PASSWORD) {
    $env:POSTGRES_APP_PASSWORD = Read-DotEnvValue "POSTGRES_APP_PASSWORD"
}
$env:ACCEPTANCE_EVIDENCE_DIR = (Resolve-Path -LiteralPath $EvidenceDir -ErrorAction SilentlyContinue)?.Path
if (-not $env:ACCEPTANCE_EVIDENCE_DIR) {
    $env:ACCEPTANCE_EVIDENCE_DIR = (Join-Path $root $EvidenceDir)
    New-Item -ItemType Directory -Force -Path $env:ACCEPTANCE_EVIDENCE_DIR | Out-Null
}

if (-not $Filter -and -not $PreserveEvidence) {
    $evidenceRoot = [System.IO.Path]::GetFullPath($env:ACCEPTANCE_EVIDENCE_DIR)
    $allowedRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts/acceptance/evidence"))
    if (-not $evidenceRoot.Equals($allowedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear evidence outside the canonical acceptance evidence directory: $evidenceRoot"
    }

    Write-Host "== Clearing prior AC-01..AC-37 evidence =="
    foreach ($number in 1..37) {
        $scenarioPath = [System.IO.Path]::GetFullPath((Join-Path $evidenceRoot ("AC-{0:D2}" -f $number)))
        if (-not $scenarioPath.StartsWith($evidenceRoot + [System.IO.Path]::DirectorySeparatorChar,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unsafe evidence path resolved for deletion: $scenarioPath"
        }
        if (Test-Path -LiteralPath $scenarioPath) {
            Remove-Item -LiteralPath $scenarioPath -Recurse -Force
        }
    }
    Remove-Item -LiteralPath (Join-Path $evidenceRoot "summary.md") -Force -ErrorAction SilentlyContinue
}

Write-Host "== Verifying the Acceptance v2 baseline =="
$verifyParameters = @{
    ComposeProjectName = "hrm-acceptance"
    GatewayBaseUrl = $GatewayBaseUrl
    OutputDirectory = "artifacts/acceptance/verification"
}
if (-not $SkipSeedIdempotency) { $verifyParameters.VerifyIdempotency = $true }
& "$PSScriptRoot/acceptance-verify.ps1" @verifyParameters
if ($LASTEXITCODE -ne 0) { throw "Acceptance v2 baseline verification failed." }

Write-Host "== Verifying PostgreSQL service-role isolation =="
& "$PSScriptRoot/acceptance-verify-db-isolation.ps1" -ComposeProjectName "hrm-acceptance" -EvidenceDir $env:ACCEPTANCE_EVIDENCE_DIR
if ($LASTEXITCODE -ne 0) { throw "PostgreSQL service-role isolation failed." }

Write-Host "== Building acceptance tests =="
dotnet build src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj -c Release --no-restore 2>$null
if ($LASTEXITCODE -ne 0) {
    dotnet restore src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj --locked-mode
    dotnet build src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw "Acceptance test build failed" }
}

Write-Host "== Running coverage/consistency checks (no live stack required) =="
dotnet test src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj `
    -c Release --no-build `
    --filter "FullyQualifiedName~AcceptanceV2CatalogTests|FullyQualifiedName~AcceptanceCoverageTests|FullyQualifiedName~AcceptanceTenantConsistencyTests|FullyQualifiedName~AcceptanceEvidenceSummaryTests"
if ($LASTEXITCODE -ne 0) { throw "Static acceptance checks failed" }

Write-Host "== Running live acceptance tests (RUN_ACCEPTANCE=true) =="
$testArgs = @(
    "test", "src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj",
    "-c", "Release", "--no-build",
    "--logger", "trx;LogFileName=acceptance-results.trx",
    "--results-directory", "./TestResults/acceptance"
)
if ($Filter) {
    $testArgs += @("--filter", $Filter)
}
& dotnet @testArgs
$testExit = $LASTEXITCODE

Write-Host "== Rebuilding acceptance-evidence/summary.md =="
& "$PSScriptRoot/acceptance-evidence-summarize.ps1" -EvidenceDir $env:ACCEPTANCE_EVIDENCE_DIR

if ($testExit -ne 0) {
    throw "Acceptance tests failed with exit code $testExit"
}

Write-Host "Acceptance verification complete. Evidence: $($env:ACCEPTANCE_EVIDENCE_DIR)"
Write-Host "Summary: $(Join-Path $env:ACCEPTANCE_EVIDENCE_DIR 'summary.md')"
