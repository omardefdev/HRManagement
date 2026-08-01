# EXT-007: validate sector-catalog contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/ext007-sector-catalog-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-sector/sector-catalog-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext007/out"
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

if ($m.card -ne "EXT-007") { $failures += "card must be EXT-007" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("NoImplicitCritical", "AdapterCompleteness", "NotificationPii", "FailureSurfacing", "CertReconReceipts")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if ($m.noImplicitCriticalIntegrations -ne $true) { $failures += "noImplicitCriticalIntegrations must be true" }
if ($m.adapterRequirements.contractRequired -ne $true) { $failures += "contractRequired must be true" }
if ($m.adapterRequirements.sloRequired -ne $true) { $failures += "sloRequired must be true" }
if ($m.adapterRequirements.retryRequired -ne $true) { $failures += "retryRequired must be true" }
if ($m.adapterRequirements.reconciliationRequired -ne $true) { $failures += "reconciliationRequired must be true" }
if ($m.adapterRequirements.exitRequired -ne $true) { $failures += "exitRequired must be true" }
if ($m.notification.sensitivePiiForbidden -ne $true) { $failures += "sensitivePiiForbidden must be true" }
if ($m.failure.mustSurface -ne $true) { $failures += "mustSurface must be true" }
if ($m.failure.swallowForbidden -ne $true) { $failures += "swallowForbidden must be true" }
if ($m.certification.requiredBeforeEnable -ne $true) { $failures += "requiredBeforeEnable must be true" }
if ($m.reconciliation.required -ne $true) { $failures += "reconciliation.required must be true" }
if ($m.deliveryReceipts.requiredWhereApplicable -ne $true) { $failures += "deliveryReceipts.requiredWhereApplicable must be true" }

$domainIds = @($m.catalogDomains | ForEach-Object { $_.id })
foreach ($need in @("pension", "insurance", "tax", "attendance", "erp", "budget", "mail-notify")) {
    if ($domainIds -notcontains $need) { $failures += "missing catalogDomain $need" }
}
foreach ($d in @($m.catalogDomains)) {
    foreach ($field in @("priority", "sourceOfTruth", "latencyClass", "ownerRole", "fallback")) {
        if ([string]::IsNullOrWhiteSpace([string]$d.$field)) {
            $failures += "catalogDomain $($d.id) missing $field"
        }
    }
}

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("adapterId", "domain", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveAdaptersForbidden -ne $true) { $failures += "liveAdaptersForbidden must be true" }
if ($m.mockSandbox.liveNotificationForbidden -ne $true) { $failures += "liveNotificationForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "EXT-005"; Status = "Satisfied" },
    @{ Id = "STR-003"; Status = "Satisfied" },
    @{ Id = "live-sector-adapters"; Status = "Blocked" },
    @{ Id = "live-notification-channel"; Status = "Blocked" },
    @{ Id = "adapter-fleet-certified-live"; Status = "Blocked" },
    @{ Id = "PROD-005"; Status = "Satisfied" },
    @{ Id = "COM-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/ext007/scenarios/implicit-critical-fail.json",
    "testdata/ext007/scenarios/missing-contract-fail.json",
    "testdata/ext007/scenarios/missing-slo-fail.json",
    "testdata/ext007/scenarios/missing-exit-fail.json",
    "testdata/ext007/scenarios/notify-pii-fail.json",
    "testdata/ext007/scenarios/swallowed-failure-fail.json",
    "testdata/ext007/scenarios/uncertified-adapter-enable-fail.json",
    "testdata/ext007/scenarios/catalog-entry-pass.json",
    "testdata/ext007/scenarios/outage-visible-pass.json",
    "testdata/ext007/scenarios/reconciliation-pass.json",
    "testdata/ext007/scenarios/delivery-receipt-pass.json",
    "testdata/ext007/audit/adapter-cert-audit-pass.json",
    "testdata/ext007/harness/sector-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$implicit = Get-Json "testdata/ext007/scenarios/implicit-critical-fail.json"
if ($implicit.expectedValidate -ne "FAIL" -or $implicit.isDeclaredCritical -ne $true -or $implicit.isInCatalog -eq $true) {
    $failures += "implicit-critical-fail invalid"
}
$contract = Get-Json "testdata/ext007/scenarios/missing-contract-fail.json"
if ($contract.expectedValidate -ne "FAIL" -or $contract.hasContract -eq $true) { $failures += "missing-contract-fail invalid" }
$slo = Get-Json "testdata/ext007/scenarios/missing-slo-fail.json"
if ($slo.expectedValidate -ne "FAIL" -or $slo.hasSlo -eq $true) { $failures += "missing-slo-fail invalid" }
$exit = Get-Json "testdata/ext007/scenarios/missing-exit-fail.json"
if ($exit.expectedValidate -ne "FAIL" -or $exit.hasExit -eq $true) { $failures += "missing-exit-fail invalid" }
$pii = Get-Json "testdata/ext007/scenarios/notify-pii-fail.json"
if ($pii.expectedValidate -ne "FAIL" -or $pii.containsSensitivePii -ne $true) { $failures += "notify-pii-fail invalid" }
$swallow = Get-Json "testdata/ext007/scenarios/swallowed-failure-fail.json"
if ($swallow.expectedValidate -ne "FAIL" -or $swallow.wasSwallowed -ne $true) { $failures += "swallowed-failure-fail invalid" }
$uncert = Get-Json "testdata/ext007/scenarios/uncertified-adapter-enable-fail.json"
if ($uncert.expectedValidate -ne "FAIL" -or $uncert.enabled -ne $true -or $uncert.certified -eq $true) {
    $failures += "uncertified-adapter-enable-fail invalid"
}

$entry = Get-Json "testdata/ext007/scenarios/catalog-entry-pass.json"
if ($entry.expectedValidate -ne "PASS" -or $entry.isInCatalog -ne $true) { $failures += "catalog-entry-pass invalid" }
$outage = Get-Json "testdata/ext007/scenarios/outage-visible-pass.json"
if ($outage.expectedValidate -ne "PASS" -or $outage.wasSwallowed -eq $true) { $failures += "outage-visible-pass invalid" }
$recon = Get-Json "testdata/ext007/scenarios/reconciliation-pass.json"
if ($recon.expectedValidate -ne "PASS" -or $recon.completed -ne $true) { $failures += "reconciliation-pass invalid" }
$receipt = Get-Json "testdata/ext007/scenarios/delivery-receipt-pass.json"
if ($receipt.expectedValidate -ne "PASS" -or $receipt.hasReceipt -ne $true) { $failures += "delivery-receipt-pass invalid" }

$audit = Get-Json "testdata/ext007/audit/adapter-cert-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "adapter-cert-audit-pass invalid" }
$ledger = Get-Json "testdata/ext007/harness/sector-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "sector-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveAdapters -eq $true) { $failures += "ledger run $($r.runId) liveAdapters must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-007"
    mode = "pack-validate"
    ok = ($failures.Count -eq 0)
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

Write-Host "PASS: EXT-007 sector-catalog validate"
exit 0
