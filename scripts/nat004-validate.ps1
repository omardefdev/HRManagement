# NAT-004: validate migration/data quality certification checklist artifacts.
# Usage:
#   powershell -File scripts/nat004-validate.ps1

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
    "docs/NAT_004_MIGRATION_CERT.md",
    "docs/nat/nat004-checklist.json",
    "scripts/nat004-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat004MigrationCertTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/nat004-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-004") { $failures += "checklist card must be NAT-004" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if (-not $check.migReferences -or $check.migReferences.Count -lt 7) {
        $failures += "must reference MIG-001..007"
    }
    if ($check.dataQuality.criticalRecordGapCount -ne 0) {
        $failures += "criticalRecordGapCount must be 0"
    }
    if ($check.dataQuality.accountedPercent -ne 100) {
        $failures += "accountedPercent must be 100"
    }
    if (-not $check.cutover.rollbackTestedInHarness) {
        $failures += "rollbackTestedInHarness must be true"
    }
    if (-not $check.evidenceItems -or $check.evidenceItems.Count -lt 2) {
        $failures += "must define evidence items"
    }
}

$docFile = Resolve-RepoPath "docs/NAT_004_MIGRATION_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-004") { $failures += "doc missing NAT-004 reference" }
    if ($doc -notmatch "migration|data quality") { $failures += "doc must describe migration certification" }
    if ($doc -notmatch "rollback|100%") { $failures += "doc must mention rollback or 100% accounted" }
}

$outDir = Resolve-RepoPath "testdata/nat004/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-004"
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

Write-Host "PASS: NAT-004 migration/data quality cert validate"
exit 0
