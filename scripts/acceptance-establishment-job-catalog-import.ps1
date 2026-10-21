# PLAN-19 AC-EST-15..17: establishment job catalog bulk import acceptance.
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
        Write-Host "Starting acceptance stack..."
        & (Join-Path $PSScriptRoot "acceptance-up.ps1") -SkipLiveVerification
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
    }

    & (Join-Path $PSScriptRoot "est006-validate.ps1") -RepoRoot $RepoRoot

    $env:RUN_ACCEPTANCE = "true"
    dotnet test (Join-Path $RepoRoot "src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj") --filter "AcEst15|AcEst16|AcEst17"
    if ($LASTEXITCODE -ne 0) { throw "Acceptance tests AcEst15|AcEst16|AcEst17 failed" }

    Write-Host "acceptance-establishment-job-catalog-import: PASS"
}
finally {
    Pop-Location
}
