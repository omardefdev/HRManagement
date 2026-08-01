# REL-007: validate national wave rollout criteria artifacts.
# Usage:
#   powershell -File scripts/rel007-validate.ps1

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
    "docs/REL_007_WAVE_ROLLOUT.md",
    "docs/rel/wave-entry-exit-criteria.json",
    "scripts/rel007-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Rel007WaveTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$criteriaFile = Resolve-RepoPath "docs/rel/wave-entry-exit-criteria.json"
if (Test-Path $criteriaFile) {
    $json = Get-Content -Raw -Path $criteriaFile | ConvertFrom-Json
    if ($json.card -ne "REL-007") { $failures += "wave-entry-exit-criteria.json card must be REL-007" }
    if ($json.waveTypes.Count -lt 3) { $failures += "waveTypes must define at least 3 types" }
    if ($json.entryCriteria.Count -lt 5) { $failures += "entryCriteria must have at least 5 items" }
    if ($json.exitCriteria.Count -lt 5) { $failures += "exitCriteria must have at least 5 items" }
    if ($json.stopCriteria.Count -lt 3) { $failures += "stopCriteria must have at least 3 items" }
    if (-not $json.hypercare.dailyReconciliation) { $failures += "hypercare must require daily reconciliation" }
    if (-not $json.rollback.rehearsalRequired) { $failures += "rollback must require rehearsal" }
    if (-not $json.capacityGate.requireHeadroomBeforeNextWave) { $failures += "capacityGate must require headroom" }
}

$docFile = Resolve-RepoPath "docs/REL_007_WAVE_ROLLOUT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "REL-007") { $failures += "doc missing REL-007 reference" }
    if ($doc -notmatch "entry") { $failures += "doc must address entry criteria" }
    if ($doc -notmatch "exit") { $failures += "doc must address exit criteria" }
    if ($doc -notmatch "hypercare") { $failures += "doc must address hypercare" }
    if ($doc -notmatch "rollback") { $failures += "doc must address rollback" }
}

$outDir = Resolve-RepoPath "testdata/rel007/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "REL-007"
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

Write-Host "PASS: REL-007 wave rollout validate"
exit 0
