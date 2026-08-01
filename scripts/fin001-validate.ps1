# FIN-001: validate 5-year TCO artifacts.
# Usage:
#   powershell -File scripts/fin001-validate.ps1

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
    "docs/FIN_001_TCO.md",
    "docs/fin/tco-model.json",
    "scripts/fin001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Fin001TcoTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$tcoFile = Resolve-RepoPath "docs/fin/tco-model.json"
if (Test-Path $tcoFile) {
    $tco = Get-Content -Raw -Path $tcoFile | ConvertFrom-Json
    if ($tco.card -ne "FIN-001") { $failures += "TCO model card must be FIN-001" }

    if (-not $tco.years -or $tco.years.Count -ne 5) {
        $failures += "TCO model must cover exactly 5 years"
    }

    if (-not $tco.costCategories -or $tco.costCategories.Count -lt 5) {
        $failures += "TCO model must define at least 5 cost categories"
    }

    if (-not $tco.unitMetrics -or $tco.unitMetrics.Count -lt 3) {
        $failures += "TCO model must define at least 3 unit metrics"
    }

    if (-not $tco.sensitivityAxes -or $tco.sensitivityAxes.Count -lt 2) {
        $failures += "TCO model must define sensitivity analysis axes"
    }

    if (-not $tco.alternatives -or $tco.alternatives.Count -lt 2) {
        $failures += "TCO model must compare at least 2 alternatives"
    }

    $hasContingency = $tco.costCategories | Where-Object { $_.id -eq "contingency" }
    if (-not $hasContingency) {
        $failures += "TCO model must include contingency reserve"
    }
}

$docFile = Resolve-RepoPath "docs/FIN_001_TCO.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "FIN-001") { $failures += "doc missing FIN-001 reference" }
    if ($doc -notmatch "TCO|Total Cost") { $failures += "doc must describe TCO" }
    if ($doc -notmatch "sensitivity|Sensitivity") { $failures += "doc must include sensitivity analysis" }
}

$outDir = Resolve-RepoPath "testdata/fin001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "FIN-001"
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

Write-Host "PASS: FIN-001 TCO validate"
exit 0
