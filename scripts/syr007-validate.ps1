# SYR-007: validate Law 12/2024 data protection artifacts.
# Usage:
#   powershell -File scripts/syr007-validate.ps1

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
    "docs/SYR_007_DATA_PROTECTION.md",
    "docs/syr/law12-2024-compliance-matrix.json",
    "scripts/syr007-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr007DataProtectionTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify compliance matrix structure
$matrixFile = Resolve-RepoPath "docs/syr/law12-2024-compliance-matrix.json"
if (Test-Path $matrixFile) {
    $json = Get-Content -Raw -Path $matrixFile | ConvertFrom-Json
    if ($json.card -ne "SYR-007") { $failures += "compliance-matrix.json card must be SYR-007" }
    if (-not $json.law.title) { $failures += "compliance-matrix.json must have law.title" }
    if (-not $json.roles.controller) { $failures += "compliance-matrix.json must define controller role" }
    if (-not $json.roles.processor) { $failures += "compliance-matrix.json must define processor role" }
    if (-not $json.roles.dpo) { $failures += "compliance-matrix.json must define DPO" }
    if ($json.legalBases.Count -lt 2) { $failures += "compliance-matrix.json must have at least 2 legal bases" }
    if ($json.dataSubjectRights.Count -lt 5) { $failures += "compliance-matrix.json must have at least 5 data subject rights" }
    if ($json.complianceControls.Count -lt 10) { $failures += "compliance-matrix.json must have at least 10 compliance controls" }
    if (-not $json.breachResponse.notifyAuthority) { $failures += "compliance-matrix.json must define breach notification deadline" }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/SYR_007_DATA_PROTECTION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-007") { $failures += "doc missing SYR-007 reference" }
    if ($doc -notmatch "12/2024|12.2024|Law 12") { $failures += "doc must reference Law 12/2024" }
    if ($doc -notmatch "ROPA") { $failures += "doc must address ROPA" }
    if ($doc -notmatch "DPO") { $failures += "doc must address DPO" }
    if ($doc -notmatch "breach|Breach") { $failures += "doc must address breach notification" }
    if ($doc -notmatch "consent|Consent") { $failures += "doc must address consent rules" }
}

$outDir = Resolve-RepoPath "testdata/syr007/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-007"
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

Write-Host "PASS: SYR-007 data protection validate"
exit 0
