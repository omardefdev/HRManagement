<#
.SYNOPSIS

    BLD-002: build and start a single Docker service (not the full stack).

.EXAMPLE

    .\scripts\dev-build-service.ps1 -Service hr-core-web

    .\scripts\dev-build-service.ps1 -Service admin-logistics-web -Profile logistics

#>

param(

    [Parameter(Mandatory = $true)]

    [string]$Service,

    [ValidateSet('full-stack', 'logistics', 'payroll', 'contracts')]

    [string]$Profile = 'full-stack',

    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),

    [switch]$NoCache

)



$ErrorActionPreference = 'Stop'

$env:DOCKER_BUILDKIT = '1'

if (-not $env:COMPOSE_PARALLEL_LIMIT) { $env:COMPOSE_PARALLEL_LIMIT = '2' }



$composeProfile = if ($Profile -eq 'full-stack') { 'full-stack' } else { "dev-$Profile" }



Push-Location $RepoRoot

try {

    & "$PSScriptRoot/docker-generate-build-manifest.ps1" -RepoRoot $RepoRoot



    $compose = @(

        '-f', 'docker-compose.yml',

        '-f', 'docker-compose.services.yml',

        '-f', 'docker-compose.local.yml',

        '-f', 'docker-compose.dev-profiles.yml'

    )



    if ($Profile -eq 'full-stack') {
        $compose += @('-f', 'docker-compose.full-stack-gateway.yml')
    } else {
        $profileOverride = "docker-compose.dev-$Profile.yml"
        if (Test-Path (Join-Path $RepoRoot $profileOverride)) {
            $compose += @('-f', $profileOverride)
        }
    }

    $compose += @('--profile', $composeProfile)



    $buildArgs = @('compose') + $compose + @('build')

    if ($NoCache) { $buildArgs += '--no-cache' }

    $buildArgs += $Service

    docker @buildArgs

    if ($LASTEXITCODE -ne 0) { throw "docker compose build failed for $Service" }



    docker compose @compose up -d $Service

    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed for $Service" }



    Write-Host "OK: $Service built and started (profile $composeProfile)" -ForegroundColor Green

}

finally {

    Pop-Location

}

