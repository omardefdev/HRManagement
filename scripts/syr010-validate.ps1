# SYR-010: validate government data bus profile artifacts.
# Usage:
#   powershell -File scripts/syr010-validate.ps1

param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()

$requiredFiles = @(
    "docs/SYR_010_DATA_BUS.md",
    "docs/syr/data-bus-profile.json",
    "scripts/syr010-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr010DataBusTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$profileFile = Resolve-RepoPath "docs/syr/data-bus-profile.json"
if (Test-Path $profileFile) {
    $profile = Get-Content -Raw -Path $profileFile | ConvertFrom-Json
    if ($profile.card -ne "SYR-010") { $failures += "profile card must be SYR-010" }
    if ($profile.liveBusClaimForbidden -ne $true) { $failures += "liveBusClaimForbidden must be true" }
    if ($profile.shadowIntegrationForbidden -ne $true) { $failures += "shadowIntegrationForbidden must be true" }
    if (-not $profile.connectors -or $profile.connectors.Count -lt 2) {
        $failures += "profile must define at least 2 connectors"
    }
    if (-not $profile.transitionalBatchPolicy) {
        $failures += "transitionalBatchPolicy required"
    } else {
        if (-not $profile.transitionalBatchPolicy.manifestRequired) { $failures += "manifestRequired must be true" }
        if (-not $profile.transitionalBatchPolicy.replaySafe) { $failures += "replaySafe must be true" }
    }
    foreach ($conn in $profile.connectors) {
        if (-not $conn.connectorId) { $failures += "connector missing connectorId" }
        if (-not $conn.authority) { $failures += "connector $($conn.connectorId) missing authority" }
        if (-not $conn.classification) { $failures += "connector $($conn.connectorId) missing classification" }
        if (-not $conn.purpose) { $failures += "connector $($conn.connectorId) missing purpose" }
        if (-not $conn.contractVersion) { $failures += "connector $($conn.connectorId) missing contractVersion" }
        if (-not $conn.slo) { $failures += "connector $($conn.connectorId) missing slo" }
    }
}

$docFile = Resolve-RepoPath "docs/SYR_010_DATA_BUS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-010") { $failures += "doc missing SYR-010 reference" }
    if ($doc -notmatch "mTLS|batch|transitional") { $failures += "doc must describe transitional integration" }
    if ($doc -notmatch "reconciliation|replay") { $failures += "doc must mention reconciliation/replay" }
}

$outDir = Resolve-RepoPath "testdata/syr010/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-010"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: SYR-010 government data bus validate"
exit 0
