# EXT-002: runtime/production civil-registry binding validate.
# Usage:
#   powershell -File scripts/ext002-civil-registry-runtime-validate.ps1

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

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "EXT-002") { $failures += "card must be EXT-002" }

foreach ($need in @(
    @{ Id = "EXT-001"; Status = "Satisfied" },
    @{ Id = "GOV-001"; Status = "Satisfied" },
    @{ Id = "MIG-004"; Status = "Satisfied" },
    @{ Id = "moi-civil-registry-live"; Status = "Blocked" },
    @{ Id = "live-sandbox-cert"; Status = "Blocked" },
    @{ Id = "L10N-002"; Status = "Done" },
    @{ Id = "national-idp-live"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.consentRequired -ne $true) { $failures += "consentRequired must be true" }
$purposes = @($m.purposeAllowlist)
if ($purposes.Count -lt 3) { $failures += "purposeAllowlist too short" }
foreach ($need in @("hire-identity-proof", "payroll-beneficiary-verify", "migration-nid-exception", "dsar-identity-confirm")) {
    if ($purposes -notcontains $need) { $failures += "purposeAllowlist missing $need" }
}

$req = @($m.minimalQuery.allowedRequestFields)
$resp = @($m.minimalQuery.allowedResponseFields)
foreach ($need in @("nationalId", "purposeCode", "consentRef", "requesterTenantId")) {
    if ($req -notcontains $need) { $failures += "request field missing $need" }
}
foreach ($need in @("matchStatus", "opaqueSubject", "assuranceHint", "queriedAt")) {
    if ($resp -notcontains $need) { $failures += "response field missing $need" }
}

foreach ($need in @("Match", "Mismatch", "Unavailable", "Error")) {
    if (@($m.matchOutcomes) -notcontains $need) { $failures += "missing match outcome $need" }
}

if ($m.mismatchHumanWorkflow.required -ne $true) { $failures += "mismatchHumanWorkflow.required must be true" }
if ($m.caching.forbidStaleAsTruth -ne $true) { $failures += "forbidStaleAsTruth must be true" }
if ([int]$m.caching.ttlSeconds -le 0) { $failures += "ttlSeconds must be > 0" }
if ($m.corrections.workflowRequired -ne $true) { $failures += "corrections.workflowRequired must be true" }
if ($m.unavailableMode.forbidUnverifiedAsTruth -ne $true) { $failures += "forbidUnverifiedAsTruth must be true" }
if ($m.rateLimits.bulkHarvestForbidden -ne $true) { $failures += "bulkHarvestForbidden must be true" }
if ([int]$m.rateLimits.maxQueriesPerMinute -le 0) { $failures += "maxQueriesPerMinute must be > 0" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("queryId", "purposeCode", "actor", "tenantId", "outcome", "queriedAt")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "mockSandbox.labStub must be true" }
if ($m.mockSandbox.liveMoiForbidden -ne $true) { $failures += "liveMoiForbidden must be true" }

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
    $version = $sig.civilRegistryPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.civilRegistryPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$purposeFail = Get-Json "testdata/ext002/scenarios/purpose-mismatch-fail.json"
$excessFail = Get-Json "testdata/ext002/scenarios/excess-field-store-fail.json"
$outageFail = Get-Json "testdata/ext002/scenarios/outage-as-truth-fail.json"
$bulkFail = Get-Json "testdata/ext002/scenarios/bulk-harvest-fail.json"
$staleFail = Get-Json "testdata/ext002/scenarios/stale-as-truth-fail.json"

if ($purposeFail.expectedValidate -ne "FAIL") { $failures += "purpose-mismatch expectedValidate must be FAIL" }
if ($purposes -contains [string]$purposeFail.purposeCode) {
    $failures += "purpose-mismatch fixture purpose must be outside allowlist"
}

if ($excessFail.expectedValidate -ne "FAIL") { $failures += "excess-field-store expectedValidate must be FAIL" }
if (@($excessFail.forbiddenStored).Count -lt 1) { $failures += "excess-field-store must list forbiddenStored" }

if ($outageFail.expectedValidate -ne "FAIL") { $failures += "outage-as-truth expectedValidate must be FAIL" }
if ($outageFail.registryAvailable -eq $true -or $outageFail.treatedAsVerifiedMatch -ne $true) {
    $failures += "outage-as-truth fixture must treat unavailable registry as verified"
}

if ($bulkFail.expectedValidate -ne "FAIL") { $failures += "bulk-harvest expectedValidate must be FAIL" }
if ($bulkFail.bulkHarvestAttempted -ne $true) { $failures += "bulk-harvest fixture must attempt harvest" }

if ($staleFail.expectedValidate -ne "FAIL") { $failures += "stale-as-truth expectedValidate must be FAIL" }
if ($staleFail.treatedAsVerifiedMatch -ne $true) { $failures += "stale-as-truth must treat stale as verified" }

# Positive fixtures
$mismatchPass = Get-Json "testdata/ext002/scenarios/mismatch-review-pass.json"
$unavailablePass = Get-Json "testdata/ext002/scenarios/unavailable-degrade-pass.json"
$correctionPass = Get-Json "testdata/ext002/scenarios/correction-workflow-pass.json"
$auditPass = Get-Json "testdata/ext002/audit/query-audit-pass.json"
$ledger = Get-Json "testdata/ext002/harness/civil-registry-run-ledger.json"

if ($mismatchPass.expectedValidate -ne "PASS") { $failures += "mismatch-review expectedValidate must be PASS" }
if ($mismatchPass.humanReviewQueued -ne $true) { $failures += "mismatch-review humanReviewQueued must be true" }

if ($unavailablePass.expectedValidate -ne "PASS") { $failures += "unavailable-degrade expectedValidate must be PASS" }
if ($correctionPass.expectedValidate -ne "PASS") { $failures += "correction-workflow expectedValidate must be PASS" }

if ($auditPass.expectedValidate -ne "PASS") { $failures += "query-audit expectedValidate must be PASS" }
if (@($auditPass.entries).Count -lt 1) { $failures += "query-audit needs entries" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "civil-registry-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveMoi -eq $true) { $failures += "ledger run $($r.runId) liveMoi must be false" }
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
    $ec = $cfg.ExtCivil
    if ($null -eq $ec) { $failures += "$rel missing ExtCivil"; continue }
    if ($ec.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ec.LiveMoiClaim -eq $true) { $failures += "$rel LiveMoiClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-002"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    purposeCount = $purposes.Count
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

Write-Host "PASS: EXT-002 civil-registry runtime validate"
exit 0
