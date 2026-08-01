# NAT-001: validate control evidence pack artifacts.
# Usage:
#   powershell -File scripts/nat001-validate.ps1

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
    "docs/NAT_001_EVIDENCE_PACK.md",
    "docs/nat/control-evidence-index.json",
    "scripts/nat001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat001EvidenceTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$indexFile = Resolve-RepoPath "docs/nat/control-evidence-index.json"
if (Test-Path $indexFile) {
    $json = Get-Content -Raw -Path $indexFile | ConvertFrom-Json
    if ($json.card -ne "NAT-001") { $failures += "control-evidence-index.json card must be NAT-001" }
    if ($json.sections.Count -lt 8) { $failures += "index must have at least 8 evidence sections" }
    if (-not $json.evidenceRequirements.immutability) { $failures += "index must require immutability" }
    if (-not $json.evidenceRequirements.piiBlocked) { $failures += "index must block PII in evidence" }
    if ($json.evidenceRequirements.reproducibilityThreshold -lt 0.10) { $failures += "index must require at least 10% reproducibility" }
    if (-not $json.authorizationGate.independentAssessor) { $failures += "index must require independent assessor" }
    if (-not $json.authorizationGate.zeroCriticalGaps) { $failures += "index must require zero critical gaps" }
    if ($json.lifecycle.Count -lt 4) { $failures += "index must have at least 4 lifecycle phases" }
}

$docFile = Resolve-RepoPath "docs/NAT_001_EVIDENCE_PACK.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-001") { $failures += "doc missing NAT-001 reference" }
    if ($doc -notmatch "immutab|Immutab") { $failures += "doc must address immutability" }
    if ($doc -notmatch "reproducib|Reproducib") { $failures += "doc must address reproducibility" }
    if ($doc -notmatch "authorization|Authorization") { $failures += "doc must address authorization gate" }
    if ($doc -notmatch "evidence|Evidence") { $failures += "doc must address evidence" }
}

$outDir = Resolve-RepoPath "testdata/nat001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-001"
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

Write-Host "PASS: NAT-001 evidence pack validate"
exit 0
