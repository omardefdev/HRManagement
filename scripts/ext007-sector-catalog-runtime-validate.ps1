# EXT-007: runtime/production sector-catalog binding validate.
# Usage:
#   powershell -File scripts/ext007-sector-catalog-runtime-validate.ps1

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

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("adapterId", "domain", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveAdaptersForbidden -ne $true) { $failures += "liveAdaptersForbidden must be true" }
if ($m.mockSandbox.liveNotificationForbidden -ne $true) { $failures += "liveNotificationForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.sectorCatalogPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.sectorCatalogPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$implicit = Get-Json "testdata/ext007/scenarios/implicit-critical-fail.json"
$contract = Get-Json "testdata/ext007/scenarios/missing-contract-fail.json"
$slo = Get-Json "testdata/ext007/scenarios/missing-slo-fail.json"
$exitF = Get-Json "testdata/ext007/scenarios/missing-exit-fail.json"
$pii = Get-Json "testdata/ext007/scenarios/notify-pii-fail.json"
$swallow = Get-Json "testdata/ext007/scenarios/swallowed-failure-fail.json"
$uncert = Get-Json "testdata/ext007/scenarios/uncertified-adapter-enable-fail.json"

if ($implicit.expectedValidate -ne "FAIL" -or $implicit.isInCatalog -eq $true) { $failures += "implicit-critical-fail invalid" }
if ($contract.expectedValidate -ne "FAIL" -or $contract.hasContract -eq $true) { $failures += "missing-contract-fail invalid" }
if ($slo.expectedValidate -ne "FAIL" -or $slo.hasSlo -eq $true) { $failures += "missing-slo-fail invalid" }
if ($exitF.expectedValidate -ne "FAIL" -or $exitF.hasExit -eq $true) { $failures += "missing-exit-fail invalid" }
if ($pii.expectedValidate -ne "FAIL" -or $pii.containsSensitivePii -ne $true) { $failures += "notify-pii-fail invalid" }
if ($swallow.expectedValidate -ne "FAIL" -or $swallow.wasSwallowed -ne $true) { $failures += "swallowed-failure-fail invalid" }
if ($uncert.expectedValidate -ne "FAIL" -or $uncert.certified -eq $true) { $failures += "uncertified-adapter-enable-fail invalid" }

# Positive
$entry = Get-Json "testdata/ext007/scenarios/catalog-entry-pass.json"
$outage = Get-Json "testdata/ext007/scenarios/outage-visible-pass.json"
$recon = Get-Json "testdata/ext007/scenarios/reconciliation-pass.json"
$receipt = Get-Json "testdata/ext007/scenarios/delivery-receipt-pass.json"
$audit = Get-Json "testdata/ext007/audit/adapter-cert-audit-pass.json"
$ledger = Get-Json "testdata/ext007/harness/sector-run-ledger.json"

if ($entry.expectedValidate -ne "PASS") { $failures += "catalog-entry-pass invalid" }
if ($outage.expectedValidate -ne "PASS") { $failures += "outage-visible-pass invalid" }
if ($recon.expectedValidate -ne "PASS") { $failures += "reconciliation-pass invalid" }
if ($receipt.expectedValidate -ne "PASS") { $failures += "delivery-receipt-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "adapter-cert-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "sector-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveAdapters -eq $true) { $failures += "ledger run $($r.runId) liveAdapters must be false" }
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
    $es = $cfg.ExtSector
    if ($null -eq $es) { $failures += "$rel missing ExtSector"; continue }
    if ($es.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($es.LiveSectorCatalogClaim -eq $true) { $failures += "$rel LiveSectorCatalogClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-007"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
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

Write-Host "PASS: EXT-007 sector-catalog runtime validate"
exit 0
