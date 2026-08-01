# MIG-004: runtime/production match binding validate.
# Usage:
#   powershell -File scripts/mig004-match-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-match/match-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig004/out"
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

if ($m.card -ne "MIG-004") { $failures += "card must be MIG-004" }

foreach ($need in @(
    @{ Id = "MIG-002"; Status = "Satisfied" },
    @{ Id = "DATA-002"; Status = "Satisfied" },
    @{ Id = "GOV-001"; Status = "Satisfied" },
    @{ Id = "GOV-003-merge"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "L10N-002"; Status = "Done" },
    @{ Id = "live-labelled-metrics"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$requiredStrategies = @("DeterministicNationalId", "DeterministicComposite", "ProbabilisticNameDob", "ExceptionQueue")
$strategyIds = @($m.matchStrategies | ForEach-Object { $_.id })
if ($strategyIds.Count -lt 4) { $failures += "need >= 4 matchStrategies (got $($strategyIds.Count))" }
foreach ($need in $requiredStrategies) {
    if ($strategyIds -notcontains $need) { $failures += "missing strategy $need" }
}

$t = $m.thresholds
if ($null -eq $t) {
    $failures += "thresholds missing"
}
else {
    if ($t.highRiskAutoMergeForbidden -ne $true) { $failures += "highRiskAutoMergeForbidden must be true" }
    if ([string]$t.autoMergeMaxRisk -ne "Low") { $failures += "autoMergeMaxRisk must be Low" }
    if ([double]$t.precisionTarget -lt 0.9) { $failures += "precisionTarget must be >= 0.9" }
    if ([double]$t.recallTarget -lt 0.85) { $failures += "recallTarget must be >= 0.85" }
}

$nid = @($m.nationalIdExceptions)
if ($nid.Count -lt 1) { $failures += "nationalIdExceptions empty" }
foreach ($ex in $nid) {
    if ($ex.autoMergeForbidden -ne $true) {
        $failures += "nationalIdException $($ex.code): autoMergeForbidden must be true"
    }
}

$ma = $m.mergeAudit
if ($null -eq $ma) {
    $failures += "mergeAudit missing"
}
else {
    if ($ma.reversibleRequired -ne $true) { $failures += "reversibleRequired must be true" }
    foreach ($need in @("mergedAt", "actor", "evidenceRef", "survivorId", "sourceId", "reversible")) {
        if (@($ma.requiredFields) -notcontains $need) { $failures += "mergeAudit missing field $need" }
    }
}

$rp = $m.reviewPrivacy
if ($null -eq $rp) {
    $failures += "reviewPrivacy missing"
}
else {
    if ($rp.tenantScopedReview -ne $true) { $failures += "tenantScopedReview must be true" }
    if ($rp.forbidCrossAgencyPii -ne $true) { $failures += "forbidCrossAgencyPii must be true" }
    if ([string]::IsNullOrWhiteSpace([string]$rp.maskingMode)) { $failures += "maskingMode missing" }
}

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
    $version = $sig.matchPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.matchPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$fraud = @($m.fraudIndicators)
if ($fraud.Count -lt 3) { $failures += "need >= 3 fraudIndicators (got $($fraud.Count))" }
foreach ($f in $fraud) {
    if ($f.forcesReview -ne $true) { $failures += "fraudIndicator $($f.id): forcesReview must be true" }
}

$fp = $m.falsePositiveControls
if ($null -eq $fp -or $fp.requireEvidenceForAutoMerge -ne $true) {
    $failures += "requireEvidenceForAutoMerge must be true"
}

# Negative fixtures
$highRisk = Get-Json "testdata/mig004/scenarios/high-risk-auto-merge-fail.json"
$crossTenant = Get-Json "testdata/mig004/scenarios/cross-tenant-review-fail.json"

if ($highRisk.expectedValidate -ne "FAIL") { $failures += "high-risk-auto-merge fixture expectedValidate must be FAIL" }
if ([string]$highRisk.proposedOutcome -ne "AutoMerge" -or [string]$highRisk.riskLevel -ne "High") {
    $failures += "high-risk fixture must propose AutoMerge at High risk"
}

if ($crossTenant.expectedValidate -ne "FAIL") { $failures += "cross-tenant-review fixture expectedValidate must be FAIL" }
if ($crossTenant.plaintextPiiExposed -ne $true) {
    $failures += "cross-tenant fixture must expose plaintext PII as FAIL case"
}

# Positive fixtures
$signoff = Get-Json "testdata/mig004/signoff/threshold-signoff.json"
$roundtrip = Get-Json "testdata/mig004/scenarios/merge-unmerge-roundtrip.json"
$pairs = Get-Json "testdata/mig004/labelled/identity-pairs.json"
$metrics = Get-Json "testdata/mig004/metrics/precision-recall-lab.json"
$twins = Get-Json "testdata/mig004/adversarial/twins-name-variants.json"
$ledger = Get-Json "testdata/mig004/harness/match-decision-ledger.json"

if ($signoff.expectedValidate -ne "PASS") { $failures += "threshold-signoff expectedValidate must be PASS" }
if ([string]::IsNullOrWhiteSpace([string]$signoff.contentHash)) { $failures += "threshold-signoff missing contentHash" }

if ($roundtrip.expectedValidate -ne "PASS") { $failures += "merge-unmerge-roundtrip expectedValidate must be PASS" }
if ($roundtrip.merge.reversible -ne $true) { $failures += "merge-unmerge roundtrip merge must be reversible" }

if (@($pairs.pairs).Count -lt 20) { $failures += "identity-pairs need >= 20 pairs" }

if ($metrics.expectedValidate -ne "PASS") { $failures += "precision-recall-lab expectedValidate must be PASS" }
if ($metrics.liveAgencyEval -eq $true) { $failures += "precision-recall liveAgencyEval must be false" }
if ([double]$metrics.precision -lt [double]$t.precisionTarget) {
    $failures += "lab precision below matrix precisionTarget"
}
if ([double]$metrics.recall -lt [double]$t.recallTarget) {
    $failures += "lab recall below matrix recallTarget"
}

if ($twins.expectedValidate -ne "PASS") { $failures += "twins-name-variants expectedValidate must be PASS" }
if ($twins.anyAutoMerge -eq $true) { $failures += "adversarial twins must not AutoMerge" }

if (@($ledger.decisions).Count -lt 1) { $failures += "match-decision-ledger must be non-empty" }

$prodFiles = @(
    "src/Gateway/HRManagement.Gateway/appsettings.Production.json",
    "src/Gateway/HRManagement.Gateway/appsettings.Staging.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Production.json",
    "src/Services/Payroll/Payroll.Web/appsettings.Staging.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Production.json",
    "src/Services/HRCore/HR.Core.Web/appsettings.Staging.json"
)

foreach ($rel in $prodFiles) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $mm = $cfg.MigMatch
    if ($null -eq $mm) { $failures += "$rel missing MigMatch"; continue }
    if ($mm.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($mm.LiveIdentityMatchClaim -eq $true) { $failures += "$rel LiveIdentityMatchClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-004"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    strategyCount = $strategyIds.Count
    fraudCount = $fraud.Count
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

Write-Host "PASS: MIG-004 match runtime validate"
exit 0
