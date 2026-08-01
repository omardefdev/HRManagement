# PROD-004: validate remote-branch contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/prod004-remote-branch-validate.ps1

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

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("LowBandwidthCore", "InterruptResilience", "LocaleAndA11y", "OfflineThreat")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.performanceBudgets.required -ne $true) { $failures += "performanceBudgets.required must be true" }
if ($null -eq $m.performanceBudgets.maxTtiMs -or [int]$m.performanceBudgets.maxTtiMs -le 0) { $failures += "maxTtiMs required" }
if ($null -eq $m.performanceBudgets.maxPayloadKb -or [int]$m.performanceBudgets.maxPayloadKb -le 0) { $failures += "maxPayloadKb required" }
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
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

foreach ($rel in @(
    "testdata/prod004/scenarios/below-budget-fail.json",
    "testdata/prod004/scenarios/data-loss-on-interrupt-fail.json",
    "testdata/prod004/scenarios/hardcode-locale-fail.json",
    "testdata/prod004/scenarios/plaintext-offline-fail.json",
    "testdata/prod004/scenarios/wcag-fail.json",
    "testdata/prod004/scenarios/non-idempotent-retry-fail.json",
    "testdata/prod004/scenarios/throttled-core-journey-pass.json",
    "testdata/prod004/scenarios/interrupted-upload-resume-pass.json",
    "testdata/prod004/scenarios/save-draft-reconnect-pass.json",
    "testdata/prod004/scenarios/ar-en-rtl-ltr-pass.json",
    "testdata/prod004/scenarios/offline-encrypted-limited-pass.json",
    "testdata/prod004/scenarios/wcag-audit-pass.json",
    "testdata/prod004/audit/branch-audit-pass.json",
    "testdata/prod004/harness/branch-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$bb = Get-Json "testdata/prod004/scenarios/below-budget-fail.json"
if ($bb.expectedValidate -ne "FAIL" -or $bb.withinBudget -eq $true) { $failures += "below-budget-fail invalid" }
$dl = Get-Json "testdata/prod004/scenarios/data-loss-on-interrupt-fail.json"
if ($dl.expectedValidate -ne "FAIL" -or $dl.dataPreserved -eq $true) { $failures += "data-loss-on-interrupt-fail invalid" }
$hc = Get-Json "testdata/prod004/scenarios/hardcode-locale-fail.json"
if ($hc.expectedValidate -ne "FAIL" -or $hc.arabicEnglish -eq $true) { $failures += "hardcode-locale-fail invalid" }
$pt = Get-Json "testdata/prod004/scenarios/plaintext-offline-fail.json"
if ($pt.expectedValidate -ne "FAIL" -or $pt.encrypted -eq $true) { $failures += "plaintext-offline-fail invalid" }
$wf = Get-Json "testdata/prod004/scenarios/wcag-fail.json"
if ($wf.expectedValidate -ne "FAIL" -or $wf.wcag22Aa -eq $true) { $failures += "wcag-fail invalid" }
$ni = Get-Json "testdata/prod004/scenarios/non-idempotent-retry-fail.json"
if ($ni.expectedValidate -ne "FAIL" -or $ni.idempotent -eq $true) { $failures += "non-idempotent-retry-fail invalid" }

$tc = Get-Json "testdata/prod004/scenarios/throttled-core-journey-pass.json"
if ($tc.expectedValidate -ne "PASS" -or $tc.withinBudget -ne $true) { $failures += "throttled-core-journey-pass invalid" }
$ir = Get-Json "testdata/prod004/scenarios/interrupted-upload-resume-pass.json"
if ($ir.expectedValidate -ne "PASS" -or $ir.resumable -ne $true -or $ir.dataPreserved -ne $true) { $failures += "interrupted-upload-resume-pass invalid" }
$sd = Get-Json "testdata/prod004/scenarios/save-draft-reconnect-pass.json"
if ($sd.expectedValidate -ne "PASS" -or $sd.draftPresent -ne $true) { $failures += "save-draft-reconnect-pass invalid" }
$ae = Get-Json "testdata/prod004/scenarios/ar-en-rtl-ltr-pass.json"
if ($ae.expectedValidate -ne "PASS" -or $ae.arabicEnglish -ne $true -or $ae.rtlLtr -ne $true) { $failures += "ar-en-rtl-ltr-pass invalid" }
$oe = Get-Json "testdata/prod004/scenarios/offline-encrypted-limited-pass.json"
if ($oe.expectedValidate -ne "PASS" -or $oe.encrypted -ne $true) { $failures += "offline-encrypted-limited-pass invalid" }
$wa = Get-Json "testdata/prod004/scenarios/wcag-audit-pass.json"
if ($wa.expectedValidate -ne "PASS" -or $wa.wcag22Aa -ne $true) { $failures += "wcag-audit-pass invalid" }

$audit = Get-Json "testdata/prod004/audit/branch-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "branch-audit-pass invalid" }
$ledger = Get-Json "testdata/prod004/harness/branch-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "branch-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveRemoteBranch -eq $true) { $failures += "ledger run $($r.runId) liveRemoteBranch must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-004"
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

Write-Host "PASS: PROD-004 remote-branch pack validate"
exit 0
