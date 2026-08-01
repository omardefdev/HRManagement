# UAT-001: validate formal acceptance artifacts.
# Usage:
#   powershell -File scripts/uat001-validate.ps1

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
    "docs/UAT_001_ACCEPTANCE.md",
    "docs/uat/requirement-test-evidence-matrix.json",
    "scripts/uat001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Uat001AcceptanceTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$matrixFile = Resolve-RepoPath "docs/uat/requirement-test-evidence-matrix.json"
if (Test-Path $matrixFile) {
    $json = Get-Content -Raw -Path $matrixFile | ConvertFrom-Json
    if ($json.card -ne "UAT-001") { $failures += "requirement-test-evidence-matrix.json card must be UAT-001" }
    if ($json.phases.Count -lt 4) { $failures += "matrix must have at least 4 phases" }
    if ($json.acceptanceCriteria.Count -lt 5) { $failures += "matrix must have at least 5 acceptance criteria" }
    if (-not $json.signOff.perCapability) { $failures += "matrix must require per-capability sign-off" }
    if (-not $json.traceability.requirementToTestMapping) { $failures += "matrix must require requirement-to-test mapping" }
    if ($json.defectSeverity.Count -lt 3) { $failures += "matrix must have at least 3 defect severity levels" }
}

$docFile = Resolve-RepoPath "docs/UAT_001_ACCEPTANCE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "UAT-001") { $failures += "doc missing UAT-001 reference" }
    if ($doc -notmatch "sign.off|Sign.off") { $failures += "doc must address sign-off" }
    if ($doc -notmatch "traceab|Traceab") { $failures += "doc must address traceability" }
    if ($doc -notmatch "evidence|Evidence") { $failures += "doc must address evidence" }
    if ($doc -notmatch "defect|Defect") { $failures += "doc must address defect management" }
}

$outDir = Resolve-RepoPath "testdata/uat001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "UAT-001"
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

Write-Host "PASS: UAT-001 formal acceptance validate"
exit 0
