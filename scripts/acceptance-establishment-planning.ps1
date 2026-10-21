# PLAN-17 AC-EST-10..12: establishment planning acceptance (gap + finance reconciliation).
# Usage:
#   powershell -File scripts/acceptance-establishment-planning.ps1
#   powershell -File scripts/acceptance-establishment-planning.ps1 -SkipStackUp

param(
    [switch]$SkipStackUp,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

Push-Location $RepoRoot
try {
    if (-not $SkipStackUp) {
        Write-Host "Starting acceptance stack (if not already running)..."
        & (Join-Path $PSScriptRoot "acceptance-up.ps1")
        if ($LASTEXITCODE -ne 0) { throw "acceptance-up.ps1 failed with exit code $LASTEXITCODE" }
    }
    else {
        . (Join-Path $PSScriptRoot "acceptance-local-env.ps1")
        Import-AcceptanceLocalEnvironment -RepoRoot $RepoRoot
        if (-not $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET) {
            $envLine = Get-Content (Join-Path $RepoRoot ".env") |
                Where-Object { $_ -match '^\s*KEYCLOAK_HR_GATEWAY_CLIENT_SECRET=' } |
                Select-Object -First 1
            if ($envLine) {
                $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET = ($envLine -split '=', 2)[1].Trim().Trim('"').Trim("'")
            }
        }
        if (-not $env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL = "http://localhost:6200" }
        if (-not $env:KEYCLOAK_BASE_URL) { $env:KEYCLOAK_BASE_URL = "http://localhost:6388" }
        if (-not $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET) {
            throw "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET is missing from the process and .env."
        }
    }

    $env:RUN_ACCEPTANCE = "true"
    Write-Host "Running AC-EST-10..12 acceptance tests..."
    dotnet test src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj `
        --filter "FullyQualifiedName~AcEst10|FullyQualifiedName~AcEst11|FullyQualifiedName~AcEst12" `
        --no-restore
    if ($LASTEXITCODE -ne 0) { throw "Acceptance tests failed" }

    Write-Host "Running est004-validate.ps1..."
    & (Join-Path $PSScriptRoot "est004-validate.ps1")
    if ($LASTEXITCODE -ne 0) { throw "est004-validate.ps1 failed" }

    Write-Host "PLAN-17 establishment planning acceptance passed." -ForegroundColor Green
} finally {
    Pop-Location
}
