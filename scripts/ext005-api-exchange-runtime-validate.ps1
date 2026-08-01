# EXT-005: runtime/production api-exchange binding validate.
# Usage:
#   powershell -File scripts/ext005-api-exchange-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-api/api-exchange-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext005/out"
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

if ($m.card -ne "EXT-005") { $failures += "card must be EXT-005" }

foreach ($need in @(
    @{ Id = "IAM-004"; Status = "Satisfied" },
    @{ Id = "TEN-003"; Status = "Satisfied" },
    @{ Id = "INT-002"; Status = "Satisfied" },
    @{ Id = "PROD-007"; Status = "Satisfied" },
    @{ Id = "gov-exchange-bus-live"; Status = "Blocked" },
    @{ Id = "schema-registry-live"; Status = "Blocked" },
    @{ Id = "partner-cdc-cert"; Status = "Blocked" },
    @{ Id = "national-openapi-publish"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.catalogRequired -ne $true) { $failures += "catalogRequired must be true" }
foreach ($need in @("integrationId", "contractRef", "owner", "sla")) {
    if (@($m.requiredCatalogFields) -notcontains $need) { $failures += "requiredCatalogFields missing $need" }
}

if ($m.versioning.semverRequired -ne $true) { $failures += "semverRequired must be true" }
if ($m.versioning.breakingRequiresMajorBump -ne $true) { $failures += "breakingRequiresMajorBump must be true" }
if ($m.compatibility.measureBackwardCompat -ne $true) { $failures += "measureBackwardCompat must be true" }
if ($m.compatibility.supportNMinus1 -ne $true) { $failures += "supportNMinus1 must be true" }
if ($m.sharedDbForbidden -ne $true) { $failures += "sharedDbForbidden must be true" }

if ($m.purposeBoundExport.consentRequired -ne $true) { $failures += "consentRequired must be true" }
$purposes = @($m.purposeBoundExport.purposeAllowlist)
if ($purposes.Count -lt 4) { $failures += "purposeAllowlist too short" }
foreach ($need in @("payroll-disbursement-export", "civil-registry-proof-query", "agency-onboarding-sync", "audit-evidence-export")) {
    if ($purposes -notcontains $need) { $failures += "purposeAllowlist missing $need" }
}

if ([int]$m.throttling.defaultRpm -le 0) { $failures += "defaultRpm must be > 0" }
if ([int]$m.throttling.burst -le 0) { $failures += "burst must be > 0" }
if ([int]$m.throttling.onExceed -ne 429) { $failures += "onExceed must be 429" }

if ($m.nonRepudiation.correlationIdRequired -ne $true) { $failures += "correlationIdRequired must be true" }
if ($m.nonRepudiation.auditRequired -ne $true) { $failures += "nonRepudiation.auditRequired must be true" }
if ($m.tenant.tenantHeaderRequired -ne $true) { $failures += "tenantHeaderRequired must be true" }
if ($m.tenant.crossTenantDeny -ne $true) { $failures += "crossTenantDeny must be true" }
if ($m.errorModel.problemDetailsRequired -ne $true) { $failures += "problemDetailsRequired must be true" }

if ([int]$m.deprecation.noticeDaysMin -lt 90) { $failures += "noticeDaysMin must be >= 90" }
if ($m.deprecation.sunsetRequiresMajor -ne $true) { $failures += "sunsetRequiresMajor must be true" }
if ($m.dataMinimization.forbidExcessFields -ne $true) { $failures += "forbidExcessFields must be true" }

if ($m.schemaRegistry.labMockAllowed -ne $true) { $failures += "labMockAllowed must be true" }
if ($m.schemaRegistry.liveForbidden -ne $true) { $failures += "schemaRegistry.liveForbidden must be true" }
if ($m.consumerDriven.certifiedConsumersProtected -ne $true) { $failures += "certifiedConsumersProtected must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("exchangeId", "integrationId", "actor", "tenantId", "outcome", "correlationId", "occurredAt")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "mockSandbox.labStub must be true" }
if ($m.mockSandbox.liveBusForbidden -ne $true) { $failures += "liveBusForbidden must be true" }
if ($m.mockSandbox.mockCatalog -ne $true -or $m.mockSandbox.mockRegistry -ne $true) {
    $failures += "mockCatalog/mockRegistry must be true"
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
    $version = $sig.apiExchangePolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.apiExchangePolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$missingOwner = Get-Json "testdata/ext005/scenarios/missing-owner-sla-fail.json"
$breaking = Get-Json "testdata/ext005/scenarios/breaking-without-bump-fail.json"
$sharedDb = Get-Json "testdata/ext005/scenarios/shared-db-fail.json"
$cdcBreak = Get-Json "testdata/ext005/scenarios/certified-consumer-break-fail.json"
$purposeFail = Get-Json "testdata/ext005/scenarios/purpose-denied-export-fail.json"
$schemaFail = Get-Json "testdata/ext005/scenarios/schema-incompat-fail.json"
$depFail = Get-Json "testdata/ext005/scenarios/deprecation-too-short-fail.json"

if ($missingOwner.expectedValidate -ne "FAIL") { $failures += "missing-owner-sla expectedValidate must be FAIL" }
if (-not [string]::IsNullOrWhiteSpace([string]$missingOwner.owner) -and -not [string]::IsNullOrWhiteSpace([string]$missingOwner.sla)) {
    $failures += "missing-owner-sla must omit owner or sla"
}

if ($breaking.expectedValidate -ne "FAIL") { $failures += "breaking-without-bump expectedValidate must be FAIL" }
if ($breaking.breakingChange -ne $true -or $breaking.majorBumped -eq $true) {
    $failures += "breaking-without-bump must be breaking without major"
}

if ($sharedDb.expectedValidate -ne "FAIL") { $failures += "shared-db expectedValidate must be FAIL" }
if ($sharedDb.sharedDbJoin -ne $true) { $failures += "shared-db fixture must use shared DB" }

if ($cdcBreak.expectedValidate -ne "FAIL") { $failures += "certified-consumer-break expectedValidate must be FAIL" }
if ($cdcBreak.contractBroken -ne $true) { $failures += "certified-consumer-break must break contract" }

if ($purposeFail.expectedValidate -ne "FAIL") { $failures += "purpose-denied-export expectedValidate must be FAIL" }
if ($purposes -contains [string]$purposeFail.purposeCode) {
    $failures += "purpose-denied fixture purpose must be outside allowlist"
}

if ($schemaFail.expectedValidate -ne "FAIL") { $failures += "schema-incompat expectedValidate must be FAIL" }
if ($schemaFail.schemaCompatible -eq $true) { $failures += "schema-incompat must be incompatible" }

if ($depFail.expectedValidate -ne "FAIL") { $failures += "deprecation-too-short expectedValidate must be FAIL" }
if ([int]$depFail.noticeDays -ge [int]$m.deprecation.noticeDaysMin) {
    $failures += "deprecation-too-short noticeDays must be below minimum"
}

# Positive fixtures
$n1 = Get-Json "testdata/ext005/scenarios/n1-compat-pass.json"
$throttle = Get-Json "testdata/ext005/scenarios/throttle-exceed-pass.json"
$interop = Get-Json "testdata/ext005/scenarios/interop-harness-pass.json"
$auditPass = Get-Json "testdata/ext005/audit/exchange-audit-pass.json"
$ledger = Get-Json "testdata/ext005/harness/api-exchange-run-ledger.json"

if ($n1.expectedValidate -ne "PASS") { $failures += "n1-compat expectedValidate must be PASS" }
if ($n1.backwardCompatMeasured -ne $true) { $failures += "n1-compat must measure backward compat" }

if ($throttle.expectedValidate -ne "PASS") { $failures += "throttle-exceed expectedValidate must be PASS" }
if ([int]$throttle.httpStatus -ne 429) { $failures += "throttle-exceed must return 429" }

if ($interop.expectedValidate -ne "PASS") { $failures += "interop-harness expectedValidate must be PASS" }
if ($interop.liveBus -eq $true) { $failures += "interop-harness liveBus must be false" }

if ($auditPass.expectedValidate -ne "PASS") { $failures += "exchange-audit expectedValidate must be PASS" }
if (@($auditPass.entries).Count -lt 1) { $failures += "exchange-audit needs entries" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "api-exchange-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveBus -eq $true) { $failures += "ledger run $($r.runId) liveBus must be false" }
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
    $ea = $cfg.ExtApi
    if ($null -eq $ea) { $failures += "$rel missing ExtApi"; continue }
    if ($ea.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ea.LiveExchangeClaim -eq $true) { $failures += "$rel LiveExchangeClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-005"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    catalogFieldCount = @($m.requiredCatalogFields).Count
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

Write-Host "PASS: EXT-005 api-exchange runtime validate"
exit 0
