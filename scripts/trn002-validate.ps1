# TRN-002: validate unions/worker representation artifacts.
# Usage:
#   powershell -File scripts/trn002-validate.ps1

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
    "docs/TRN_002_UNIONS.md",
    "docs/trn/worker-representation.json",
    "scripts/trn002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Trn002UnionsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$modelFile = Resolve-RepoPath "docs/trn/worker-representation.json"
if (Test-Path $modelFile) {
    $json = Get-Content -Raw -Path $modelFile | ConvertFrom-Json
    if ($json.card -ne "TRN-002") { $failures += "worker-representation.json card must be TRN-002" }
    if ($json.representationBodies.Count -lt 2) { $failures += "worker-representation.json must have at least 2 representation bodies" }
    if ($json.engagementClassification.Count -lt 5) { $failures += "worker-representation.json must have at least 5 engagement topics" }
    if ($json.engagementProtocol.Count -lt 4) { $failures += "worker-representation.json must have at least 4 protocol steps" }
    if (-not $json.monitoringBoundaries.scopeDisclosed) { $failures += "worker-representation.json must require monitoring scope disclosure" }
    if (-not $json.waveReadiness.noWaveWithoutEngagementEvidence) { $failures += "worker-representation.json must block wave without engagement" }
}

$docFile = Resolve-RepoPath "docs/TRN_002_UNIONS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "TRN-002") { $failures += "doc missing TRN-002 reference" }
    if ($doc -notmatch "union|Union|representation|Representation") { $failures += "doc must address unions/representation" }
    if ($doc -notmatch "negotiat|Negotiat") { $failures += "doc must address negotiation" }
    if ($doc -notmatch "consult|Consult") { $failures += "doc must address consultation" }
    if ($doc -notmatch "monitor|Monitor") { $failures += "doc must address monitoring boundaries" }
}

$outDir = Resolve-RepoPath "testdata/trn002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "TRN-002"
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

Write-Host "PASS: TRN-002 unions validate"
exit 0
