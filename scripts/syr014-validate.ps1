# SYR-014: validate Syrian calendar and holiday policy artifacts.
# Usage:
#   powershell -File scripts/syr014-validate.ps1

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
    "docs/SYR_014_CALENDAR.md",
    "docs/syr/holiday-calendar-policy.json",
    "scripts/syr014-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr014CalendarTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$policyFile = Resolve-RepoPath "docs/syr/holiday-calendar-policy.json"
if (Test-Path $policyFile) {
    $policy = Get-Content -Raw -Path $policyFile | ConvertFrom-Json
    if ($policy.card -ne "SYR-014") { $failures += "policy card must be SYR-014" }

    if ($policy.timezone -ne "Asia/Damascus") {
        $failures += "policy timezone must be Asia/Damascus"
    }

    if ($policy.forbidHardcodedHolidayList -ne $true) {
        $failures += "policy must forbid hard-coded holiday lists"
    }

    if ($policy.legalSourceId -ne "SYR-DEC-188-2025") {
        $failures += "policy must reference Decree 188/2025"
    }

    if (-not $policy.fixedHolidays -or $policy.fixedHolidays.Count -lt 5) {
        $failures += "policy must define at least 5 fixed holidays"
    }

    if (-not $policy.continuousOperations -or $policy.continuousOperations.forbidFullClosureFor24x7 -ne $true) {
        $failures += "policy must forbid full closure for 24/7 agencies"
    }

    if (-not $policy.payrollImpact -or $policy.payrollImpact.recalculateBeforePayrollApproval -ne $true) {
        $failures += "policy must require payroll recalculation before approval"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_014_CALENDAR.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-014") { $failures += "doc missing SYR-014 reference" }
    if ($doc -notmatch "188/2025") { $failures += "doc must mention Decree 188/2025" }
}

$outDir = Resolve-RepoPath "testdata/syr014/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-014"
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

Write-Host "PASS: SYR-014 Syrian calendar validate"
exit 0
