# NAT-003: validate payroll/legal certification checklist artifacts.
# Usage:
#   powershell -File scripts/nat003-validate.ps1

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
    "docs/NAT_003_PAYROLL_CERT.md",
    "docs/nat/nat003-checklist.json",
    "scripts/nat003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat003PayrollCertTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/nat003-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-003") { $failures += "checklist card must be NAT-003" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if ($check.payrollCycles.consecutiveRequired -lt 3) {
        $failures += "payrollCycles.consecutiveRequired must be >= 3"
    }
    if ($check.payrollCycles.unexplainedVariancePercent -ne 0) {
        $failures += "unexplainedVariancePercent must be 0"
    }
    if (-not $check.goldenCases -or $check.goldenCases.Count -lt 5) {
        $failures += "must define at least 5 golden cases"
    }
    if (-not $check.reconciliation.bankReconciliationRequired) {
        $failures += "bankReconciliationRequired must be true"
    }
    if (-not $check.reconciliation.treasuryReconciliationRequired) {
        $failures += "treasuryReconciliationRequired must be true"
    }
    if (-not $check.legalEvidence.ruleProvenanceRequired) {
        $failures += "ruleProvenanceRequired must be true"
    }
}

$docFile = Resolve-RepoPath "docs/NAT_003_PAYROLL_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-003") { $failures += "doc missing NAT-003 reference" }
    if ($doc -notmatch "payroll|golden") { $failures += "doc must describe payroll certification" }
    if ($doc -notmatch "maker.?checker|reconcil") { $failures += "doc must mention maker/checker or reconciliation" }
}

$outDir = Resolve-RepoPath "testdata/nat003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-003"
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

Write-Host "PASS: NAT-003 payroll/legal cert validate"
exit 0
