# SYR-012: validate legacy record unification artifacts.
# Usage:
#   powershell -File scripts/syr012-validate.ps1

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
    "docs/SYR_012_LEGACY_UNIFICATION.md",
    "docs/syr/provenance-quarantine-model.json",
    "scripts/syr012-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr012LegacyTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$modelFile = Resolve-RepoPath "docs/syr/provenance-quarantine-model.json"
if (Test-Path $modelFile) {
    $json = Get-Content -Raw -Path $modelFile | ConvertFrom-Json
    if ($json.card -ne "SYR-012") { $failures += "provenance-quarantine-model.json card must be SYR-012" }
    if ($json.quarantineReasons.Count -lt 5) { $failures += "provenance-quarantine-model.json must have at least 5 quarantine reasons" }
    if (-not $json.provenanceFields.sourceSystem) { $failures += "provenance-quarantine-model.json must define sourceSystem field" }
    if (-not $json.provenanceFields.hashAtExtraction) { $failures += "provenance-quarantine-model.json must define hashAtExtraction" }
    if (-not $json.unificationRules.provenanceRequired) { $failures += "provenance-quarantine-model.json must require provenance" }
    if (-not $json.reconciliation.controlTotalInvariant) { $failures += "provenance-quarantine-model.json must define control total invariant" }
    if ($json.approvedSourceSystems.Count -lt 3) { $failures += "provenance-quarantine-model.json must have at least 3 approved source systems" }
}

$docFile = Resolve-RepoPath "docs/SYR_012_LEGACY_UNIFICATION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-012") { $failures += "doc missing SYR-012 reference" }
    if ($doc -notmatch "quarantine|Quarantine") { $failures += "doc must address quarantine" }
    if ($doc -notmatch "provenance|Provenance") { $failures += "doc must address provenance" }
    if ($doc -notmatch "hash|Hash") { $failures += "doc must address hash verification" }
    if ($doc -notmatch "four.eyes") { $failures += "doc must address four-eyes approval" }
}

$outDir = Resolve-RepoPath "testdata/syr012/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-012"
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

Write-Host "PASS: SYR-012 legacy unification validate"
exit 0
