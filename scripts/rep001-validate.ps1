# REP-001: validate statutory report catalog artifacts.
# Usage:
#   powershell -File scripts/rep001-validate.ps1

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
    "docs/REP_001_REPORTS.md",
    "docs/rep/statutory-catalog.json",
    "scripts/rep001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Rep001ReportsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$catalogFile = Resolve-RepoPath "docs/rep/statutory-catalog.json"
if (Test-Path $catalogFile) {
    $cat = Get-Content -Raw -Path $catalogFile | ConvertFrom-Json
    if ($cat.card -ne "REP-001") { $failures += "catalog card must be REP-001" }
    if ($cat.crossTenantForbidden -ne $true) { $failures += "crossTenantForbidden must be true" }
    if (-not $cat.reports -or $cat.reports.Count -lt 3) {
        $failures += "catalog must define at least 3 reports"
    }
    foreach ($rep in $cat.reports) {
        if (-not $rep.reportId) { $failures += "report missing reportId" }
        if (-not $rep.legalAuthority) { $failures += "report $($rep.reportId) missing legalAuthority" }
        if (-not $rep.goldenDataset) { $failures += "report $($rep.reportId) missing goldenDataset" }
        if (-not $rep.version) { $failures += "report $($rep.reportId) missing version" }
        if (-not $rep.effectiveDate) { $failures += "report $($rep.reportId) missing effectiveDate" }
        if ($rep.asOfRequired -ne $true) { $failures += "report $($rep.reportId) must require asOf" }
    }
    if (-not $cat.metadataPolicy.embedVersion) { $failures += "metadataPolicy must embed version" }
}

$docFile = Resolve-RepoPath "docs/REP_001_REPORTS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "REP-001") { $failures += "doc missing REP-001 reference" }
    if ($doc -notmatch "statutory|golden") { $failures += "doc must describe statutory/golden datasets" }
    if ($doc -notmatch "as-of|as of") { $failures += "doc must mention as-of semantics" }
}

$outDir = Resolve-RepoPath "testdata/rep001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "REP-001"
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

Write-Host "PASS: REP-001 statutory reports validate"
exit 0
