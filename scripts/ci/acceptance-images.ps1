#Requires -Version 5.1
# PLAN-66: the images of the parallel Acceptance job. One job builds every service image once and pulls every third-party image
# once; the stage jobs load that archive, check that each service image carries the commit under test, and pull from no registry.
#   -Action Build   build the service images of the lab stack and hr-migrate, pull the rest, write images.json beside the archive
#   -Action Save    one zstd-compressed docker save of everything in images.json
#   -Action Load    docker load of that archive
#   -Action Verify  exit 1 when a service image is missing or carries another revision, or a third-party image is missing
# The images hold what the Dockerfiles copy: .dockerignore keeps .env, keys, .acceptance/ and docker/** out of the context.
# ASCII only; Windows PowerShell 5.1 and PowerShell 7 (Save and Load need bash and zstd).
param(
    [Parameter(Mandatory)][ValidateSet("Build", "Save", "Load", "Verify")][string]$Action,
    [string]$SourceSha = "",
    [string]$Directory = "artifacts/acceptance/images",
    [string]$RepoRoot = "",
    # Tests: {"image": "revision"} instead of docker inspect; an image that is not listed does not exist.
    [string]$InspectFile = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
Set-Location $RepoRoot
$scripts = Join-Path $RepoRoot "scripts"
. (Join-Path $scripts "acceptance-evidence-lib.ps1")

$folder = if ([System.IO.Path]::IsPathRooted($Directory)) { $Directory } else { Join-Path $RepoRoot $Directory }
$manifestPath = Join-Path $folder "images.json"
$archive = Join-Path $folder "images.tar.zst"
$migrateImage = "hr-migrate:local"
$labFiles = @("docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml",
    "docker-compose.full-stack-gateway.yml", "docker-compose.acceptance.yml")
$prodLikeFiles = $labFiles + @("docker-compose.acceptance-prodlike.yml")
$baseFiles = @("docker-compose.yml", "docker-compose.services.yml")
$hasPwsh7 = $PSVersionTable.PSVersion.Major -ge 7

# A registry that answers 503 for a moment (mcr.microsoft.com, run 41951228726) must not end the run.
function Invoke-WithRetry {
    param([Parameter(Mandatory)][string]$What, [Parameter(Mandatory)][scriptblock]$Body, [int]$Attempts = 3, [int]$WaitSeconds = 60)
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            & $Body
            if ($LASTEXITCODE -eq 0) { return }
            Write-Host "::warning::$What failed with exit code $LASTEXITCODE (attempt $attempt of $Attempts)"
        }
        catch {
            Write-Host "::warning::$What failed: $($_.Exception.Message) (attempt $attempt of $Attempts)"
        }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $WaitSeconds }
    }
    throw "$What failed after $Attempts attempts."
}

function Get-ComposeServices {
    param([Parameter(Mandatory)][string[]]$Files, [string]$Project = "hrm-acceptance")
    $arguments = @("compose", "-p", $Project) + @($Files | ForEach-Object { "-f", $_ }) + @("--profile", "full-stack", "config", "--format", "json")
    $text = (& docker @arguments) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "docker compose config failed for: $($Files -join ', ')" }
    return @(($text | ConvertFrom-Json).services.PSObject.Properties)
}

function Read-ImageManifest {
    $manifest = Read-AcceptanceJson -Path $manifestPath
    if ($null -eq $manifest) { throw "No image manifest at $manifestPath." }
    return $manifest
}

# The revision of each image, or $null for one that does not exist.
function Get-ImageRevision {
    param([Parameter(Mandatory)][string]$Image, $Fixture)
    if ($null -ne $Fixture) {
        $property = $Fixture.PSObject.Properties[$Image]
        if ($null -eq $property) { return $null }
        return [string]$property.Value
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $revision = docker image inspect --format '{{ index .Config.Labels "org.opencontainers.image.revision" }}' $Image 2>$null
        if ($LASTEXITCODE -ne 0) { return $null }
        return ([string]$revision).Trim()
    }
    finally { $ErrorActionPreference = $previous }
}

