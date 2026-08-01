# EXT-003: runtime/production treasury-payment binding validate.
# Usage:
#   powershell -File scripts/ext003-treasury-payment-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-treasury/treasury-payment-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext003/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "EXT-003") { $failures += "card must be EXT-003" }

foreach ($need in @(
    @{ Id = "PAY-003"; Status = "Satisfied" },
    @{ Id = "IAM-003"; Status = "Satisfied" },
    @{ Id = "INT-003"; Status = "Satisfied" },
    @{ Id = "EXT-004"; Status = "NotStarted" },
    @{ Id = "treasury-live"; Status = "Blocked" },
    @{ Id = "bank-sandbox-cert"; Status = "Blocked" },
    @{ Id = "postal-channel-live"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.approvedRunRequired -ne $true) { $failures += "approvedRunRequired must be true" }
foreach ($need in @("Posted", "Approved")) {
    if (@($m.runStatusesAllowed) -notcontains $need) { $failures += "runStatusesAllowed missing $need" }
}

if ($m.paymentOrder.bindingToRunId -ne $true) { $failures += "paymentOrder.bindingToRunId must be true" }
foreach ($need in @("orderId", "payrollRunId", "tenantId", "totalAmount", "lineCount", "idempotencyKey")) {
    if (@($m.paymentOrder.requiredFields) -notcontains $need) { $failures += "paymentOrder missing field $need" }
}

if ($m.signatures.labStubAllowed -ne $true) { $failures += "signatures.labStubAllowed must be true" }
if ($m.signatures.livePkiRequiresExt004 -ne $true) { $failures += "signatures.livePkiRequiresExt004 must be true" }

foreach ($need in @("ACK", "NACK")) {
    if (@($m.channel.ackCodes) -notcontains $need) { $failures += "channel.ackCodes missing $need" }
}
foreach ($need in @("REJECT", "RETURN", "PARTIAL_REJECT")) {
    if (@($m.channel.returnRejectCodes) -notcontains $need) { $failures += "channel.returnRejectCodes missing $need" }
}

if ($m.bankAccountVerification.requiredBeforeSettle -ne $true) {
    $failures += "bankAccountVerification.requiredBeforeSettle must be true"
}
foreach ($need in @("Submitted", "Acked", "Settled", "Rejected", "Returned")) {
    if (@($m.settlement.states) -notcontains $need) { $failures += "settlement.states missing $need" }
}

if ($m.reconciliation.totalsMustMatch -ne $true) { $failures += "reconciliation.totalsMustMatch must be true" }
if ($m.reconciliation.lineCountMustMatch -ne $true) { $failures += "reconciliation.lineCountMustMatch must be true" }

if ($m.exactlyOnce.idempotencyKeyRequired -ne $true) { $failures += "exactlyOnce.idempotencyKeyRequired must be true" }
if ([string]$m.exactlyOnce.duplicatePolicy -ne "reject") { $failures += "exactlyOnce.duplicatePolicy must be reject" }

if ($m.rejectCorrectionWorkflow.required -ne $true) { $failures += "rejectCorrectionWorkflow.required must be true" }
if ($m.cutoffCalendar.cutoffRequired -ne $true) { $failures += "cutoffCalendar.cutoffRequired must be true" }

if ($m.manualFiles.unauditedForbidden -ne $true) { $failures += "manualFiles.unauditedForbidden must be true" }
foreach ($need in @("fileId", "actor", "tenantId", "payrollRunId", "approvedAt")) {
    if (@($m.manualFiles.auditRequiredFields) -notcontains $need) {
        $failures += "manualFiles.auditRequiredFields missing $need"
    }
}

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("orderId", "payrollRunId", "actor", "tenantId", "outcome", "submittedAt")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "mockSandbox.labStub must be true" }
if ($m.mockSandbox.liveTreasuryForbidden -ne $true) { $failures += "liveTreasuryForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) {
    $failures += "signing missing"
}
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.treasuryPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.treasuryPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$unapproved = Get-Json "testdata/ext003/scenarios/unapproved-run-fail.json"
$duplicate = Get-Json "testdata/ext003/scenarios/duplicate-submit-fail.json"
$totalsFail = Get-Json "testdata/ext003/scenarios/totals-mismatch-fail.json"
$manualFail = Get-Json "testdata/ext003/scenarios/manual-file-unaudited-fail.json"
$cutoffFail = Get-Json "testdata/ext003/scenarios/cutoff-breach-fail.json"

if ($unapproved.expectedValidate -ne "FAIL") { $failures += "unapproved-run expectedValidate must be FAIL" }
if (@($m.runStatusesAllowed) -contains [string]$unapproved.runStatus) {
    $failures += "unapproved-run fixture status must be outside runStatusesAllowed"
}

if ($duplicate.expectedValidate -ne "FAIL") { $failures += "duplicate-submit expectedValidate must be FAIL" }
if ($duplicate.duplicateSubmit -ne $true -or $duplicate.secondBusinessEffect -ne $true) {
    $failures += "duplicate-submit fixture must attempt second business effect"
}

if ($totalsFail.expectedValidate -ne "FAIL") { $failures += "totals-mismatch expectedValidate must be FAIL" }
if ([decimal]$totalsFail.orderTotal -eq [decimal]$totalsFail.runNetPayable -and $totalsFail.treatedAsReconciled -eq $true) {
    $failures += "totals-mismatch fixture must have mismatch treated as reconciled"
}
elseif ($totalsFail.treatedAsReconciled -ne $true) {
    $failures += "totals-mismatch fixture must treat mismatch as reconciled"
}

if ($manualFail.expectedValidate -ne "FAIL") { $failures += "manual-file-unaudited expectedValidate must be FAIL" }
if ($manualFail.audited -eq $true) { $failures += "manual-file-unaudited must be unaudited" }

if ($cutoffFail.expectedValidate -ne "FAIL") { $failures += "cutoff-breach expectedValidate must be FAIL" }
if ($cutoffFail.submittedAfterCutoff -ne $true -or $cutoffFail.contingencyHold -eq $true) {
    $failures += "cutoff-breach fixture must breach without contingency"
}

# Positive fixtures
$reconPass = Get-Json "testdata/ext003/scenarios/recon-totals-pass.json"
$rejectPass = Get-Json "testdata/ext003/scenarios/partial-reject-correct-pass.json"
$oooPass = Get-Json "testdata/ext003/scenarios/out-of-order-ack-pass.json"
$delayPass = Get-Json "testdata/ext003/scenarios/delay-ack-pass.json"
$parallelPass = Get-Json "testdata/ext003/scenarios/parallel-payroll-pass.json"
$auditPass = Get-Json "testdata/ext003/audit/payment-order-audit-pass.json"
$ledger = Get-Json "testdata/ext003/harness/treasury-run-ledger.json"

if ($reconPass.expectedValidate -ne "PASS") { $failures += "recon-totals expectedValidate must be PASS" }
if ([decimal]$reconPass.orderTotal -ne [decimal]$reconPass.runNetPayable) {
    $failures += "recon-totals totals must match"
}

if ($rejectPass.expectedValidate -ne "PASS") { $failures += "partial-reject-correct expectedValidate must be PASS" }
if ($rejectPass.correctionOpened -ne $true) { $failures += "partial-reject-correct correctionOpened must be true" }

if ($oooPass.expectedValidate -ne "PASS") { $failures += "out-of-order-ack expectedValidate must be PASS" }
if ($delayPass.expectedValidate -ne "PASS") { $failures += "delay-ack expectedValidate must be PASS" }
if ($parallelPass.expectedValidate -ne "PASS") { $failures += "parallel-payroll expectedValidate must be PASS" }

if ($auditPass.expectedValidate -ne "PASS") { $failures += "payment-order-audit expectedValidate must be PASS" }
if (@($auditPass.entries).Count -lt 1) { $failures += "payment-order-audit needs entries" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "treasury-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveTreasury -eq $true) { $failures += "ledger run $($r.runId) liveTreasury must be false" }
}

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Gateway/HRManagement.Gateway/appsettings.Staging.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Production.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Staging.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Staging.json"
)

foreach ($rel in $prodFiles) {
    $fullPath = Resolve-RepoPath $rel
    if (-not (Test-Path $fullPath)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $fullPath | ConvertFrom-Json
    $et = $cfg.ExtTreasury
    if ($null -eq $et) { $failures += "$rel missing ExtTreasury"; continue }
    if ($et.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($et.LiveTreasuryClaim -eq $true) { $failures += "$rel LiveTreasuryClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-003"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    paymentOrderFieldCount = @($m.paymentOrder.requiredFields).Count
    auditFieldCount = @($m.audit.requiredFields).Count
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "runtime-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: EXT-003 treasury-payment runtime validate"
exit 0
