# EXT-002: validate civil-registry contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/ext002-civil-registry-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-civil/civil-registry-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext002/out"
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

if ($m.card -ne "EXT-002") { $failures += "card must be EXT-002" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("PurposeConsent", "MinimalData", "MismatchHuman", "UnavailableMode", "RateLimitAbuse", "AuditTrail", "StaleCorrection")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if (-not $m.consentRequired) { $failures += "consentRequired must be true" }
if (@($m.purposeAllowlist).Count -lt 3) { $failures += "purposeAllowlist too short" }

$req = @($m.minimalQuery.allowedRequestFields)
$resp = @($m.minimalQuery.allowedResponseFields)
foreach ($need in @("nationalId", "purposeCode", "consentRef", "requesterTenantId")) {
    if ($req -notcontains $need) { $failures += "request field missing $need" }
}
foreach ($need in @("matchStatus", "opaqueSubject", "assuranceHint", "queriedAt")) {
    if ($resp -notcontains $need) { $failures += "response field missing $need" }
}

$outcomes = @($m.matchOutcomes)
foreach ($need in @("Match", "Mismatch", "Unavailable", "Error")) {
    if ($outcomes -notcontains $need) { $failures += "missing match outcome $need" }
}

if (-not $m.mismatchHumanWorkflow.required) { $failures += "mismatchHumanWorkflow.required" }
if (-not $m.caching.forbidStaleAsTruth) { $failures += "forbidStaleAsTruth must be true" }
if (-not $m.corrections.workflowRequired) { $failures += "corrections.workflowRequired" }
if (-not $m.unavailableMode.forbidUnverifiedAsTruth) { $failures += "forbidUnverifiedAsTruth" }
if (-not $m.rateLimits.bulkHarvestForbidden) { $failures += "bulkHarvestForbidden" }
if ([int]$m.rateLimits.maxQueriesPerMinute -le 0) { $failures += "maxQueriesPerMinute invalid" }
if (-not $m.audit.required) { $failures += "audit.required" }
$auditFields = @($m.audit.requiredFields)
foreach ($need in @("queryId", "purposeCode", "actor", "tenantId", "outcome", "queriedAt")) {
    if ($auditFields -notcontains $need) { $failures += "audit missing $need" }
}
if (-not $m.mockSandbox.labStub -or -not $m.mockSandbox.liveMoiForbidden) {
    $failures += "mockSandbox must be labStub and liveMoiForbidden"
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
if ($blockerMap["EXT-001"] -ne "Satisfied") { $failures += "EXT-001 must be Satisfied" }
if ($blockerMap["GOV-001"] -ne "Satisfied") { $failures += "GOV-001 must be Satisfied" }
if ($blockerMap["MIG-004"] -ne "Satisfied") { $failures += "MIG-004 must be Satisfied" }
if ($blockerMap["moi-civil-registry-live"] -ne "Blocked") { $failures += "moi-civil-registry-live must be Blocked" }
if ($blockerMap["live-sandbox-cert"] -ne "Blocked") { $failures += "live-sandbox-cert must be Blocked" }
if ($blockerMap["L10N-002"] -ne "Done") { $failures += "L10N-002 must be Done" }
if ($blockerMap["national-idp-live"] -ne "Blocked") { $failures += "national-idp-live must be Blocked" }

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

$purp = Assert-FixtureExpected $m.simulationPaths.purposeMismatchFail "FAIL"
$excess = Assert-FixtureExpected $m.simulationPaths.excessFieldStoreFail "FAIL"
$mis = Assert-FixtureExpected $m.simulationPaths.mismatchReviewPass "PASS"
$outT = Assert-FixtureExpected $m.simulationPaths.outageAsTruthFail "FAIL"
$unav = Assert-FixtureExpected $m.simulationPaths.unavailableDegradePass "PASS"
$bulk = Assert-FixtureExpected $m.simulationPaths.bulkHarvestFail "FAIL"
$stale = Assert-FixtureExpected $m.simulationPaths.staleAsTruthFail "FAIL"
$corr = Assert-FixtureExpected $m.simulationPaths.correctionWorkflowPass "PASS"
$audit = Assert-FixtureExpected $m.simulationPaths.queryAuditPass "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.civilRegistryRunLedger $null

if ($null -ne $purp) {
    if (@($m.purposeAllowlist) -contains $purp.purposeCode) {
        $failures += "purpose FAIL fixture must use non-allowlisted purpose"
    }
}

if ($null -ne $excess) {
    $bad = @($excess.forbiddenStored)
    if ($bad.Count -lt 1) { $failures += "excess FAIL needs forbiddenStored" }
    foreach ($f in $bad) {
        if ($resp -contains $f) { $failures += "forbiddenStored $f is allowlisted" }
    }
}

if ($null -ne $mis) {
    if ($mis.humanReviewQueued -ne $true -or $mis.autoAcceptedAsMatch -eq $true) {
        $failures += "mismatch review incomplete"
    }
}

if ($null -ne $outT) {
    if ($outT.treatedAsVerifiedMatch -ne $true) { $failures += "outage-as-truth FAIL must treat as verified" }
}
if ($null -ne $unav) {
    if ($unav.treatedAsVerifiedMatch -eq $true) { $failures += "unavailable degrade must not treat as verified" }
    if ($unav.manualHold -ne $true) { $failures += "unavailable degrade needs manualHold" }
}

if ($null -ne $bulk) {
    if ($bulk.bulkHarvestAttempted -ne $true) { $failures += "bulk FAIL must attempt harvest" }
    if ([int]$bulk.queriesPerMinute -le [int]$m.rateLimits.maxQueriesPerMinute) {
        $failures += "bulk FAIL queriesPerMinute must exceed limit"
    }
}

if ($null -ne $stale) {
    if ([int]$stale.cacheAgeSeconds -le [int]$m.caching.ttlSeconds) {
        $failures += "stale FAIL age must exceed ttl"
    }
    if ($stale.treatedAsVerifiedMatch -ne $true) { $failures += "stale FAIL must treat as verified" }
}

if ($null -ne $corr) {
    if ($corr.correctionOpened -ne $true -or $corr.silentOverwrite -eq $true) {
        $failures += "correction workflow incomplete"
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
        if (@($m.purposeAllowlist) -notcontains $e.purposeCode) {
            $failures += "audit purpose $($e.purposeCode) not allowlisted"
        }
    }
}

if ($null -ne $ledger) {
    if (@($ledger.runs).Count -lt 1) { $failures += "civil registry ledger empty" }
    foreach ($r in $ledger.runs) {
        if ($r.liveMoi -eq $true) { $failures += "ledger run must not be liveMoi" }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-002"
    ok = ($failures.Count -eq 0)
    purposeCount = @($m.purposeAllowlist).Count
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

Write-Host "PASS: EXT-002 civil-registry validate"
exit 0
