# MIG-004: validate identity match/merge matrix and harness fixtures.
# Usage:
#   powershell -File scripts/mig004-match-validate.ps1

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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "MIG-004") { $failures += "card must be MIG-004" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("ThresholdSignOff", "NoHighRiskAutoMerge", "ReversibleMerge", "ReviewPrivacy", "LabelledMetrics", "AdversarialIdentity")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$strategyIds = @($m.matchStrategies | ForEach-Object { $_.id })
foreach ($need in @("DeterministicNationalId", "DeterministicComposite", "ProbabilisticNameDob", "ExceptionQueue")) {
    if ($strategyIds -notcontains $need) { $failures += "missing strategy $need" }
}

# Thresholds
$t = $m.thresholds
if ($t.autoMergeMaxRisk -ne "Low") { $failures += "autoMergeMaxRisk must be Low" }
if (-not $t.highRiskAutoMergeForbidden) { $failures += "highRiskAutoMergeForbidden must be true" }
if ([double]$t.precisionTarget -lt 0.9) { $failures += "precisionTarget too low" }
if ([double]$t.recallTarget -lt 0.85) { $failures += "recallTarget too low" }

# National ID exceptions
$excCodes = @($m.nationalIdExceptions | ForEach-Object { $_.code })
foreach ($need in @("missing", "invalid-checksum", "twin-shared", "foreign-id")) {
    if ($excCodes -notcontains $need) { $failures += "missing nationalId exception $need" }
}
foreach ($e in $m.nationalIdExceptions) {
    if (-not $e.autoMergeForbidden) { $failures += "exception $($e.code) must forbid auto-merge" }
}

# Merge audit + privacy
$auditFields = @($m.mergeAudit.requiredFields)
foreach ($need in @("mergedAt", "actor", "evidenceRef", "survivorId", "sourceId", "reversible")) {
    if ($auditFields -notcontains $need) { $failures += "mergeAudit missing $need" }
}
if (-not $m.mergeAudit.reversibleRequired) { $failures += "reversibleRequired must be true" }
if (-not $m.reviewPrivacy.tenantScopedReview) { $failures += "tenantScopedReview must be true" }
if (-not $m.reviewPrivacy.forbidCrossAgencyPii) { $failures += "forbidCrossAgencyPii must be true" }

# Signing
$sig = $m.signing
if ([string]::IsNullOrWhiteSpace($sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]::IsNullOrWhiteSpace($sig.matchPolicyVersion)) { $failures += "signing matchPolicyVersion required" }
if ($sig.signedByRole -eq $sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

# Fraud indicators
if (@($m.fraudIndicators).Count -lt 1) { $failures += "need >= 1 fraud indicator" }
foreach ($f in $m.fraudIndicators) {
    if (-not $f.forcesReview) { $failures += "fraud $($f.id) must force review" }
}

# Blockers
$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["MIG-002"] -ne "Satisfied") { $failures += "MIG-002 must be Satisfied" }
if ($blockerMap["DATA-002"] -ne "Satisfied") { $failures += "DATA-002 must be Satisfied" }
if ($blockerMap["GOV-001"] -ne "Satisfied") { $failures += "GOV-001 must be Satisfied" }
if ($blockerMap["GOV-003-merge"] -ne "Satisfied") { $failures += "GOV-003-merge must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["L10N-002"] -ne "Done") { $failures += "L10N-002 must be Done" }
if ($blockerMap["live-labelled-metrics"] -ne "Blocked") { $failures += "live-labelled-metrics must be Blocked" }

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

$sign = Assert-FixtureExpected $m.simulationPaths.thresholdSignoff "PASS"
$high = Assert-FixtureExpected $m.simulationPaths.highRiskAutoMergeFail "FAIL"
$round = Assert-FixtureExpected $m.simulationPaths.mergeUnmergeRoundtrip "PASS"
$xtenant = Assert-FixtureExpected $m.simulationPaths.crossTenantReviewFail "FAIL"
$pairs = Assert-FixtureExpected $m.simulationPaths.identityPairs $null
$metrics = Assert-FixtureExpected $m.simulationPaths.precisionRecallLab "PASS"
$adv = Assert-FixtureExpected $m.simulationPaths.twinsNameVariants "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.matchDecisionLedger $null

if ($null -ne $sign) {
    if ($sign.signedByRole -eq $sign.reviewerRole) {
        $failures += "signoff roles must differ"
    }
    if ($sign.contentHash -ne $sig.contentHash) {
        $failures += "signoff contentHash must match matrix signing"
    }
    if ($sign.matchPolicyVersion -ne $sig.matchPolicyVersion) {
        $failures += "signoff version must match matrix"
    }
}

if ($null -ne $high) {
    if ($high.proposedOutcome -eq "AutoMerge" -and $high.riskLevel -eq "High") {
        # expected FAIL fixture — OK if expectedValidate is FAIL
        if ($high.expectedValidate -ne "FAIL") {
            $failures += "high-risk auto-merge must be FAIL fixture"
        }
        if ($null -ne $high.evidenceRef -and -not [string]::IsNullOrWhiteSpace([string]$high.evidenceRef)) {
            # still FAIL because high risk; evidence alone does not allow auto in this pack policy for High
        }
    }
    else {
        $failures += "high-risk fixture must propose AutoMerge at High risk"
    }
}

if ($null -ne $round) {
    $merge = $round.merge
    foreach ($f in $auditFields) {
        if ($null -eq $merge.$f -or ($merge.$f -is [string] -and [string]::IsNullOrWhiteSpace($merge.$f) -and $f -ne "reversible")) {
            # reversible is bool
            if ($f -eq "reversible") {
                if ($merge.reversible -ne $true) { $failures += "merge reversible must be true" }
            }
            else {
                $failures += "roundtrip merge missing $f"
            }
        }
    }
    if ($merge.reversible -ne $true) { $failures += "merge reversible must be true" }
    if ($round.unmerge.sourceRestored -ne $true) { $failures += "unmerge must restore source" }
}

if ($null -ne $xtenant) {
    if ($xtenant.reviewerTenantId -eq $xtenant.payloadTenantId) {
        $failures += "cross-tenant fixture must use different tenants"
    }
    if ($xtenant.plaintextPiiExposed -ne $true) {
        $failures += "cross-tenant FAIL fixture must declare plaintext exposure"
    }
    if ($xtenant.expectedValidate -ne "FAIL") {
        $failures += "cross-tenant must be FAIL"
    }
}

if ($null -ne $pairs) {
    $count = @($pairs.pairs).Count
    if ($count -lt 20) { $failures += "identity-pairs need >= 20 pairs (have $count)" }
    $labels = @($pairs.pairs | ForEach-Object { $_.label }) | Select-Object -Unique
    if ($labels -notcontains "match" -or $labels -notcontains "non-match") {
        $failures += "identity-pairs need both match and non-match labels"
    }
}

if ($null -ne $metrics) {
    if ([double]$metrics.precision -lt [double]$t.precisionTarget) {
        $failures += "lab precision below target"
    }
    if ([double]$metrics.recall -lt [double]$t.recallTarget) {
        $failures += "lab recall below target"
    }
    if ($metrics.liveAgencyEval -eq $true) {
        $failures += "liveAgencyEval must be false while Blocked"
    }
}

if ($null -ne $adv) {
    if ($adv.anyAutoMerge -eq $true) {
        $failures += "adversarial twins/variants must not auto-merge"
    }
    foreach ($c in $adv.cases) {
        if ($c.autoMergeAttempted -eq $true -or $c.proposedOutcome -eq "AutoMerge") {
            $failures += "adversarial case $($c.id) must not AutoMerge"
        }
    }
}

if ($null -ne $ledger -and @($ledger.decisions).Count -lt 1) {
    $failures += "match decision ledger empty"
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-004"
    ok = ($failures.Count -eq 0)
    strategyCount = @($m.matchStrategies).Count
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

Write-Host "PASS: MIG-004 match validate"
exit 0
