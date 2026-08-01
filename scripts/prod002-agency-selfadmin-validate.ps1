# PROD-002: validate agency-selfadmin contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/prod002-agency-selfadmin-validate.ps1

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

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("DailyDelegatedChange", "NationalAndTenantCeiling", "Accountability", "SensitiveFourEyes", "HostileConcurrent")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
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
if ($m.fourEyesSensitive.required -ne $true) { $failures += "fourEyesSensitive.required must be true" }
if ($m.fourEyesSensitive.publisherRoleEqualsReviewerForbidden -ne $true) { $failures += "publisherRoleEqualsReviewerForbidden must be true" }
if ($m.preview.required -ne $true) { $failures += "preview.required must be true" }
if ($m.separationNationalAgency.required -ne $true) { $failures += "separationNationalAgency.required must be true" }
if ($m.supportEscalation.required -ne $true) { $failures += "supportEscalation.required must be true" }

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveSelfAdminForbidden -ne $true) { $failures += "liveSelfAdminForbidden must be true" }
if ($m.mockSandbox.liveDelegatedUiForbidden -ne $true) { $failures += "liveDelegatedUiForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

foreach ($rel in @(
    "testdata/prod002/scenarios/national-control-mutate-fail.json",
    "testdata/prod002/scenarios/cross-tenant-admin-fail.json",
    "testdata/prod002/scenarios/no-rollback-fail.json",
    "testdata/prod002/scenarios/same-role-sensitive-fail.json",
    "testdata/prod002/scenarios/missing-audit-fail.json",
    "testdata/prod002/scenarios/dba-required-path-fail.json",
    "testdata/prod002/scenarios/hostile-quota-bypass-fail.json",
    "testdata/prod002/scenarios/daily-structure-change-pass.json",
    "testdata/prod002/scenarios/role-template-preview-pass.json",
    "testdata/prod002/scenarios/sensitive-four-eyes-pass.json",
    "testdata/prod002/scenarios/rollback-pass.json",
    "testdata/prod002/scenarios/concurrent-change-pass.json",
    "testdata/prod002/audit/agency-admin-audit-pass.json",
    "testdata/prod002/harness/selfadmin-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$nat = Get-Json "testdata/prod002/scenarios/national-control-mutate-fail.json"
if ($nat.expectedValidate -ne "FAIL" -or $nat.attemptedNationalMutate -ne $true) { $failures += "national-control-mutate-fail invalid" }
$cross = Get-Json "testdata/prod002/scenarios/cross-tenant-admin-fail.json"
if ($cross.expectedValidate -ne "FAIL" -or $cross.targetTenantEqualsActorTenant -eq $true) { $failures += "cross-tenant-admin-fail invalid" }
$norb = Get-Json "testdata/prod002/scenarios/no-rollback-fail.json"
if ($norb.expectedValidate -ne "FAIL" -or $norb.hasRollbackRef -eq $true) { $failures += "no-rollback-fail invalid" }
$same = Get-Json "testdata/prod002/scenarios/same-role-sensitive-fail.json"
if ($same.expectedValidate -ne "FAIL" -or [string]$same.publisherRole -ne [string]$same.reviewerRole) { $failures += "same-role-sensitive-fail invalid" }
$maud = Get-Json "testdata/prod002/scenarios/missing-audit-fail.json"
if ($maud.expectedValidate -ne "FAIL") { $failures += "missing-audit-fail invalid" }
$dba = Get-Json "testdata/prod002/scenarios/dba-required-path-fail.json"
if ($dba.expectedValidate -ne "FAIL" -or $dba.noDbaPath -eq $true) { $failures += "dba-required-path-fail invalid" }
$hostq = Get-Json "testdata/prod002/scenarios/hostile-quota-bypass-fail.json"
if ($hostq.expectedValidate -ne "FAIL" -or $hostq.attemptedNationalMutate -ne $true) { $failures += "hostile-quota-bypass-fail invalid" }

$daily = Get-Json "testdata/prod002/scenarios/daily-structure-change-pass.json"
if ($daily.expectedValidate -ne "PASS" -or $daily.noDbaPath -ne $true) { $failures += "daily-structure-change-pass invalid" }
$prev = Get-Json "testdata/prod002/scenarios/role-template-preview-pass.json"
if ($prev.expectedValidate -ne "PASS" -or $prev.previewUsed -ne $true) { $failures += "role-template-preview-pass invalid" }
$fe = Get-Json "testdata/prod002/scenarios/sensitive-four-eyes-pass.json"
if ($fe.expectedValidate -ne "PASS" -or [string]$fe.publisherRole -eq [string]$fe.reviewerRole) { $failures += "sensitive-four-eyes-pass invalid" }
$rb = Get-Json "testdata/prod002/scenarios/rollback-pass.json"
if ($rb.expectedValidate -ne "PASS" -or $rb.hasRollbackRef -ne $true) { $failures += "rollback-pass invalid" }
$conc = Get-Json "testdata/prod002/scenarios/concurrent-change-pass.json"
if ($conc.expectedValidate -ne "PASS") { $failures += "concurrent-change-pass invalid" }

$audit = Get-Json "testdata/prod002/audit/agency-admin-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "agency-admin-audit-pass invalid" }
$ledger = Get-Json "testdata/prod002/harness/selfadmin-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "selfadmin-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveSelfAdmin -eq $true) { $failures += "ledger run $($r.runId) liveSelfAdmin must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-002"
    mode = "pack-validate"
    ok = ($failures.Count -eq 0)
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

Write-Host "PASS: PROD-002 agency-selfadmin validate"
exit 0
