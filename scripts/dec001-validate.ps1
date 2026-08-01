# DEC-001: validate legacy decommission artifacts.
# Usage:
#   powershell -File scripts/dec001-validate.ps1

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
    "docs/DEC_001_DECOMMISSION.md",
    "docs/dec/decommission-checklist.json",
    "scripts/dec001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Dec001DecommissionTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checklistFile = Resolve-RepoPath "docs/dec/decommission-checklist.json"
if (Test-Path $checklistFile) {
    $json = Get-Content -Raw -Path $checklistFile | ConvertFrom-Json
    if ($json.card -ne "DEC-001") { $failures += "decommission-checklist.json card must be DEC-001" }
    if ($json.phases.Count -lt 6) { $failures += "checklist must have at least 6 phases" }
    if (-not $json.archiveRequirements.manifestRequired) { $failures += "checklist must require archive manifest" }
    if (-not $json.shadowOperationDetection.fieldAuditRequired) { $failures += "checklist must require shadow audit" }
    if (-not $json.mediaDestruction.destructionCertificateRequired) { $failures += "checklist must require destruction certificate" }
    if (-not $json.credentialRevocation.userAccounts) { $failures += "checklist must revoke user accounts" }
}

$docFile = Resolve-RepoPath "docs/DEC_001_DECOMMISSION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "DEC-001") { $failures += "doc missing DEC-001 reference" }
    if ($doc -notmatch "decommiss|Decommiss") { $failures += "doc must address decommission" }
    if ($doc -notmatch "archive|Archive") { $failures += "doc must address archival" }
    if ($doc -notmatch "shadow|Shadow") { $failures += "doc must address shadow operations" }
    if ($doc -notmatch "destruct|Destruct") { $failures += "doc must address destruction" }
}

$outDir = Resolve-RepoPath "testdata/dec001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "DEC-001"
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

Write-Host "PASS: DEC-001 decommission validate"
exit 0
