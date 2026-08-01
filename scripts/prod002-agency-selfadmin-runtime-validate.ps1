# PROD-002: runtime/production agency-selfadmin binding validate.
# Usage:
#   powershell -File scripts/prod002-agency-selfadmin-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-agency/agency-selfadmin-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod002/out"
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

if ($m.card -ne "PROD-002") { $failures += "card must be PROD-002" }

foreach ($need in @(
    @{ Id = "IAM-003"; Status = "Satisfied" },
    @{ Id = "TEN-003"; Status = "Satisfied" },
    @{ Id = "PROD-001"; Status = "Satisfied" },
    @{ Id = "live-agency-selfadmin"; Status = "Blocked" },
    @{ Id = "live-delegated-admin-ui"; Status = "Blocked" },
    @{ Id = "cross-tenant-admin"; Status = "Blocked" },
    @{ Id = "ONB-001"; Status = "Satisfied" },
    @{ Id = "PROD-003"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.delegatedAdmin.required -ne $true) { $failures += "delegatedAdmin.required must be true" }
if ($m.delegatedAdmin.noDbaDeveloperRequired -ne $true) { $failures += "noDbaDeveloperRequired must be true" }
foreach ($need in @("structure", "roles", "templates", "calendars", "notifications", "quotas")) {
    if (@($m.delegatedAdmin.scopes) -notcontains $need) { $failures += "missing scope $need" }
}
if ($m.nationalControlsImmutable -ne $true) { $failures += "nationalControlsImmutable must be true" }
if ($m.crossTenantAdminForbidden -ne $true) { $failures += "crossTenantAdminForbidden must be true" }
if ($m.rollback.required -ne $true) { $failures += "rollback.required must be true" }
if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("changeId", "agencyId", "scope", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}
if ($m.fourEyesSensitive.publisherRoleEqualsReviewerForbidden -ne $true) { $failures += "publisherRoleEqualsReviewerForbidden must be true" }
if ($m.preview.required -ne $true) { $failures += "preview.required must be true" }
if ($m.separationNationalAgency.required -ne $true) { $failures += "separationNationalAgency.required must be true" }
if ($m.supportEscalation.required -ne $true) { $failures += "supportEscalation.required must be true" }

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveSelfAdminForbidden -ne $true) { $failures += "liveSelfAdminForbidden must be true" }
if ($m.mockSandbox.liveDelegatedUiForbidden -ne $true) { $failures += "liveDelegatedUiForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.agencySelfAdminPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.agencySelfAdminPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$nat = Get-Json "testdata/prod002/scenarios/national-control-mutate-fail.json"
$cross = Get-Json "testdata/prod002/scenarios/cross-tenant-admin-fail.json"
$norb = Get-Json "testdata/prod002/scenarios/no-rollback-fail.json"
$same = Get-Json "testdata/prod002/scenarios/same-role-sensitive-fail.json"
$maud = Get-Json "testdata/prod002/scenarios/missing-audit-fail.json"
$dba = Get-Json "testdata/prod002/scenarios/dba-required-path-fail.json"
$hostq = Get-Json "testdata/prod002/scenarios/hostile-quota-bypass-fail.json"

if ($nat.expectedValidate -ne "FAIL" -or $nat.attemptedNationalMutate -ne $true) { $failures += "national-control-mutate-fail invalid" }
if ($cross.expectedValidate -ne "FAIL" -or $cross.targetTenantEqualsActorTenant -eq $true) { $failures += "cross-tenant-admin-fail invalid" }
if ($norb.expectedValidate -ne "FAIL" -or $norb.hasRollbackRef -eq $true) { $failures += "no-rollback-fail invalid" }
if ($same.expectedValidate -ne "FAIL") { $failures += "same-role-sensitive-fail invalid" }
if ($maud.expectedValidate -ne "FAIL") { $failures += "missing-audit-fail invalid" }
if ($dba.expectedValidate -ne "FAIL" -or $dba.noDbaPath -eq $true) { $failures += "dba-required-path-fail invalid" }
if ($hostq.expectedValidate -ne "FAIL") { $failures += "hostile-quota-bypass-fail invalid" }

# Positive
$daily = Get-Json "testdata/prod002/scenarios/daily-structure-change-pass.json"
$prev = Get-Json "testdata/prod002/scenarios/role-template-preview-pass.json"
$fe = Get-Json "testdata/prod002/scenarios/sensitive-four-eyes-pass.json"
$rb = Get-Json "testdata/prod002/scenarios/rollback-pass.json"
$conc = Get-Json "testdata/prod002/scenarios/concurrent-change-pass.json"
$audit = Get-Json "testdata/prod002/audit/agency-admin-audit-pass.json"
$ledger = Get-Json "testdata/prod002/harness/selfadmin-run-ledger.json"

if ($daily.expectedValidate -ne "PASS") { $failures += "daily-structure-change-pass invalid" }
if ($prev.expectedValidate -ne "PASS") { $failures += "role-template-preview-pass invalid" }
if ($fe.expectedValidate -ne "PASS") { $failures += "sensitive-four-eyes-pass invalid" }
if ($rb.expectedValidate -ne "PASS") { $failures += "rollback-pass invalid" }
if ($conc.expectedValidate -ne "PASS") { $failures += "concurrent-change-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "agency-admin-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "selfadmin-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveSelfAdmin -eq $true) { $failures += "ledger run $($r.runId) liveSelfAdmin must be false" }
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
    $pa = $cfg.ProdAgency
    if ($null -eq $pa) { $failures += "$rel missing ProdAgency"; continue }
    if ($pa.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pa.LiveAgencySelfAdminClaim -eq $true) { $failures += "$rel LiveAgencySelfAdminClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-002"
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

Write-Host "PASS: PROD-002 agency-selfadmin runtime validate"
exit 0
