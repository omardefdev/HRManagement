# MIG-003: runtime/production factory binding validate.
# Usage:
#   powershell -File scripts/mig003-factory-runtime-validate.ps1

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

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "MIG-003") { $failures += "card must be MIG-003" }

foreach ($need in @(
    @{ Id = "MIG-002"; Status = "Satisfied" },
    @{ Id = "TEN-002"; Status = "Satisfied" },
    @{ Id = "OPS-005"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "live-cutover-perf"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$requiredStages = @("extract", "validate", "transform", "quarantine", "load", "purge")
$stageIds = @($m.stages | ForEach-Object { $_.id })
if ($stageIds.Count -lt 6) { $failures += "need >= 6 stages (got $($stageIds.Count))" }
foreach ($need in $requiredStages) {
    if ($stageIds -notcontains $need) { $failures += "missing stage $need" }
}

$zones = @($m.stagingZones)
if ($zones.Count -lt 3) { $failures += "need >= 3 stagingZones (got $($zones.Count))" }
foreach ($z in $zones) {
    if ($z.encryptionAtRest -ne $true) { $failures += "staging zone $($z.id): encryptionAtRest must be true" }
    if ($z.tenantScoped -ne $true) { $failures += "staging zone $($z.id): tenantScoped must be true" }
    if ([string]::IsNullOrWhiteSpace([string]$z.purgePolicy)) { $failures += "staging zone $($z.id): purgePolicy missing" }
    if ([string]::IsNullOrWhiteSpace([string]$z.accessRole)) { $failures += "staging zone $($z.id): accessRole missing" }
}

$idemp = $m.idempotency
if ($null -eq $idemp) {
    $failures += "idempotency missing"
}
else {
    if ($idemp.duplicateOnReloadForbidden -ne $true) { $failures += "duplicateOnReloadForbidden must be true" }
    if ([string]$idemp.reloadPolicy -ne "skip-identical") { $failures += "reloadPolicy must be skip-identical" }
    if (@($idemp.batchKeyFields).Count -lt 1) { $failures += "batchKeyFields empty" }
    if (@($idemp.naturalKeyFields).Count -lt 1) { $failures += "naturalKeyFields empty" }
}

$cp = $m.checkpoint
if ($null -eq $cp) {
    $failures += "checkpoint missing"
}
else {
    if ($cp.resumeFromStage -ne $true) { $failures += "resumeFromStage must be true" }
    if ([string]::IsNullOrWhiteSpace([string]$cp.midFailBehavior)) { $failures += "midFailBehavior missing" }
}

$lineageFields = @($m.lineage.perRecordFields)
foreach ($need in @("sourceId", "sourceRecordKey", "mappingVersion", "disposition", "loadBatchId", "loadedAt", "tenantId")) {
    if ($lineageFields -notcontains $need) { $failures += "lineage missing field $need" }
}

$pii = $m.piiLogging
if ($null -eq $pii) {
    $failures += "piiLogging missing"
}
else {
    if (@($pii.forbiddenFields).Count -lt 1) { $failures += "piiLogging forbiddenFields empty" }
    foreach ($need in @("NationalId", "FullName")) {
        if (@($pii.forbiddenFields) -notcontains $need) { $failures += "piiLogging forbiddenFields missing $need" }
    }
    if ([string]::IsNullOrWhiteSpace([string]$pii.redactionMode)) { $failures += "piiLogging redactionMode missing" }
}

$tp = $m.throughput
if ($null -eq $tp) {
    $failures += "throughput missing"
}
else {
    if ([double]$tp.cutoverWindowHours -le 0) { $failures += "cutoverWindowHours must be > 0" }
    if ([double]$tp.targetRowsPerHour -le 0) { $failures += "targetRowsPerHour must be > 0" }
    if ($tp.liveProofRequired -eq $true) {
        $failures += "liveProofRequired must be false while live-cutover-perf is Blocked"
    }
}

$pipes = @($m.pipelines)
if ($pipes.Count -lt 1) { $failures += "need >= 1 pipeline" }
foreach ($p in $pipes) {
    if ([string]::IsNullOrWhiteSpace([string]$p.codeMapVersionRef)) {
        $failures += "pipeline $($p.id): codeMapVersionRef missing"
    }
    $pStages = @($p.stageIds)
    foreach ($need in $requiredStages) {
        if ($pStages -notcontains $need) { $failures += "pipeline $($p.id): missing stageId $need" }
    }
}

# Negative fixture
$corrupt = Get-Json "testdata/mig003/scenarios/corrupt-schema-fail.json"
if ($corrupt.expectedValidate -ne "FAIL") { $failures += "corrupt-schema fixture expectedValidate must be FAIL" }
if ($corrupt.quarantineCreated -ne $true) { $failures += "corrupt-schema fixture must create quarantine" }
if ($corrupt.silentPartialCommit -eq $true) { $failures += "corrupt-schema fixture must not silentPartialCommit" }

# Positive fixtures
$reload = Get-Json "testdata/mig003/scenarios/same-batch-reload.json"
$midFail = Get-Json "testdata/mig003/scenarios/mid-stage-fail-resume.json"
$budget = Get-Json "testdata/mig003/perf/cutover-window-budget.json"
$secure = Get-Json "testdata/mig003/review/secure-staging-checklist.json"
$lineage = Get-Json "testdata/mig003/harness/lineage-sample.json"
$redact = Get-Json "testdata/mig003/harness/log-redaction-sample.json"
$ledger = Get-Json "testdata/mig003/harness/pipeline-run-ledger.json"

if ($reload.expectedValidate -ne "PASS") { $failures += "same-batch-reload expectedValidate must be PASS" }
if ([int]$reload.duplicatesCreated -ne 0) { $failures += "same-batch-reload duplicatesCreated must be 0" }

if ($midFail.expectedValidate -ne "PASS") { $failures += "mid-stage-fail-resume expectedValidate must be PASS" }
if ($midFail.reExtractedCompletedStages -eq $true) {
    $failures += "mid-stage-fail-resume must not re-extract completed stages"
}

if ($budget.expectedValidate -ne "PASS") { $failures += "cutover-window-budget expectedValidate must be PASS" }
if ($budget.liveProof -eq $true) { $failures += "cutover budget liveProof must be false" }
if ($budget.fitsWindow -ne $true) { $failures += "cutover budget must fit window" }

if ($secure.expectedValidate -ne "PASS") { $failures += "secure-staging expectedValidate must be PASS" }
if ($secure.allZonesEncrypted -ne $true) { $failures += "secure-staging allZonesEncrypted must be true" }

if (@($lineage.records).Count -lt 5) { $failures += "lineage-sample needs >= 5 records" }

if ($redact.expectedValidate -ne "PASS") { $failures += "log-redaction expectedValidate must be PASS" }
if ($redact.plaintextForbiddenAbsent -ne $true) {
    $failures += "log-redaction plaintextForbiddenAbsent must be true"
}

if (@($ledger.runs).Count -lt 1) { $failures += "pipeline-run-ledger must be non-empty" }

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
    $mf = $cfg.MigFactory
    if ($null -eq $mf) { $failures += "$rel missing MigFactory"; continue }
    if ($mf.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($mf.LiveEtlWorkersClaim -eq $true) { $failures += "$rel LiveEtlWorkersClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-003"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    pipelineCount = $pipes.Count
    zoneCount = $zones.Count
    stageCount = $stageIds.Count
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

Write-Host "PASS: MIG-003 factory runtime validate"
exit 0