switch ($Action) {
    "Build" {
        if ([string]::IsNullOrWhiteSpace($SourceSha)) { $SourceSha = (git rev-parse HEAD).Trim() }
        git diff --quiet HEAD --
        $revision = if ($LASTEXITCODE -eq 0) { $SourceSha } else { "$SourceSha-dirty" }
        New-Item -ItemType Directory -Force -Path $folder | Out-Null
        $shell = if ($hasPwsh7) { "pwsh" } else { "powershell" }
        if ($env:OS -ne "Windows_NT") {
            bash scripts/prepare-ci-compose-env.sh
            if ($LASTEXITCODE -ne 0) { throw "prepare-ci-compose-env.sh failed with exit code $LASTEXITCODE" }
        }
        Invoke-WithRetry -What "The service image build" -Body {
            & $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts "acceptance-up.ps1") -Profile Lab -SourceSha $SourceSha -BuildOnly
        }
        Invoke-WithRetry -What "The hr-migrate build" -Body {
            # COPY --exclude needs Dockerfile frontend 1.19: as in acceptance-job.ps1.
            docker build -f docker/Migrator.Dockerfile -t $migrateImage --build-arg BUILDKIT_SYNTAX=docker/dockerfile:1.19 --build-arg "SOURCE_SHA=$revision" .
        }

        . (Join-Path $scripts "acceptance-local-env.ps1")
        Import-AcceptanceLocalEnvironment -RepoRoot $RepoRoot
        $services = New-Object System.Collections.Generic.List[string]
        $local = New-Object System.Collections.Generic.List[string]
        $thirdParty = New-Object System.Collections.Generic.List[string]
        foreach ($service in @(Get-ComposeServices -Files $prodLikeFiles)) {
            $image = [string](Get-AcceptanceProperty $service.Value "image")
            if ($null -ne (Get-AcceptanceProperty $service.Value "build")) {
                if ([string]::IsNullOrWhiteSpace($image)) { [void]$services.Add("hrm-acceptance-$($service.Name):latest") }
                else { [void]$local.Add($image) }
            }
            elseif ($image) { [void]$thirdParty.Add($image) }
        }
        foreach ($service in @(Get-ComposeServices -Files $baseFiles -Project "hrmanagement")) {
            $image = [string](Get-AcceptanceProperty $service.Value "image")
            if ($image -and $null -eq (Get-AcceptanceProperty $service.Value "build")) { [void]$thirdParty.Add($image) }
        }
        $pins = Read-AcceptanceJson -Path (Join-Path (Join-Path (Join-Path $RepoRoot "docs") "qa") "tool-pins.json")
        $tools = @($pins.tools.PSObject.Properties | ForEach-Object { [string]$_.Value.ref })
        $wanted = @(@($thirdParty) + $tools | Sort-Object -Unique)
        foreach ($image in $wanted) { Invoke-WithRetry -What "docker pull $image" -Body { docker pull -q $image } }

        $manifest = [ordered]@{
            sourceSha = $revision
            services = @(@($services) + $migrateImage | Sort-Object -Unique)
            local = @($local | Sort-Object -Unique)
            thirdParty = @($thirdParty | Sort-Object -Unique)
            tools = $tools
        }
        $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
        [System.IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 4) + "`n"), $utf8NoBom)
        Write-Host "acceptance-images: built $($manifest.services.Count) service images, $($wanted.Count) others pulled."
    }
    "Save" {
        $manifest = Read-ImageManifest
        $images = @(@($manifest.services) + @($manifest.local) + @($manifest.thirdParty) + @($manifest.tools))
        bash -c "set -o pipefail; docker save $($images -join ' ') | zstd -T0 -3 -q -f -o '$($archive -replace '\\', '/')'"
        if ($LASTEXITCODE -ne 0) { throw "docker save failed with exit code $LASTEXITCODE" }
        Write-Host ("acceptance-images: {0} images, {1:N0} MB" -f $images.Count, ((Get-Item -LiteralPath $archive).Length / 1MB))
    }
    "Load" {
        bash -c "set -o pipefail; zstd -dc '$($archive -replace '\\', '/')' | docker load -q"
        if ($LASTEXITCODE -ne 0) { throw "docker load failed with exit code $LASTEXITCODE" }
    }
    "Verify" {
        if ([string]::IsNullOrWhiteSpace($SourceSha)) { throw "-Action Verify needs -SourceSha." }
        $manifest = Read-ImageManifest
        $fixture = $null
        if (-not [string]::IsNullOrWhiteSpace($InspectFile)) { $fixture = Read-AcceptanceJson -Path $InspectFile }
        $problems = New-Object System.Collections.Generic.List[string]
        foreach ($image in @($manifest.services)) {
            $revision = Get-ImageRevision -Image ([string]$image) -Fixture $fixture
            if ($null -eq $revision) { [void]$problems.Add("$image is missing.") }
            elseif ($revision -ne $SourceSha) { [void]$problems.Add("$image carries revision '$revision', not $SourceSha.") }
        }
        foreach ($image in @(@($manifest.local) + @($manifest.thirdParty))) {
            if ($null -eq (Get-ImageRevision -Image ([string]$image) -Fixture $fixture)) { [void]$problems.Add("$image is missing.") }
        }
        # docker load does not restore a reference by digest on every image store: the meta stage would fetch these two small tools.
        foreach ($image in @($manifest.tools)) {
            if ($null -eq (Get-ImageRevision -Image ([string]$image) -Fixture $fixture)) { Write-Host "::warning::$image is not available by digest after docker load." }
        }
        if ($problems.Count -gt 0) {
            Write-Host "acceptance-images: FAIL ($($problems.Count))"
            foreach ($problem in $problems) { Write-Host "  - $problem" }
            exit 1
        }
        Write-Host "acceptance-images: PASS ($(@($manifest.services).Count) service images of $SourceSha)"
    }
}
exit 0
