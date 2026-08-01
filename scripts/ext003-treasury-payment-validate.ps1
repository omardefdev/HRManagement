# EXT-003: validate treasury/payment channel contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/ext003-treasury-payment-validate.ps1

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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "EXT-003") { $failures += "card must be EXT-003" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("ApprovedRunBinding", "ExactlyOnce", "TotalsRecon", "RejectCorrection", "ManualFileGate", "CutoffContingency", "ParallelPayroll", "OutOfOrderAck")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if (-not $m.approvedRunRequired) { $failures += "approvedRunRequired must be true" }
$runStatuses = @($m.runStatusesAllowed)
foreach ($need in @("Posted", "Approved")) {
    if ($runStatuses -notcontains $need) { $failures += "runStatusesAllowed missing $need" }
}

$orderFields = @($m.paymentOrder.requiredFields)
foreach ($need in @("orderId", "payrollRunId", "tenantId", "totalAmount", "lineCount", "idempotencyKey")) {
    if ($orderFields -notcontains $need) { $failures += "paymentOrder missing $need" }
}
if (-not $m.paymentOrder.bindingToRunId) { $failures += "bindingToRunId must be true" }

if (-not $m.signatures.labStubAllowed) { $failures += "labStubAllowed must be true" }
if (-not $m.signatures.livePkiRequiresExt004) { $failures += "livePkiRequiresExt004 must be true" }

$settle = @($m.settlement.states)
foreach ($need in @("Submitted", "Acked", "Settled", "Rejected", "Returned")) {
    if ($settle -notcontains $need) { $failures += "settlement missing $need" }
}

if (-not $m.reconciliation.totalsMustMatch) { $failures += "totalsMustMatch" }
if (-not $m.reconciliation.lineCountMustMatch) { $failures += "lineCountMustMatch" }
if (-not $m.exactlyOnce.idempotencyKeyRequired) { $failures += "idempotencyKeyRequired" }
if ($m.exactlyOnce.duplicatePolicy -ne "reject") { $failures += "duplicatePolicy must be reject" }
if (-not $m.rejectCorrectionWorkflow.required) { $failures += "rejectCorrectionWorkflow.required" }
if (-not $m.cutoffCalendar.cutoffRequired) { $failures += "cutoffRequired" }
if (-not $m.manualFiles.unauditedForbidden) { $failures += "unauditedForbidden" }
if (-not $m.bankAccountVerification.requiredBeforeSettle) { $failures += "requiredBeforeSettle" }
if (-not $m.audit.required) { $failures += "audit.required" }

$auditFields = @($m.audit.requiredFields)
foreach ($need in @("orderId", "payrollRunId", "actor", "tenantId", "outcome", "submittedAt")) {
    if ($auditFields -notcontains $need) { $failures += "audit missing $need" }
}

if (-not $m.mockSandbox.labStub -or -not $m.mockSandbox.liveTreasuryForbidden) {
    $failures += "mockSandbox must be labStub and liveTreasuryForbidden"
}

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace($sig.contentHash)) { $failures += "signing contentHash required" }
if ($sig.signedByRole -eq $sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["PAY-003"] -ne "Satisfied") { $failures += "PAY-003 must be Satisfied" }
if ($blockerMap["IAM-003"] -ne "Satisfied") { $failures += "IAM-003 must be Satisfied" }
if ($blockerMap["INT-003"] -ne "Satisfied") { $failures += "INT-003 must be Satisfied" }
if ($blockerMap["EXT-004"] -ne "NotStarted") { $failures += "EXT-004 must be NotStarted" }
if ($blockerMap["treasury-live"] -ne "Blocked") { $failures += "treasury-live must be Blocked" }
if ($blockerMap["bank-sandbox-cert"] -ne "Blocked") { $failures += "bank-sandbox-cert must be Blocked" }
if ($blockerMap["postal-channel-live"] -ne "Blocked") { $failures += "postal-channel-live must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if (-not [string]::IsNullOrEmpty($expect) -and $obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$unapp = Assert-FixtureExpected $m.simulationPaths.unapprovedRunFail "FAIL"
$dup = Assert-FixtureExpected $m.simulationPaths.duplicateSubmitFail "FAIL"
$totFail = Assert-FixtureExpected $m.simulationPaths.totalsMismatchFail "FAIL"
$recon = Assert-FixtureExpected $m.simulationPaths.reconTotalsPass "PASS"
$part = Assert-FixtureExpected $m.simulationPaths.partialRejectCorrectPass "PASS"
$manual = Assert-FixtureExpected $m.simulationPaths.manualFileUnauditedFail "FAIL"
$ooo = Assert-FixtureExpected $m.simulationPaths.outOfOrderAckPass "PASS"
$delay = Assert-FixtureExpected $m.simulationPaths.delayAckPass "PASS"
$par = Assert-FixtureExpected $m.simulationPaths.parallelPayrollPass "PASS"
$cut = Assert-FixtureExpected $m.simulationPaths.cutoffBreachFail "FAIL"
$audit = Assert-FixtureExpected $m.simulationPaths.paymentOrderAuditPass "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.treasuryRunLedger $null

if ($null -ne $unapp) {
    if ($runStatuses -contains $unapp.runStatus) {
        $failures += "unapproved FAIL must use non-allowed runStatus"
    }
}

if ($null -ne $dup) {
    if ($dup.duplicateSubmit -ne $true -or $dup.secondBusinessEffect -ne $true) {
        $failures += "duplicate FAIL must set duplicateSubmit and secondBusinessEffect"
    }
}

if ($null -ne $totFail) {
    if ([decimal]$totFail.orderTotal -eq [decimal]$totFail.runNetPayable) {
        $failures += "totals FAIL must mismatch amounts"
    }
    if ($totFail.treatedAsReconciled -ne $true) {
        $failures += "totals FAIL must treat as reconciled"
    }
}

if ($null -ne $recon) {
    if ([decimal]$recon.orderTotal -ne [decimal]$recon.runNetPayable) {
        $failures += "recon PASS totals must match"
    }
    if ([int]$recon.orderLineCount -ne [int]$recon.runLineCount) {
        $failures += "recon PASS line counts must match"
    }
    if ($recon.accountVerified -ne $true) {
        $failures += "recon PASS needs accountVerified"
    }
}

if ($null -ne $part) {
    if ($part.correctionOpened -ne $true -or $part.silentOverwriteToSettled -eq $true) {
        $failures += "partial reject correction incomplete"
    }
}

if ($null -ne $manual) {
    if ($manual.manualFile -ne $true -or $manual.audited -eq $true) {
        $failures += "manual unaudited FAIL incomplete"
    }
}

if ($null -ne $ooo) {
    if ($ooo.reconciledByOrderId -ne $true) {
        $failures += "out-of-order PASS needs reconciledByOrderId"
    }
}

if ($null -ne $delay) {
    if ($delay.heldAsSubmittedNotSettled -ne $true) {
        $failures += "delay PASS needs heldAsSubmittedNotSettled"
    }
}

if ($null -ne $par) {
    $keys = @($par.runs | ForEach-Object { $_.idempotencyKey })
    if ($keys.Count -lt 2) { $failures += "parallel PASS needs two runs" }
    if (($keys | Select-Object -Unique).Count -ne $keys.Count) {
        $failures += "parallel PASS keys must be distinct"
    }
    if ($par.crossRunKeyCollision -eq $true) {
        $failures += "parallel PASS must not collide keys"
    }
}

if ($null -ne $cut) {
    if ($cut.submittedAfterCutoff -ne $true -or $cut.contingencyHold -eq $true) {
        $failures += "cutoff FAIL must breach without contingency"
    }
    if ($cut.treatedAsSettled -ne $true) {
        $failures += "cutoff FAIL must treat as settled"
    }
}

if ($null -ne $audit) {
    if (@($audit.entries).Count -lt 1) { $failures += "audit entries empty" }
    foreach ($e in $audit.entries) {
        foreach ($f in $auditFields) {
            if ($null -eq $e.$f -or ($e.$f -is [string] -and [string]::IsNullOrWhiteSpace($e.$f))) {
                $failures += "audit entry missing $f"
            }
        }
    }
}

if ($null -ne $ledger) {
    if (@($ledger.runs).Count -lt 1) { $failures += "treasury ledger empty" }
    foreach ($r in $ledger.runs) {
        if ($r.liveTreasury -eq $true) { $failures += "ledger run must not be liveTreasury" }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-003"
    ok = ($failures.Count -eq 0)
    journeyCount = $journeyIds.Count
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: EXT-003 treasury-payment validate"
exit 0
