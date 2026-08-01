# PROD-007: runtime/production extensibility-platform binding validate.
# Usage:
#   powershell -File scripts/prod007-extensibility-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-extensibility/extensibility-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod007/out"
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

if ($m.card -ne "PROD-007") { $failures += "card must be PROD-007" }

foreach ($need in @(
    @{ Id = "INT-002"; Status = "Satisfied" },
    @{ Id = "SUP-001"; Status = "Satisfied" },
    @{ Id = "STR-004"; Status = "Satisfied" },
    @{ Id = "live-extension-runtime"; Status = "Blocked" },
    @{ Id = "live-partner-sdk"; Status = "Blocked" },
    @{ Id = "live-webhook-delivery"; Status = "Blocked" },
    @{ Id = "EXT-006"; Status = "NotStarted" },
    @{ Id = "IOP-001"; Status = "NotStarted" },
    @{ Id = "REL-004"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.supportedContracts.required -ne $true) { $failures += "supportedContracts.required must be true" }
foreach ($need in @("apis", "events", "webhooks")) {
    if (@($m.supportedContracts.contractTypes) -notcontains $need) { $failures += "missing contractType $need" }
}
if ($m.semanticVersioning.required -ne $true) { $failures += "semanticVersioning.required must be true" }
if ($m.semanticVersioning.semverRequired -ne $true) { $failures += "semverRequired must be true" }
if ($m.semanticVersioning.breakingRequiresMajorBump -ne $true) { $failures += "breakingRequiresMajorBump must be true" }
if ($m.compatibilityWindows.required -ne $true) { $failures += "compatibilityWindows.required must be true" }
if ($m.compatibilityWindows.supportNMinus2 -ne $true) { $failures += "supportNMinus2 must be true" }
if ($m.compatibilityWindows.measureBackwardCompat -ne $true) { $failures += "measureBackwardCompat must be true" }
if ($m.pluginIsolation.required -ne $true) { $failures += "pluginIsolation.required must be true" }
if ($m.pluginIsolation.crossTenantForbidden -ne $true) { $failures += "crossTenantForbidden must be true" }
if ($m.pluginIsolation.coreDisruptionForbidden -ne $true) { $failures += "coreDisruptionForbidden must be true" }
if ($m.quotas.required -ne $true) { $failures += "quotas.required must be true" }
if ($m.quotas.enforceRpm -ne $true) { $failures += "enforceRpm must be true" }
if ($m.certification.required -ne $true) { $failures += "certification.required must be true" }
if ($m.certification.consumerInventoryRequired -ne $true) { $failures += "consumerInventoryRequired must be true" }
if ($m.certification.labStubAllowed -ne $true) { $failures += "certification.labStubAllowed must be true" }
if ($m.certification.liveForbidden -ne $true) { $failures += "certification.liveForbidden must be true" }
if ($m.deprecation.required -ne $true) { $failures += "deprecation.required must be true" }
if ([int]$m.deprecation.noticeDaysMin -lt 90) { $failures += "noticeDaysMin must be >= 90" }
if ($m.deprecation.measuredAnnouncementRequired -ne $true) { $failures += "measuredAnnouncementRequired must be true" }
if ($m.sdkPolicy.required -ne $true) { $failures += "sdkPolicy.required must be true" }
if ($m.sdkPolicy.labStubAllowed -ne $true) { $failures += "sdkPolicy.labStubAllowed must be true" }
if ($m.sdkPolicy.liveSdkForbidden -ne $true) { $failures += "liveSdkForbidden must be true" }
if ($m.noDirectDb -ne $true) { $failures += "noDirectDb must be true" }
if ($m.noCustomFork -ne $true) { $failures += "noCustomFork must be true" }
if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("extensionId", "contractRef", "tenantId", "version", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}
if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveExtensionRuntimeForbidden -ne $true) { $failures += "liveExtensionRuntimeForbidden must be true" }
if ($m.mockSandbox.livePartnerSdkForbidden -ne $true) { $failures += "livePartnerSdkForbidden must be true" }
if ($m.mockSandbox.liveWebhookDeliveryForbidden -ne $true) { $failures += "liveWebhookDeliveryForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.extensibilityPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.extensibilityPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$nb = Get-Json "testdata/prod007/scenarios/n2-break-fail.json"
$me = Get-Json "testdata/prod007/scenarios/malicious-extension-fail.json"
$se = Get-Json "testdata/prod007/scenarios/slow-extension-fail.json"
$ct = Get-Json "testdata/prod007/scenarios/cross-tenant-extension-fail.json"
$db = Get-Json "testdata/prod007/scenarios/direct-db-access-fail.json"
$ub = Get-Json "testdata/prod007/scenarios/undeprecated-breaking-fail.json"
$mu = Get-Json "testdata/prod007/scenarios/missing-upgrade-path-fail.json"

if ($nb.expectedValidate -ne "FAIL" -or $nb.withinWindow -eq $true) { $failures += "n2-break-fail invalid" }
if ($me.expectedValidate -ne "FAIL" -or $me.coreHealthy -eq $true) { $failures += "malicious-extension-fail invalid" }
if ($se.expectedValidate -ne "FAIL" -or $se.withinLimits -eq $true) { $failures += "slow-extension-fail invalid" }
if ($ct.expectedValidate -ne "FAIL" -or $ct.withinTenant -eq $true) { $failures += "cross-tenant-extension-fail invalid" }
if ($db.expectedValidate -ne "FAIL" -or $db.directDbAttempted -ne $true) { $failures += "direct-db-access-fail invalid" }
if ($ub.expectedValidate -ne "FAIL" -or $ub.noticeSatisfied -eq $true) { $failures += "undeprecated-breaking-fail invalid" }
if ($mu.expectedValidate -ne "FAIL" -or $mu.rehearsalPresent -eq $true) { $failures += "missing-upgrade-path-fail invalid" }

# Positive
$nc = Get-Json "testdata/prod007/scenarios/n2-compat-pass.json"
$qe = Get-Json "testdata/prod007/scenarios/quota-enforced-pass.json"
$cc = Get-Json "testdata/prod007/scenarios/certified-consumer-pass.json"
$dn = Get-Json "testdata/prod007/scenarios/deprecation-notice-pass.json"
$ur = Get-Json "testdata/prod007/scenarios/upgrade-rehearsal-pass.json"
$ip = Get-Json "testdata/prod007/scenarios/isolation-pass.json"
$audit = Get-Json "testdata/prod007/audit/extensibility-audit-pass.json"
$ledger = Get-Json "testdata/prod007/harness/extensibility-run-ledger.json"

if ($nc.expectedValidate -ne "PASS") { $failures += "n2-compat-pass invalid" }
if ($qe.expectedValidate -ne "PASS") { $failures += "quota-enforced-pass invalid" }
if ($cc.expectedValidate -ne "PASS") { $failures += "certified-consumer-pass invalid" }
if ($dn.expectedValidate -ne "PASS") { $failures += "deprecation-notice-pass invalid" }
if ($ur.expectedValidate -ne "PASS") { $failures += "upgrade-rehearsal-pass invalid" }
if ($ip.expectedValidate -ne "PASS") { $failures += "isolation-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "extensibility-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "extensibility-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveExtensibility -eq $true) { $failures += "ledger run $($r.runId) liveExtensibility must be false" }
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
    $pe = $cfg.ProdExtensibility
    if ($null -eq $pe) { $failures += "$rel missing ProdExtensibility"; continue }
    if ($pe.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pe.LiveExtensibilityClaim -eq $true) { $failures += "$rel LiveExtensibilityClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-007"
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

Write-Host "PASS: PROD-007 extensibility runtime validate"
exit 0
