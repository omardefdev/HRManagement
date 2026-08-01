# PROD-007: validate extensibility contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/prod007-extensibility-validate.ps1

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

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("VersionCompat", "IsolationQuotas", "DeprecationUpgrade", "NoForkDb")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
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
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

foreach ($rel in @(
    "testdata/prod007/scenarios/n2-break-fail.json",
    "testdata/prod007/scenarios/malicious-extension-fail.json",
    "testdata/prod007/scenarios/slow-extension-fail.json",
    "testdata/prod007/scenarios/cross-tenant-extension-fail.json",
    "testdata/prod007/scenarios/direct-db-access-fail.json",
    "testdata/prod007/scenarios/undeprecated-breaking-fail.json",
    "testdata/prod007/scenarios/missing-upgrade-path-fail.json",
    "testdata/prod007/scenarios/n2-compat-pass.json",
    "testdata/prod007/scenarios/quota-enforced-pass.json",
    "testdata/prod007/scenarios/certified-consumer-pass.json",
    "testdata/prod007/scenarios/deprecation-notice-pass.json",
    "testdata/prod007/scenarios/upgrade-rehearsal-pass.json",
    "testdata/prod007/scenarios/isolation-pass.json",
    "testdata/prod007/audit/extensibility-audit-pass.json",
    "testdata/prod007/harness/extensibility-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$nb = Get-Json "testdata/prod007/scenarios/n2-break-fail.json"
if ($nb.expectedValidate -ne "FAIL" -or $nb.withinWindow -eq $true) { $failures += "n2-break-fail invalid" }
$me = Get-Json "testdata/prod007/scenarios/malicious-extension-fail.json"
if ($me.expectedValidate -ne "FAIL" -or $me.coreHealthy -eq $true) { $failures += "malicious-extension-fail invalid" }
$se = Get-Json "testdata/prod007/scenarios/slow-extension-fail.json"
if ($se.expectedValidate -ne "FAIL" -or $se.withinLimits -eq $true) { $failures += "slow-extension-fail invalid" }
$ct = Get-Json "testdata/prod007/scenarios/cross-tenant-extension-fail.json"
if ($ct.expectedValidate -ne "FAIL" -or $ct.withinTenant -eq $true) { $failures += "cross-tenant-extension-fail invalid" }
$db = Get-Json "testdata/prod007/scenarios/direct-db-access-fail.json"
if ($db.expectedValidate -ne "FAIL" -or $db.directDbAttempted -ne $true) { $failures += "direct-db-access-fail invalid" }
$ub = Get-Json "testdata/prod007/scenarios/undeprecated-breaking-fail.json"
if ($ub.expectedValidate -ne "FAIL" -or $ub.noticeSatisfied -eq $true) { $failures += "undeprecated-breaking-fail invalid" }
$mu = Get-Json "testdata/prod007/scenarios/missing-upgrade-path-fail.json"
if ($mu.expectedValidate -ne "FAIL" -or $mu.rehearsalPresent -eq $true) { $failures += "missing-upgrade-path-fail invalid" }

$nc = Get-Json "testdata/prod007/scenarios/n2-compat-pass.json"
if ($nc.expectedValidate -ne "PASS" -or $nc.withinWindow -ne $true) { $failures += "n2-compat-pass invalid" }
$qe = Get-Json "testdata/prod007/scenarios/quota-enforced-pass.json"
if ($qe.expectedValidate -ne "PASS" -or $qe.withinLimits -ne $true) { $failures += "quota-enforced-pass invalid" }
$cc = Get-Json "testdata/prod007/scenarios/certified-consumer-pass.json"
if ($cc.expectedValidate -ne "PASS" -or $cc.inventoryPresent -ne $true) { $failures += "certified-consumer-pass invalid" }
$dn = Get-Json "testdata/prod007/scenarios/deprecation-notice-pass.json"
if ($dn.expectedValidate -ne "PASS" -or $dn.noticeSatisfied -ne $true) { $failures += "deprecation-notice-pass invalid" }
$ur = Get-Json "testdata/prod007/scenarios/upgrade-rehearsal-pass.json"
if ($ur.expectedValidate -ne "PASS" -or $ur.rehearsalPresent -ne $true) { $failures += "upgrade-rehearsal-pass invalid" }
$ip = Get-Json "testdata/prod007/scenarios/isolation-pass.json"
if ($ip.expectedValidate -ne "PASS" -or $ip.withinTenant -ne $true) { $failures += "isolation-pass invalid" }

$audit = Get-Json "testdata/prod007/audit/extensibility-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "extensibility-audit-pass invalid" }
$ledger = Get-Json "testdata/prod007/harness/extensibility-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "extensibility-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveExtensibility -eq $true) { $failures += "ledger run $($r.runId) liveExtensibility must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-007"
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

Write-Host "PASS: PROD-007 extensibility pack validate"
exit 0
