<#
.SYNOPSIS
    BLD-002: benchmark cold vs warm rebuild for a single service (default: hr-core-web).
.EXAMPLE
    .\scripts\bld002-benchmark-build.ps1
    .\scripts\bld002-benchmark-build.ps1 -Service hr-contracts-web
#>
param(
    [string]$Service = 'hr-core-web',
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [int]$WarmTargetSeconds = 120
)

$ErrorActionPreference = 'Stop'
$env:DOCKER_BUILDKIT = '1'
$env:COMPOSE_PARALLEL_LIMIT = '2'

Push-Location $RepoRoot
try {
    & "$PSScriptRoot/docker-generate-build-manifest.ps1" -RepoRoot $RepoRoot

    $compose = @('-f', 'docker-compose.yml', '-f', 'docker-compose.services.yml', '--profile', 'full-stack')

    function Invoke-TimedBuild([string]$Label, [string[]]$ExtraArgs) {
        $sw = [System.Diagnostics.Stopwatch]::StartNew()
        docker compose @compose @('build') @ExtraArgs $Service 2>&1 | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Build failed: $Label" }
        $sw.Stop()
        [pscustomobject]@{ Label = $Label; Seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1) }
    }

    Write-Host "=== BLD-002 benchmark: $Service ===" -ForegroundColor Cyan

    $cold = Invoke-TimedBuild 'cold (no-cache)' @('--no-cache')
    Write-Host "Cold build: $($cold.Seconds)s" -ForegroundColor Yellow

    $touchFile = Join-Path $RepoRoot 'src/Services/HRCore/HR.Core.Web/Program.cs'
    if (-not (Test-Path $touchFile)) {
        $touchFile = Get-ChildItem -Path (Join-Path $RepoRoot 'src') -Recurse -Filter 'Program.cs' |
            Select-Object -First 1 -ExpandProperty FullName
    }
    if ($touchFile) {
        (Get-Item $touchFile).LastWriteTime = Get-Date
    }

    $warm = Invoke-TimedBuild 'warm (after single-file touch)' @()
    Write-Host "Warm rebuild: $($warm.Seconds)s (target < ${WarmTargetSeconds}s)" -ForegroundColor Yellow

    if ($warm.Seconds -gt $WarmTargetSeconds) {
        Write-Warning "Warm rebuild exceeded ${WarmTargetSeconds}s target — check BuildKit cache and manifest layer."
    }
    else {
        Write-Host "Acceptance criterion met: warm rebuild < ${WarmTargetSeconds}s" -ForegroundColor Green
    }

    [pscustomobject]@{
        Service = $Service
        ColdSeconds = $cold.Seconds
        WarmSeconds = $warm.Seconds
        WarmTargetSeconds = $WarmTargetSeconds
    }
}
finally {
    Pop-Location
}
