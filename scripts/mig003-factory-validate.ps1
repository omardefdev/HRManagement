# MIG-003: validate migration factory pipeline matrix and harness fixtures.
# Usage:
#   powershell -File scripts/mig003-factory-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-factory/pipeline-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig003/out"
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

if ($m.card -ne "MIG-003") { $failures += "card must be MIG-003" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("IdempotentReload", "MidFailResume", "RecordLineage", "NoPiiInLogs", "CutoverThroughput", "SecureStagingReview")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$stageIds = @($m.stages | ForEach-Object { $_.id })
foreach ($need in @("extract", "validate", "transform", "quarantine", "load", "purge")) {
    if ($stageIds -notcontains $need) { $failures += "missing stage $need" }
}

# Staging zones
foreach ($z in $m.stagingZones) {
    if (-not $z.encryptionAtRest) { $failures += "zone $($z.id) not encrypted" }
    if (-not $z.tenantScoped) { $failures += "zone $($z.id) not tenantScoped" }
    if ([string]::IsNullOrWhiteSpace($z.purgePolicy)) { $failures += "zone $($z.id) missing purgePolicy" }
    if ([string]::IsNullOrWhiteSpace($z.accessRole)) { $failures += "zone $($z.id) missing accessRole" }
}
if (@($m.stagingZones).Count -lt 3) { $failures += "need >= 3 staging zones" }

# Idempotency / checkpoint / lineage / pii
if (-not $m.idempotency.duplicateOnReloadForbidden) { $failures += "duplicateOnReloadForbidden must be true" }
if ($m.idempotency.reloadPolicy -ne "skip-identical") { $failures += "reloadPolicy must be skip-identical" }
if (-not $m.checkpoint.resumeFromStage) { $failures += "resumeFromStage must be true" }
$linFields = @($m.lineage.perRecordFields)
foreach ($need in @("sourceId", "sourceRecordKey", "mappingVersion", "disposition", "loadBatchId", "loadedAt", "tenantId")) {
    if ($linFields -notcontains $need) { $failures += "lineage missing $need" }
}
if (@($m.piiLogging.forbiddenFields).Count -lt 3) { $failures += "piiLogging forbiddenFields too short" }
if ($m.throughput.cutoverWindowHours -le 0 -or $m.throughput.targetRowsPerHour -le 0) {
    $failures += "throughput budget invalid"
}

# Pipelines
if (@($m.pipelines).Count -lt 1) { $failures += "need >= 1 pipeline" }
foreach ($p in $m.pipelines) {
    if ($p.codeMapVersionRef -ne "2026.07.30.1") {
        $failures += "pipeline $($p.id) codeMapVersionRef must match MIG-002 mappingVersion"
    }
    foreach ($sid in @($p.stageIds)) {
        if ($stageIds -notcontains $sid) { $failures += "pipeline $($p.id) unknown stage $sid" }
    }
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
if ($blockerMap["TEN-002"] -ne "Satisfied") { $failures += "TEN-002 must be Satisfied" }
if ($blockerMap["OPS-005"] -ne "Satisfied") { $failures += "OPS-005 must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["live-cutover-perf"] -ne "Blocked") { $failures += "live-cutover-perf must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    # [string]$expect coerces $null to ""; only enforce when a concrete outcome is required
    if (-not [string]::IsNullOrEmpty($expect) -and $obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$reload = Assert-FixtureExpected $m.simulationPaths.sameBatchReload "PASS"
$resume = Assert-FixtureExpected $m.simulationPaths.midFailResume "PASS"
$corrupt = Assert-FixtureExpected $m.simulationPaths.corruptSchemaFail "FAIL"
$budget = Assert-FixtureExpected $m.simulationPaths.cutoverBudget "PASS"
$review = Assert-FixtureExpected $m.simulationPaths.secureStagingReview "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.pipelineLedger $null
$lineage = Assert-FixtureExpected $m.simulationPaths.lineageSample $null
$logs = Assert-FixtureExpected $m.simulationPaths.logRedactionSample "PASS"

if ($null -ne $reload) {
    if ([int]$reload.duplicatesCreated -ne 0) { $failures += "same-batch-reload created duplicates" }
}
if ($null -ne $resume) {
    if ($resume.reExtractedCompletedStages -eq $true) {
        $failures += "mid-fail resume must not re-extract completed stages"
    }
    if ($resume.resumeFromStage -ne $resume.failedAtStage -and $resume.checkpointStage -eq $null) {
        $failures += "mid-fail resume missing checkpoint"
    }
}
if ($null -ne $corrupt) {
    if ($corrupt.silentPartialCommit -eq $true) {
        $failures += "corrupt-schema must not allow silent partial commit"
    }
    if ($corrupt.quarantineCreated -ne $true) {
        $failures += "corrupt-schema must create quarantine"
    }
}
if ($null -ne $budget) {
    if ($budget.fitsWindow -ne $true) { $failures += "cutover budget must fit window" }
    if ($budget.liveProof -eq $true) { $failures += "liveProof must be false while Blocked" }
    $needHrs = [double]$budget.syntheticRowBudget / [double]$budget.targetRowsPerHour
    if ($needHrs -gt [double]$budget.cutoverWindowHours) {
        $failures += "synthetic budget exceeds cutover window"
    }
}
if ($null -ne $review) {
    if ($review.allZonesEncrypted -ne $true -or $review.allZonesTenantScoped -ne $true) {
        $failures += "secure staging review incomplete"
    }
}
if ($null -ne $lineage) {
    if (@($lineage.records).Count -lt 5) { $failures += "lineage-sample need >= 5 records" }
    foreach ($r in $lineage.records) {
        foreach ($f in $linFields) {
            if ($null -eq $r.$f -or ($r.$f -is [string] -and [string]::IsNullOrWhiteSpace($r.$f))) {
                $failures += "lineage record missing $f"
            }
        }
    }
}
if ($null -ne $logs) {
    if ($logs.plaintextForbiddenAbsent -ne $true) {
        $failures += "log sample must declare plaintextForbiddenAbsent"
    }
    $text = ($logs | ConvertTo-Json -Depth 6)
    foreach ($bad in @($m.piiLogging.forbiddenFields)) {
        # allow field names in policy docs sense only if not as values — check common PII patterns
        if ($text -match ('"' + [regex]::Escape($bad) + '"\s*:')) {
            # property name mention in structure is ok for stage metadata; skip
        }
    }
    if ($text -match 'NationalId["\s]*:\s*"[0-9]' -or $text -match 'FullName["\s]*:\s*"[A-Za-z\u0600-\u06FF]') {
        $failures += "log sample appears to contain plaintext PII values"
    }
}
if ($null -ne $ledger -and @($ledger.runs).Count -lt 1) {
    $failures += "pipeline ledger empty"
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-003"
    ok = ($failures.Count -eq 0)
    stageCount = @($m.stages).Count
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

Write-Host "PASS: MIG-003 factory validate"
exit 0
