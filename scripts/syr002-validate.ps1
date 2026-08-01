# SYR-002: validate census register artifacts.
# Usage:
#   powershell -File scripts/syr002-validate.ps1

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
    "docs/SYR_002_CENSUS.md",
    "docs/syr/census-register.json",
    "scripts/syr002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr002CensusTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$censusFile = Resolve-RepoPath "docs/syr/census-register.json"
if (Test-Path $censusFile) {
    $census = Get-Content -Raw -Path $censusFile | ConvertFrom-Json
    if ($census.card -ne "SYR-002") { $failures += "census card must be SYR-002" }

    if (-not $census.governorates -or $census.governorates.Count -ne 14) {
        $failures += "census must define exactly 14 Syrian governorates"
    }

    if (-not $census.agencyTypes -or $census.agencyTypes.Count -lt 4) {
        $failures += "census must define at least 4 agency types"
    }

    if (-not $census.censusCategories) {
        $failures += "census must define classification categories"
    } else {
        $codes = $census.censusCategories | ForEach-Object { $_.code }
        foreach ($required in @("current", "historical", "beneficiary", "duplicate")) {
            if ($required -notin $codes) {
                $failures += "census missing category: $required"
            }
        }
    }

    if (-not $census.headroomPolicy -or $census.headroomPolicy.minimumPercent -lt 50) {
        $failures += "headroom policy must be at least 50%"
    }

    if (-not $census.sourceAuthorities -or $census.sourceAuthorities.Count -lt 3) {
        $failures += "census must define at least 3 source authorities"
    }

    if (-not $census.reconciliationRules -or $census.reconciliationRules.Count -lt 2) {
        $failures += "census must define reconciliation rules"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_002_CENSUS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-002") { $failures += "doc missing SYR-002 reference" }
    if ($doc -notmatch "14 governorates|14 محافظ") { $failures += "doc must mention 14 governorates" }
}

$outDir = Resolve-RepoPath "testdata/syr002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-002"
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

Write-Host "PASS: SYR-002 census register validate"
exit 0
