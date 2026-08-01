# FRD-001: validate fraud prevention artifacts.
# Usage:
#   powershell -File scripts/frd001-validate.ps1

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
    "docs/FRD_001_FRAUD.md",
    "docs/frd/anomaly-rules.json",
    "scripts/frd001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Frd001FraudTests.cs",
    "src/Services/Payroll/Payroll.Application/Services/FraudAnomalyEngine.cs",
    "src/Services/Payroll/Payroll.Application/Services/PaymentHoldService.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/FraudAlert.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/PaymentHold.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/ProofOfLifeRecord.cs",
    "src/Services/Payroll/Payroll.Infrastructure/Migrations/20260731182000_FRD001_FraudPrevention.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/FraudAlertsController.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/PaymentHoldsController.cs",
    "src/Tests/HRManagement.UnitTests/Frd001FraudEngineTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$rulesFile = Resolve-RepoPath "docs/frd/anomaly-rules.json"
if (Test-Path $rulesFile) {
    $json = Get-Content -Raw -Path $rulesFile | ConvertFrom-Json
    if ($json.card -ne "FRD-001") { $failures += "anomaly-rules.json card must be FRD-001" }
    if ($json.detectionRules.Count -lt 5) { $failures += "anomaly-rules.json must have at least 5 detection rules" }
    if (-not $json.proofOfLife.pensioners) { $failures += "anomaly-rules.json must define proof-of-life for pensioners" }
    if (-not $json.paymentHold.ownerRequired) { $failures += "anomaly-rules.json must require hold owner" }
    if (-not $json.paymentHold.singleOperatorReleaseBlocked) { $failures += "anomaly-rules.json must block single operator release" }
    if ($json.verification.consecutiveCyclesWithAudit -lt 3) { $failures += "anomaly-rules.json must require at least 3 consecutive audited cycles" }
}

$docFile = Resolve-RepoPath "docs/FRD_001_FRAUD.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "FRD-001") { $failures += "doc missing FRD-001 reference" }
    if ($doc -notmatch "ghost|Ghost") { $failures += "doc must address ghost employees" }
    if ($doc -notmatch "proof.of.life|Proof.of.Life") { $failures += "doc must address proof-of-life" }
    if ($doc -notmatch "four.eyes") { $failures += "doc must address four-eyes approval" }
    if ($doc -notmatch "anomal|Anomal") { $failures += "doc must address anomaly detection" }
}

$outDir = Resolve-RepoPath "testdata/frd001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "FRD-001"
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

Write-Host "PASS: FRD-001 fraud prevention validate"
exit 0
