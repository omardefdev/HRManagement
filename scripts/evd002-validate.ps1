# EVD-002: validate forensic readiness artifacts.
# Usage:
#   powershell -File scripts/evd002-validate.ps1

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
    "docs/EVD_002_FORENSIC_READINESS.md",
    "docs/evd/custody-chain-model.json",
    "scripts/evd002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Evd002ForensicTests.cs",
    "src/Shared/HRManagement.SharedKernel/Evidence/ForensicCase.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate custody chain model
$modelFile = Resolve-RepoPath "docs/evd/custody-chain-model.json"
if (Test-Path $modelFile) {
    $model = Get-Content -Raw -Path $modelFile | ConvertFrom-Json
    if ($model.card -ne "EVD-002") { $failures += "custody model card must be EVD-002" }
    if (-not $model.custodyChainSchema) { $failures += "model must have custodyChainSchema" }
    if (-not $model.custodyChainSchema.evidenceItem) { $failures += "model must define evidenceItem" }
    if (-not $model.custodyChainSchema.custodyTransfer) { $failures += "model must define custodyTransfer" }
    if (-not $model.severityLevels -or $model.severityLevels.Count -lt 4) {
        $failures += "model must define at least 4 severity levels"
    }
    if ($model.retentionPolicy.legalHoldOverridesRetention -ne $true) {
        $failures += "legal hold must override retention"
    }
}

# Validate doc content
$docFile = Resolve-RepoPath "docs/EVD_002_FORENSIC_READINESS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "EVD-002") { $failures += "doc missing EVD-002 reference" }
    if ($doc -notmatch "custody") { $failures += "doc must mention custody chain" }
    if ($doc -notmatch "legal hold|LegalHold") { $failures += "doc must mention legal hold" }
    if ($doc -notmatch "ForensicCase") { $failures += "doc must mention ForensicCase" }
}

# Validate ForensicCase entity
$caseFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Evidence/ForensicCase.cs"
if (Test-Path $caseFile) {
    $case = Get-Content -Raw -Path $caseFile
    if ($case -notmatch "CaseNumber") { $failures += "ForensicCase must have CaseNumber" }
    if ($case -notmatch "LegalHoldActive") { $failures += "ForensicCase must have LegalHoldActive" }
    if ($case -notmatch "LeadInvestigator") { $failures += "ForensicCase must have LeadInvestigator" }
    if ($case -notmatch "ForensicCaseSeverity") { $failures += "ForensicCase must have severity enum" }
}

$outDir = Resolve-RepoPath "testdata/evd002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "EVD-002"
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

Write-Host "PASS: EVD-002 forensic readiness validate"
exit 0
