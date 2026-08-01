# QA-005: validate technical debt baseline artifacts.
# Usage:
#   powershell -File scripts/qa005-validate.ps1

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
    "docs/QA_005_TECH_DEBT.md",
    "docs/qa/debt-baseline.json",
    "docs/qa/debt-register.json",
    "scripts/qa005-validate.ps1",
    "scripts/qa005-measure-debt.ps1",
    "src/Tests/HRManagement.UnitTests/Qa005TechDebtTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$baselineFile = Resolve-RepoPath "docs/qa/debt-baseline.json"
if (Test-Path $baselineFile) {
    $baseline = Get-Content -Raw -Path $baselineFile | ConvertFrom-Json
    if ($baseline.card -ne "QA-005") { $failures += "baseline card must be QA-005" }

    if ($baseline.measuredInCi -ne $true) {
        $failures += "baseline must be measured in CI"
    }

    if (-not $baseline.metrics -or $baseline.metrics.Count -lt 5) {
        $failures += "baseline must define at least 5 metrics"
    }

    $metricIds = @($baseline.metrics | ForEach-Object { $_.id })
    foreach ($required in @("cyclomatic_complexity", "duplication", "circular_dependencies", "blocking_warnings")) {
        if ($required -notin $metricIds) {
            $failures += "baseline missing metric: $required"
        }
    }

    if (-not $baseline.debtBudget -or $baseline.debtBudget.maxNewDebtPointsPerPr -gt 0) {
        $failures += "baseline must set maxNewDebtPointsPerPr to 0"
    }

    if (-not $baseline.architectureFitnessTests -or $baseline.architectureFitnessTests.runInCi -ne $true) {
        $failures += "baseline must run architecture fitness tests in CI"
    }

    if (-not $baseline.sharedKernelPolicy -or $baseline.sharedKernelPolicy.forbidDuplicatedBusinessLogic -ne $true) {
        $failures += "baseline must forbid duplicated business logic"
    }
}

$docFile = Resolve-RepoPath "docs/QA_005_TECH_DEBT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "QA-005") { $failures += "doc missing QA-005 reference" }
    if ($doc -notmatch "CI") { $failures += "doc must mention CI enforcement" }
}

$outDir = Resolve-RepoPath "testdata/qa005/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "QA-005"
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

Write-Host "PASS: QA-005 tech debt baseline validate"
exit 0
