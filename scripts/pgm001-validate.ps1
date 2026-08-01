# PGM-001: validate program timeline and critical path artifacts.
# Usage:
#   powershell -File scripts/pgm001-validate.ps1

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
    "docs/PGM_001_TIMELINE.md",
    "docs/pgm/critical-path.json",
    "scripts/pgm001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Pgm001TimelineTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$pathFile = Resolve-RepoPath "docs/pgm/critical-path.json"
if (Test-Path $pathFile) {
    $json = Get-Content -Raw -Path $pathFile | ConvertFrom-Json
    if ($json.card -ne "PGM-001") { $failures += "critical-path.json card must be PGM-001" }
    if ($json.xlDecomposition.openXlWithoutSplit -ne 0) { $failures += "openXlWithoutSplit must be 0" }
    if ($json.criticalPath.Count -lt 4) { $failures += "criticalPath must have at least 4 segments" }
    if (-not $json.baseline.linkedFin001) { $failures += "baseline must link to FIN-001" }
    if (-not $json.waveOneMinimumSlice.explicitlyDeferred) { $failures += "waveOneMinimumSlice must name deferrals" }
    if ($json.resourceModel.teams.Count -lt 4) { $failures += "resourceModel must define at least 4 teams" }
    foreach ($seg in $json.criticalPath) {
        if (-not $seg.constraintType) { $failures += "segment $($seg.id) missing constraintType" }
        if (-not $seg.owner) { $failures += "segment $($seg.id) missing owner" }
    }
}

$docFile = Resolve-RepoPath "docs/PGM_001_TIMELINE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "PGM-001") { $failures += "doc missing PGM-001 reference" }
    if ($doc -notmatch "critical path") { $failures += "doc must address critical path" }
    if ($doc -notmatch "XL") { $failures += "doc must address XL decomposition" }
    if ($doc -notmatch "wave") { $failures += "doc must address wave-one slice" }
}

$outDir = Resolve-RepoPath "testdata/pgm001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "PGM-001"
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

Write-Host "PASS: PGM-001 timeline validate"
exit 0
