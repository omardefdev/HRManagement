# SAN-001: Windows helper for air-gapped build pinning checks + optional restore.
param(
    [ValidateSet("dry-run", "apply")]
    [string]$Mode = "dry-run",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$outDir = Join-Path $RepoRoot "testdata/san001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$reportPath = Join-Path $outDir "airgapped-build-report.json"

$globalJson = Get-Content -Raw (Join-Path $RepoRoot "global.json") | ConvertFrom-Json
if ($globalJson.sdk.rollForward -ne "disable") {
    throw "global.json rollForward must be 'disable' (SAN-001)"
}

$offlineConfig = Join-Path $RepoRoot "NuGet.offline.config"
if (-not (Test-Path $offlineConfig)) {
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local-cache" value="./.nuget-offline" />
  </packageSources>
</configuration>
"@ | Set-Content -Path $offlineConfig -Encoding utf8
}

$status = "dry-run-ok"
if ($Mode -eq "apply") {
    $cache = Join-Path $RepoRoot ".nuget-offline"
    New-Item -ItemType Directory -Force -Path $cache | Out-Null
    Push-Location $RepoRoot
    try {
        dotnet restore HRManagement.slnx --packages $cache --configfile $offlineConfig --locked-mode
        if ($LASTEXITCODE -ne 0) { $status = "fail-restore" }
        else {
            dotnet build HRManagement.slnx -c Release --no-restore
            if ($LASTEXITCODE -ne 0) { $status = "fail" } else { $status = "pass-local-cache" }
        }
    }
    finally { Pop-Location }
}

$report = [ordered]@{
    card = "SAN-001"
    mode = $Mode
    status = $status
    rollForward = "disable"
    offlineNuGetConfig = "NuGet.offline.config"
    timestampUtc = (Get-Date).ToUniversalTime().ToString("o")
}
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath status=$status"
if ($status -like "fail*") { exit 1 }
Write-Host "SAN-001 airgapped check PASS"
exit 0
