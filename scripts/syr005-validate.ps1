# SYR-005: validate wage/compensation rule pack artifacts.
# Usage:
#   powershell -File scripts/syr005-validate.ps1

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
    "docs/SYR_005_WAGE_PACKAGES.md",
    "docs/syr/decree-67-68-wage-tables.json",
    "scripts/syr005-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr005WageTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$packFile = Resolve-RepoPath "docs/syr/decree-67-68-wage-tables.json"
if (Test-Path $packFile) {
    $pack = Get-Content -Raw -Path $packFile | ConvertFrom-Json
    if ($pack.card -ne "SYR-005") { $failures += "pack card must be SYR-005" }

    $sources = @($pack.legalSources | ForEach-Object { $_.legalSourceId })
    if ("SYR-DEC-67-2026" -notin $sources) { $failures += "pack must reference SYR-DEC-67-2026" }
    if ("SYR-DEC-68-2026" -notin $sources) { $failures += "pack must reference SYR-DEC-68-2026" }

    if (-not $pack.wageTables -or $pack.wageTables.Count -lt 1) {
        $failures += "pack must define wage tables"
    }

    if (-not $pack.separationPolicy) {
        $failures += "pack must define base/compensation/taxable/insurable separation"
    } elseif ($pack.separationPolicy.forbidDoubleStacking67And68 -ne $true) {
        $failures += "pack must forbid double stacking 67/68"
    }

    if (-not $pack.taxBrackets -or $pack.taxBrackets.Count -lt 1) {
        $failures += "pack must define tax brackets"
    }

    if (-not $pack.proposedTaxReform -or $pack.proposedTaxReform.forbidApplicationBeforeEffectiveDate -ne $true) {
        $failures += "pack must forbid proposed tax reform before enactment"
    }

    if (-not $pack.roundingRules -or $pack.roundingRules.forbidFloatArithmetic -ne $true) {
        $failures += "pack must forbid float arithmetic"
    }

    if (-not $pack.governance -or $pack.governance.fourEyesRequired -ne $true) {
        $failures += "pack must require four-eyes governance"
    }

    if ($pack.shadowCyclesRequired -lt 3) {
        $failures += "pack must require at least 3 shadow cycles"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_005_WAGE_PACKAGES.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-005") { $failures += "doc missing SYR-005 reference" }
    if ($doc -notmatch "67/2026") { $failures += "doc must mention Decree 67/2026" }
    if ($doc -notmatch "68/2026") { $failures += "doc must mention Decree 68/2026" }
}

$outDir = Resolve-RepoPath "testdata/syr005/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-005"
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

Write-Host "PASS: SYR-005 wage packages validate"
exit 0
