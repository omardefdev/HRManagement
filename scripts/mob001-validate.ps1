# MOB-001: validate mobile/PWA performance budget artifacts.
# Usage:
#   powershell -File scripts/mob001-validate.ps1

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
    "docs/MOB_001_MOBILE.md",
    "docs/mob/performance-budgets.json",
    "scripts/mob001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Mob001MobileTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$budgetFile = Resolve-RepoPath "docs/mob/performance-budgets.json"
if (Test-Path $budgetFile) {
    $budget = Get-Content -Raw -Path $budgetFile | ConvertFrom-Json
    if ($budget.card -ne "MOB-001") { $failures += "budget card must be MOB-001" }

    if (-not $budget.pwaDecision -or $budget.pwaDecision.strategy -ne "mobile-first-pwa") {
        $failures += "budget must define mobile-first-pwa strategy"
    }

    if (-not $budget.networkProfiles -or $budget.networkProfiles.Count -lt 2) {
        $failures += "budget must define at least 2 network profiles"
    }

    $perf = $budget.performanceBudgets
    if (-not $perf) {
        $failures += "budget must define performance budgets"
    } else {
        if ($perf.firstContentfulPaintMs -gt 3000) { $failures += "FCP budget too high" }
        if ($perf.largestContentfulPaintMs -gt 5000) { $failures += "LCP budget too high" }
    }

    if (-not $budget.retryPolicy -or $budget.retryPolicy.forbidDoubleSubmit -ne $true) {
        $failures += "budget must forbid double submit"
    }

    if (-not $budget.secureCache -or $budget.secureCache.piiInCacheForbidden -ne $true) {
        $failures += "budget must forbid PII in cache"
    }

    if (-not $budget.criticalJourneys -or $budget.criticalJourneys.Count -lt 3) {
        $failures += "budget must define at least 3 critical journeys"
    }
}

$docFile = Resolve-RepoPath "docs/MOB_001_MOBILE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "MOB-001") { $failures += "doc missing MOB-001 reference" }
    if ($doc -notmatch "PWA") { $failures += "doc must mention PWA" }
}

$outDir = Resolve-RepoPath "testdata/mob001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "MOB-001"
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

Write-Host "PASS: MOB-001 mobile/PWA validate"
exit 0
