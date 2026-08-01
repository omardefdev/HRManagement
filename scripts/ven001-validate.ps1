# VEN-001: validate vendor exit drill checklist artifacts.
# Usage:
#   powershell -File scripts/ven001-validate.ps1

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
    "docs/VEN_001_VENDOR_EXIT.md",
    "docs/ven/exit-drill-checklist.json",
    "scripts/ven001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Ven001ExitTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/ven/exit-drill-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "VEN-001") { $failures += "checklist card must be VEN-001" }
    if (-not $check.exitRtoHours -or $check.exitRtoHours -le 0) {
        $failures += "exitRtoHours must be positive"
    }
    if (-not $check.phases -or $check.phases.Count -lt 3) {
        $failures += "must define at least 3 exit phases"
    }
    $hasRevoke = $false
    $hasRebuild = $false
    $hasAttest = $false
    foreach ($phase in $check.phases) {
        foreach ($step in $phase.steps) {
            if ($step.stepId -match "revoke") { $hasRevoke = $true }
            if ($step.stepId -match "rebuild|restore") { $hasRebuild = $true }
            if ($step.stepId -match "attest|deletion") { $hasAttest = $true }
        }
    }
    if (-not $hasRevoke) { $failures += "must include vendor access revocation step" }
    if (-not $hasRebuild) { $failures += "must include rebuild/restore step" }
    if (-not $hasAttest) { $failures += "must include deletion attestation step" }
    if (-not $check.exportImportPolicy.reconciliationPercent -or $check.exportImportPolicy.reconciliationPercent -ne 100) {
        $failures += "export/import reconciliation must be 100%"
    }
}

$docFile = Resolve-RepoPath "docs/VEN_001_VENDOR_EXIT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "VEN-001") { $failures += "doc missing VEN-001 reference" }
    if ($doc -notmatch "exit|vendor") { $failures += "doc must describe vendor exit" }
    if ($doc -notmatch "RTO|rebuild|attest") { $failures += "doc must mention RTO/rebuild/attestation" }
}

$outDir = Resolve-RepoPath "testdata/ven001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "VEN-001"
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

Write-Host "PASS: VEN-001 vendor exit drill validate"
exit 0
