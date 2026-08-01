# PROD-004: runtime/production remote-branch binding validate.
# Usage:
#   powershell -File scripts/prod004-remote-branch-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-branch/remote-branch-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod004/out"
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

if ($m.card -ne "PROD-004") { $failures += "card must be PROD-004" }

foreach ($need in @(
    @{ Id = "UX-002"; Status = "Satisfied" },
    @{ Id = "DATA-001"; Status = "Satisfied" },
    @{ Id = "FILE-001"; Status = "Satisfied" },
    @{ Id = "live-remote-branch-ux"; Status = "Blocked" },
    @{ Id = "live-pwa-fleet"; Status = "Blocked" },
    @{ Id = "live-offline-field"; Status = "Blocked" },
    @{ Id = "L10N-001"; Status = "NotStarted" },
    @{ Id = "MOB-001"; Status = "NotStarted" },
    @{ Id = "PROD-005"; Status = "Satisfied" },
    @{ Id = "SYR-013"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.performanceBudgets.required -ne $true) { $failures += "performanceBudgets.required must be true" }
if ([int]$m.performanceBudgets.maxTtiMs -le 0) { $failures += "maxTtiMs required" }
if ([int]$m.performanceBudgets.maxPayloadKb -le 0) { $failures += "maxPayloadKb required" }
if ($m.networkProfile.throttledLabRequired -ne $true) { $failures += "throttledLabRequired must be true" }
if ($m.networkProfile.lowBandwidthSupported -ne $true) { $failures += "lowBandwidthSupported must be true" }
if ($m.responsivePwa.required -ne $true) { $failures += "responsivePwa.required must be true" }
if ($m.responsivePwa.labStubAllowed -ne $true) { $failures += "labStubAllowed must be true" }
if ($m.responsivePwa.livePwaForbidden -ne $true) { $failures += "livePwaForbidden must be true" }
if ($m.resumableUploads.required -ne $true) { $failures += "resumableUploads.required must be true" }
if ($m.saveDraft.required -ne $true) { $failures += "saveDraft.required must be true" }
if ($m.retryIdempotency.required -ne $true) { $failures += "retryIdempotency.required must be true" }
if ($m.retryIdempotency.duplicateSubmitSafe -ne $true) { $failures += "duplicateSubmitSafe must be true" }
if ($m.localization.arabicEnglishRequired -ne $true) { $failures += "arabicEnglishRequired must be true" }
if ($m.localization.rtlLtrRequired -ne $true) { $failures += "rtlLtrRequired must be true" }
if ($m.localization.localLanguagesDecisionRequired -ne $true) { $failures += "localLanguagesDecisionRequired must be true" }
if ($m.timezoneCalendar.required -ne $true) { $failures += "timezoneCalendar.required must be true" }
if ($m.printableFallback.required -ne $true) { $failures += "printableFallback.required must be true" }
if ($m.offline.limitedAllowed -ne $true) { $failures += "limitedAllowed must be true" }
if ($m.offline.encryptionRequired -ne $true) { $failures += "encryptionRequired must be true" }
if ($m.offline.threatModelRequired -ne $true) { $failures += "threatModelRequired must be true" }
if ($m.offline.liveOfflineForbidden -ne $true) { $failures += "liveOfflineForbidden must be true" }
if ($m.accessibility.wcag22AaRequired -ne $true) { $failures += "wcag22AaRequired must be true" }
if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("journeyId", "networkProfileId", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}
if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveRemoteBranchForbidden -ne $true) { $failures += "liveRemoteBranchForbidden must be true" }
if ($m.mockSandbox.liveFullOfflineForbidden -ne $true) { $failures += "liveFullOfflineForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.remoteBranchPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.remoteBranchPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$bb = Get-Json "testdata/prod004/scenarios/below-budget-fail.json"
$dl = Get-Json "testdata/prod004/scenarios/data-loss-on-interrupt-fail.json"
$hc = Get-Json "testdata/prod004/scenarios/hardcode-locale-fail.json"
$pt = Get-Json "testdata/prod004/scenarios/plaintext-offline-fail.json"
$wf = Get-Json "testdata/prod004/scenarios/wcag-fail.json"
$ni = Get-Json "testdata/prod004/scenarios/non-idempotent-retry-fail.json"

if ($bb.expectedValidate -ne "FAIL" -or $bb.withinBudget -eq $true) { $failures += "below-budget-fail invalid" }
if ($dl.expectedValidate -ne "FAIL" -or $dl.dataPreserved -eq $true) { $failures += "data-loss-on-interrupt-fail invalid" }
if ($hc.expectedValidate -ne "FAIL" -or $hc.arabicEnglish -eq $true) { $failures += "hardcode-locale-fail invalid" }
if ($pt.expectedValidate -ne "FAIL" -or $pt.encrypted -eq $true) { $failures += "plaintext-offline-fail invalid" }
if ($wf.expectedValidate -ne "FAIL" -or $wf.wcag22Aa -eq $true) { $failures += "wcag-fail invalid" }
if ($ni.expectedValidate -ne "FAIL" -or $ni.idempotent -eq $true) { $failures += "non-idempotent-retry-fail invalid" }

# Positive
$tc = Get-Json "testdata/prod004/scenarios/throttled-core-journey-pass.json"
$ir = Get-Json "testdata/prod004/scenarios/interrupted-upload-resume-pass.json"
$sd = Get-Json "testdata/prod004/scenarios/save-draft-reconnect-pass.json"
$ae = Get-Json "testdata/prod004/scenarios/ar-en-rtl-ltr-pass.json"
$oe = Get-Json "testdata/prod004/scenarios/offline-encrypted-limited-pass.json"
$wa = Get-Json "testdata/prod004/scenarios/wcag-audit-pass.json"
$audit = Get-Json "testdata/prod004/audit/branch-audit-pass.json"
$ledger = Get-Json "testdata/prod004/harness/branch-run-ledger.json"

if ($tc.expectedValidate -ne "PASS") { $failures += "throttled-core-journey-pass invalid" }
if ($ir.expectedValidate -ne "PASS") { $failures += "interrupted-upload-resume-pass invalid" }
if ($sd.expectedValidate -ne "PASS") { $failures += "save-draft-reconnect-pass invalid" }
if ($ae.expectedValidate -ne "PASS") { $failures += "ar-en-rtl-ltr-pass invalid" }
if ($oe.expectedValidate -ne "PASS") { $failures += "offline-encrypted-limited-pass invalid" }
if ($wa.expectedValidate -ne "PASS") { $failures += "wcag-audit-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "branch-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "branch-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveRemoteBranch -eq $true) { $failures += "ledger run $($r.runId) liveRemoteBranch must be false" }
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
    $pb = $cfg.ProdBranch
    if ($null -eq $pb) { $failures += "$rel missing ProdBranch"; continue }
    if ($pb.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pb.LiveRemoteBranchClaim -eq $true) { $failures += "$rel LiveRemoteBranchClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-004"
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

Write-Host "PASS: PROD-004 remote-branch runtime validate"
exit 0
