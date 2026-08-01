# PAY-004: validate arrears / partial disbursement artifacts.
# Usage:
#   powershell -File scripts/pay004-validate.ps1

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
    "docs/PAY_004_ARREARS.md",
    "src/Services/Payroll/Payroll.Domain/Entities/ArrearsLedgerEntry.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/FundingDecision.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/DisbursementPriorityRule.cs",
    "src/Services/Payroll/Payroll.Domain/Entities/PaymentBatch.cs",
    "src/Services/Payroll/Payroll.Application/Services/PartialDisbursementService.cs",
    "src/Services/Payroll/Payroll.Application/Services/ArrearsSettlementService.cs",
    "src/Services/Payroll/Payroll.Web/Controllers/ArrearsController.cs",
    "src/Services/Payroll/Payroll.Infrastructure/Migrations/20260731183000_PAY004_ArrearsAndPartialDisbursement.cs",
    "scripts/pay004-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Pay004ArrearsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$arrearsFile = Resolve-RepoPath "src/Services/Payroll/Payroll.Domain/Entities/ArrearsLedgerEntry.cs"
if (Test-Path $arrearsFile) {
    $cs = Get-Content -Raw -Path $arrearsFile
    if ($cs -notmatch "ArrearsLedgerEntry") { $failures += "ArrearsLedgerEntry.cs must define ArrearsLedgerEntry" }
    if ($cs -notmatch "EmployeeId") { $failures += "ArrearsLedgerEntry.cs must have EmployeeId" }
    if ($cs -notmatch "OriginalAmount") { $failures += "ArrearsLedgerEntry.cs must have OriginalAmount" }
    if ($cs -notmatch "PaidAmount") { $failures += "ArrearsLedgerEntry.cs must have PaidAmount" }
    if ($cs -notmatch "ArrearsAmount") { $failures += "ArrearsLedgerEntry.cs must have ArrearsAmount" }
    if ($cs -notmatch "ApplyPayment") { $failures += "ArrearsLedgerEntry.cs must define ApplyPayment" }
    if ($cs -notmatch "CurrencyEra") { $failures += "ArrearsLedgerEntry.cs must have CurrencyEra" }
    if ($cs -notmatch "TryManualUpdate") { $failures += "ArrearsLedgerEntry.cs must define TryManualUpdate (four-eyes)" }
}

$batchFile = Resolve-RepoPath "src/Services/Payroll/Payroll.Domain/Entities/PaymentBatch.cs"
if (Test-Path $batchFile) {
    $batch = Get-Content -Raw -Path $batchFile
    if ($batch -notmatch "FundingDecisionId") { $failures += "PaymentBatch must have FundingDecisionId" }
    if ($batch -notmatch "IsPartial") { $failures += "PaymentBatch must have IsPartial" }
    if ($batch -notmatch "DisbursedAmount") { $failures += "PaymentBatch must have DisbursedAmount" }
}

$svcFile = Resolve-RepoPath "src/Services/Payroll/Payroll.Application/Services/PartialDisbursementService.cs"
if (Test-Path $svcFile) {
    $svc = Get-Content -Raw -Path $svcFile
    if ($svc -notmatch "Allocate") { $failures += "PartialDisbursementService must expose Allocate" }
}

$settleFile = Resolve-RepoPath "src/Services/Payroll/Payroll.Application/Services/ArrearsSettlementService.cs"
if (Test-Path $settleFile) {
    $settle = Get-Content -Raw -Path $settleFile
    if ($settle -notmatch "Settle") { $failures += "ArrearsSettlementService must expose Settle" }
    if ($settle -notmatch "GetBalance") { $failures += "ArrearsSettlementService must expose GetBalance" }
}

$docFile = Resolve-RepoPath "docs/PAY_004_ARREARS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "PAY-004") { $failures += "doc missing PAY-004 reference" }
    if ($doc -notmatch "arrears|Arrears") { $failures += "doc must address arrears" }
    if ($doc -notmatch "partial|Partial") { $failures += "doc must address partial disbursement" }
    if ($doc -notmatch "four.eyes") { $failures += "doc must address four-eyes approval" }
    if ($doc -notmatch "priority|Priority") { $failures += "doc must address disbursement priority" }
}

$outDir = Resolve-RepoPath "testdata/pay004/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "PAY-004"
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

Write-Host "PASS: PAY-004 arrears/liquidity validate"
exit 0
