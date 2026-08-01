# SYR-006: validate insurance/pension jurisdiction map artifacts.
# Usage:
#   powershell -File scripts/syr006-validate.ps1

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
    "docs/SYR_006_INSURANCE_PENSIONS.md",
    "docs/syr/insurance-jurisdiction-map.json",
    "scripts/syr006-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr006InsuranceTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$mapFile = Resolve-RepoPath "docs/syr/insurance-jurisdiction-map.json"
if (Test-Path $mapFile) {
    $map = Get-Content -Raw -Path $mapFile | ConvertFrom-Json
    if ($map.card -ne "SYR-006") { $failures += "map card must be SYR-006" }

    if (-not $map.institutions -or $map.institutions.Count -lt 2) {
        $failures += "map must define at least 2 institutions"
    } else {
        $ids = @($map.institutions | ForEach-Object { $_.id })
        if ("GOSI" -notin $ids) { $failures += "map must include GOSI" }
        if ("GOIP" -notin $ids) { $failures += "map must include GOIP" }
    }

    if (-not $map.jurisdictionRules -or $map.jurisdictionRules.Count -lt 3) {
        $failures += "map must define jurisdiction routing rules"
    }

    $activeRule = $map.jurisdictionRules | Where-Object { $_.ruleId -eq "ACTIVE_EMPLOYEE_CONTRIBUTION" }
    if (-not $activeRule) {
        $failures += "map must define ACTIVE_EMPLOYEE_CONTRIBUTION rule"
    } elseif ($activeRule.routeTo -ne "GOSI") {
        $failures += "active employees must route to GOSI"
    }

    if (-not $map.contributionRates -or $map.contributionRates.Count -lt 1) {
        $failures += "map must define contribution rates"
    }

    if (-not $map.beneficiaryRules -or $map.beneficiaryRules.forbidDuplicateBeneficiary -ne $true) {
        $failures += "map must forbid duplicate beneficiaries"
    }

    if (-not $map.lawAmendmentPolicy -or $map.lawAmendmentPolicy.forbidManualPatch -ne $true) {
        $failures += "map must forbid manual patch on law amendment"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_006_INSURANCE_PENSIONS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-006") { $failures += "doc missing SYR-006 reference" }
    if ($doc -notmatch "GOSI") { $failures += "doc must mention GOSI" }
    if ($doc -notmatch "GOIP") { $failures += "doc must mention GOIP" }
}

$outDir = Resolve-RepoPath "testdata/syr006/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-006"
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

Write-Host "PASS: SYR-006 insurance/pensions validate"
exit 0
