# PROD-005: validate workbox contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/prod005-workbox-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-workbox/workbox-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod005/out"
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

if ($m.card -ne "PROD-005") { $failures += "card must be PROD-005" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("QueueCompleteness", "SafeSearch", "BulkFourEyes", "NotifyDeepLink")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.queueCompleteness.required -ne $true) { $failures += "queueCompleteness.required must be true" }
if ($m.queueCompleteness.noLostCrossModule -ne $true) { $failures += "noLostCrossModule must be true" }
foreach ($need in @("tasks", "cases", "approvals")) {
    if (@($m.queueCompleteness.itemTypes) -notcontains $need) { $failures += "missing itemType $need" }
}
if ($m.prioritySlaEscalation.required -ne $true) { $failures += "prioritySlaEscalation.required must be true" }
if ($m.safeSearch.required -ne $true) { $failures += "safeSearch.required must be true" }
if ($m.safeSearch.tenantScoped -ne $true) { $failures += "tenantScoped must be true" }
if ($m.safeSearch.orgScoped -ne $true) { $failures += "orgScoped must be true" }
if ($m.safeSearch.existenceLeakForbidden -ne $true) { $failures += "existenceLeakForbidden must be true" }
if ($m.bulkActions.required -ne $true) { $failures += "bulkActions.required must be true" }
if ($m.bulkActions.limitsRequired -ne $true) { $failures += "limitsRequired must be true" }
if ($m.bulkActions.fourEyesRequired -ne $true) { $failures += "fourEyesRequired must be true" }
if ($m.notifications.required -ne $true) { $failures += "notifications.required must be true" }
if ($m.notifications.noSecretsInPayload -ne $true) { $failures += "noSecretsInPayload must be true" }
if ($m.notifications.deliveryReceiptsRequired -ne $true) { $failures += "deliveryReceiptsRequired must be true" }
if ($m.notifications.labStubAllowed -ne $true) { $failures += "labStubAllowed must be true" }
if ($m.notifications.liveForbidden -ne $true) { $failures += "notifications.liveForbidden must be true" }
if ($m.deepLinks.required -ne $true) { $failures += "deepLinks.required must be true" }
if ($m.deepLinks.reauthOnExpiredRequired -ne $true) { $failures += "reauthOnExpiredRequired must be true" }
if ($m.deepLinks.correctStateRequired -ne $true) { $failures += "correctStateRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("itemId", "actorRole", "tenantId", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveWorkboxForbidden -ne $true) { $failures += "liveWorkboxForbidden must be true" }
if ($m.mockSandbox.liveNotificationForbidden -ne $true) { $failures += "liveNotificationForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "INT-003"; Status = "Satisfied" },
    @{ Id = "EXT-007"; Status = "Satisfied" },
    @{ Id = "IAM-002"; Status = "NotStarted" },
    @{ Id = "live-workbox-ui"; Status = "Blocked" },
    @{ Id = "live-notification-delivery"; Status = "Blocked" },
    @{ Id = "live-safe-search"; Status = "Blocked" },
    @{ Id = "COM-001"; Status = "NotStarted" },
    @{ Id = "WF-002"; Status = "NotStarted" },
    @{ Id = "PROD-006"; Status = "Satisfied" },
    @{ Id = "NAT-005"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/prod005/scenarios/lost-transaction-fail.json",
    "testdata/prod005/scenarios/cross-tenant-search-fail.json",
    "testdata/prod005/scenarios/bulk-without-limit-fail.json",
    "testdata/prod005/scenarios/secret-in-notification-fail.json",
    "testdata/prod005/scenarios/expired-deeplink-no-reauth-fail.json",
    "testdata/prod005/scenarios/notification-failure-untracked-fail.json",
    "testdata/prod005/scenarios/case-persists-pass.json",
    "testdata/prod005/scenarios/scoped-search-pass.json",
    "testdata/prod005/scenarios/bulk-four-eyes-pass.json",
    "testdata/prod005/scenarios/delivery-receipt-pass.json",
    "testdata/prod005/scenarios/deeplink-reauth-pass.json",
    "testdata/prod005/scenarios/escalation-queue-pass.json",
    "testdata/prod005/audit/workbox-audit-pass.json",
    "testdata/prod005/harness/workbox-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$lt = Get-Json "testdata/prod005/scenarios/lost-transaction-fail.json"
if ($lt.expectedValidate -ne "FAIL" -or $lt.persistedAcrossModules -eq $true) { $failures += "lost-transaction-fail invalid" }
$ct = Get-Json "testdata/prod005/scenarios/cross-tenant-search-fail.json"
if ($ct.expectedValidate -ne "FAIL" -or $ct.withinTenant -eq $true) { $failures += "cross-tenant-search-fail invalid" }
$bl = Get-Json "testdata/prod005/scenarios/bulk-without-limit-fail.json"
if ($bl.expectedValidate -ne "FAIL" -or $bl.withinLimits -eq $true) { $failures += "bulk-without-limit-fail invalid" }
$sn = Get-Json "testdata/prod005/scenarios/secret-in-notification-fail.json"
if ($sn.expectedValidate -ne "FAIL" -or $sn.containsSecret -ne $true) { $failures += "secret-in-notification-fail invalid" }
$ed = Get-Json "testdata/prod005/scenarios/expired-deeplink-no-reauth-fail.json"
if ($ed.expectedValidate -ne "FAIL" -or $ed.reauthPerformed -eq $true) { $failures += "expired-deeplink-no-reauth-fail invalid" }
$nf = Get-Json "testdata/prod005/scenarios/notification-failure-untracked-fail.json"
if ($nf.expectedValidate -ne "FAIL") { $failures += "notification-failure-untracked-fail invalid" }

$cp = Get-Json "testdata/prod005/scenarios/case-persists-pass.json"
if ($cp.expectedValidate -ne "PASS" -or $cp.persistedAcrossModules -ne $true) { $failures += "case-persists-pass invalid" }
$ss = Get-Json "testdata/prod005/scenarios/scoped-search-pass.json"
if ($ss.expectedValidate -ne "PASS" -or $ss.withinTenant -ne $true) { $failures += "scoped-search-pass invalid" }
$bf = Get-Json "testdata/prod005/scenarios/bulk-four-eyes-pass.json"
if ($bf.expectedValidate -ne "PASS" -or $bf.fourEyesSatisfied -ne $true) { $failures += "bulk-four-eyes-pass invalid" }
$dr = Get-Json "testdata/prod005/scenarios/delivery-receipt-pass.json"
if ($dr.expectedValidate -ne "PASS" -or $dr.hasDeliveryReceipt -ne $true) { $failures += "delivery-receipt-pass invalid" }
$dl = Get-Json "testdata/prod005/scenarios/deeplink-reauth-pass.json"
if ($dl.expectedValidate -ne "PASS" -or $dl.reauthPerformed -ne $true) { $failures += "deeplink-reauth-pass invalid" }
$eq = Get-Json "testdata/prod005/scenarios/escalation-queue-pass.json"
if ($eq.expectedValidate -ne "PASS" -or $eq.escalationPresent -ne $true) { $failures += "escalation-queue-pass invalid" }

$audit = Get-Json "testdata/prod005/audit/workbox-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "workbox-audit-pass invalid" }
$ledger = Get-Json "testdata/prod005/harness/workbox-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "workbox-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveWorkbox -eq $true) { $failures += "ledger run $($r.runId) liveWorkbox must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-005"
    mode = "pack-validate"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "pack-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: PROD-005 workbox pack validate"
exit 0
