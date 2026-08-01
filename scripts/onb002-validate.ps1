# ONB-002: validate agency accreditation wave artifacts.
# Usage:
#   powershell -File scripts/onb002-validate.ps1

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
    "docs/ONB_002_ACCREDITATION.md",
    "docs/onb/wave-readiness-checklist.json",
    "scripts/onb002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Onb002AccreditationTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checklistFile = Resolve-RepoPath "docs/onb/wave-readiness-checklist.json"
if (Test-Path $checklistFile) {
    $json = Get-Content -Raw -Path $checklistFile | ConvertFrom-Json
    if ($json.card -ne "ONB-002") { $failures += "wave-readiness-checklist.json card must be ONB-002" }
    if ($json.waveProgression.Count -lt 3) { $failures += "checklist must have at least 3 wave phases" }
    if ($json.readinessDimensions.Count -lt 6) { $failures += "checklist must have at least 6 readiness dimensions" }
    if (-not $json.goNoGoDecision.allMandatoryRequired) { $failures += "checklist must require all mandatory dimensions" }
    if (-not $json.goNoGoDecision.blastRadiusDefined) { $failures += "checklist must define blast radius" }
    if ($json.goNoGoDecision.monitoringPeriodDays -lt 30) { $failures += "checklist must require at least 30-day monitoring" }
}

$docFile = Resolve-RepoPath "docs/ONB_002_ACCREDITATION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "ONB-002") { $failures += "doc missing ONB-002 reference" }
    if ($doc -notmatch "wave|Wave") { $failures += "doc must address waves" }
    if ($doc -notmatch "readiness|Readiness") { $failures += "doc must address readiness" }
    if ($doc -notmatch "Go.No.Go|go.no.go") { $failures += "doc must address Go/No-Go decision" }
    if ($doc -notmatch "rollback|Rollback") { $failures += "doc must address rollback" }
}

$outDir = Resolve-RepoPath "testdata/onb002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "ONB-002"
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

Write-Host "PASS: ONB-002 accreditation validate"
exit 0
