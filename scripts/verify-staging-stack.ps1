# Verifies HRManagement staging/full-stack health after compose is up.
# Usage:
#   docker compose -f docker-compose.yml -f docker-compose.services.yml up -d --wait
#   pwsh scripts/verify-staging-stack.ps1

param(
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:5000" })
)

$ErrorActionPreference = "Stop"
$slugs = @(
    "hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training",
    "payroll", "idcard", "records-archive", "admin-logistics", "it-infra",
    "dev-analytics", "compliance", "auth-directory", "file-storage"
)

function Invoke-HealthRequest {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [int]$TimeoutSec = 15,
        [int]$Attempts = 3
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            return Invoke-WebRequest -Uri $Uri -UseBasicParsing -TimeoutSec $TimeoutSec
        }
        catch {
            if ($attempt -eq $Attempts) { throw }
            Start-Sleep -Seconds 2
        }
    }
}

Write-Host "Gateway: $GatewayBaseUrl"
$failures = @()

try {
    $live = Invoke-HealthRequest -Uri "$GatewayBaseUrl/health/live" -TimeoutSec 20
    Write-Host "OK  /health/live -> $($live.StatusCode)"
} catch {
    $failures += "/health/live: $($_.Exception.Message)"
}

foreach ($slug in $slugs) {
    $uri = "$GatewayBaseUrl/health/upstreams/$slug"
    try {
        $r = Invoke-HealthRequest -Uri $uri -TimeoutSec 20
        $status = if ($r.StatusCode -eq 200) { "OK" } else { "WARN" }
        Write-Host "$status  $uri -> $($r.StatusCode)"
        if ($r.StatusCode -ge 500) { $failures += "$uri -> $($r.StatusCode)" }
    } catch {
        $failures += "$uri -> $($_.Exception.Message)"
        Write-Host "FAIL $uri"
    }
}

if ($failures.Count -gt 0) {
    Write-Error ("Staging health verification failed:`n" + ($failures -join "`n"))
}
Write-Host "Staging health verification passed."
