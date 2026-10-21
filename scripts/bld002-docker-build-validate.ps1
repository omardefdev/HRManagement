<#
.SYNOPSIS
    BLD-002: validate unified Docker build setup (compose, dockerignore, manifest generator).
#>
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Push-Location $RepoRoot
try {
    $failures = [System.Collections.Generic.List[string]]::new()

    $dockerfile = Join-Path $RepoRoot 'docker/DotnetService.Dockerfile'
    if (-not (Test-Path $dockerfile)) {
        $failures.Add('Missing docker/DotnetService.Dockerfile')
    }
    else {
        $df = Get-Content $dockerfile -Raw
        if ($df -notmatch 'type=cache,id=hrmanagement-nuget,sharing=locked') {
            $failures.Add('Unified Dockerfile must use BuildKit NuGet cache mount with sharing=locked')
        }
        if ($df -notmatch '--locked-mode') {
            $failures.Add('Unified Dockerfile must restore with --locked-mode')
        }
        if ($df -match 'COPY \. \.\s*\r?\n\s*RUN[^\n]*dotnet restore') {
            $failures.Add('Unified Dockerfile must not COPY full source before restore')
        }
    }

    $ignore = Get-Content (Join-Path $RepoRoot '.dockerignore') -Raw
    foreach ($pattern in @('deploy/', 'terraform/', 'perf/', 'chaos/', 'testdata/', '.env', 'secrets/')) {
        if ($ignore -notmatch [regex]::Escape($pattern)) {
            $failures.Add(".dockerignore missing exclusion: $pattern")
        }
    }
    if ($ignore -notmatch '!docker/build-manifest/') {
        $failures.Add('.dockerignore must allow docker/build-manifest/ for restore layer')
    }
    if ($ignore -notmatch '!docker/DotnetService\.Dockerfile') {
        $failures.Add('.dockerignore must allow docker/DotnetService.Dockerfile')
    }

    $matrixPath = Join-Path $RepoRoot 'docker/service-build-matrix.json'
    if (-not (Test-Path $matrixPath)) {
        $failures.Add('Missing docker/service-build-matrix.json')
    }
    else {
        $matrix = Get-Content $matrixPath -Raw | ConvertFrom-Json
        $composeText = @(
            (Get-Content 'docker-compose.yml' -Raw),
            (Get-Content 'docker-compose.services.yml' -Raw)
        ) -join "`n"

        foreach ($entry in $matrix) {
            if ($composeText -notmatch "PROJECT_CSPROJ:\s*$([regex]::Escape($entry.projectCsproj))") {
                $failures.Add("Compose missing PROJECT_CSPROJ for $($entry.service)")
            }
            if ($composeText -notmatch "docker/DotnetService\.Dockerfile") {
                $failures.Add('Compose must reference docker/DotnetService.Dockerfile')
                break
            }
        }
    }

    $legacy = Get-ChildItem -Path $RepoRoot -Recurse -Filter Dockerfile -File |
        Where-Object { $_.FullName -notmatch '\\docker\\' -and $_.FullName -match '\\src\\' }
    foreach ($legacyFile in $legacy) {
        $content = Get-Content $legacyFile.FullName -Raw
        if ($content -match '^\s*FROM\s' -and $content -match 'COPY \. \.') {
            $rel = $legacyFile.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
            $failures.Add("Legacy per-service Dockerfile still copies full context before restore: $rel")
        }
    }

    & "$PSScriptRoot/docker-generate-build-manifest.ps1" -RepoRoot $RepoRoot
    $manifestCount = (Get-ChildItem (Join-Path $RepoRoot 'docker/build-manifest') -Recurse -File -Filter '*.csproj' | Measure-Object).Count
    if ($manifestCount -lt 15) {
        $failures.Add("Expected at least 15 csproj files in docker/build-manifest (found $manifestCount)")
    }
    $lockCount = (Get-ChildItem (Join-Path $RepoRoot 'docker/build-manifest') -Recurse -File -Filter 'packages.lock.json' | Measure-Object).Count
    if ($lockCount -lt 10) {
        $failures.Add("Expected packages.lock.json files in build manifest (found $lockCount)")
    }

    foreach ($path in @(
            'docker-compose.dev-profiles.yml',
            'docker-compose.dev-logistics.yml',
            'docker-compose.dev-contracts.yml',
            'docker-compose.full-stack-gateway.yml',
            'scripts/dev-profile-up.ps1',
            'scripts/dev-profile-down.ps1'
        )) {
        if (-not (Test-Path (Join-Path $RepoRoot $path))) {
            $failures.Add("Missing dev profile artifact: $path")
        }
    }

    $devProfiles = Get-Content (Join-Path $RepoRoot 'docker-compose.dev-profiles.yml') -Raw
    if ($devProfiles -notmatch 'dev-logistics') {
        $failures.Add('docker-compose.dev-profiles.yml must define dev-logistics profile')
    }
    if ($devProfiles -notmatch 'dev-contracts') {
        $failures.Add('docker-compose.dev-profiles.yml must define dev-contracts profile')
    }

    $readme = Get-Content (Join-Path $RepoRoot 'README.md') -Raw
    if ($readme -notmatch 'dev-infra-up|dev-run-hrcore|dev-build-service|full-stack|dev-profile-up') {
        $failures.Add('README must document BLD-002 dev workflow scripts, dev-profile-up, and full-stack profile')
    }

    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Error $_ }
        throw "BLD-002 validation failed with $($failures.Count) issue(s)."
    }

    Write-Host 'BLD-002 Docker build validation OK' -ForegroundColor Green
}
finally {
    Pop-Location
}
