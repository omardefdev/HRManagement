# PROD-003: runtime/production employee-selfservice binding validate.
# Usage:
#   powershell -File scripts/prod003-employee-selfservice-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-ess/employee-selfservice-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod003/out"
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

if ($m.card -ne "PROD-003") { $failures += "card must be PROD-003" }

foreach ($need in @(
    @{ Id = "IAM-003"; Status = "Satisfied" },
    @{ Id = "FUN-001"; Status = "Satisfied" },
    @{ Id = "FUN-002"; Status = "Satisfied" },
    @{ Id = "FUN-003"; Status = "Satisfied" },
    @{ Id = "FUN-004"; Status = "Satisfied" },
    @{ Id = "FUN-005"; Status = "Satisfied" },
    @{ Id = "FUN-006"; Status = "Satisfied" },
    @{ Id = "UX-002"; Status = "Satisfied" },
    @{ Id = "IAM-002"; Status = "NotStarted" },
    @{ Id = "live-employee-portal"; Status = "Blocked" },
    @{ Id = "live-manager-portal"; Status = "Blocked" },
    @{ Id = "live-usage-telemetry"; Status = "Blocked" },
    @{ Id = "ESS-001"; Status = "NotStarted" },
    @{ Id = "MSS-001"; Status = "NotStarted" },
    @{ Id = "PROD-004"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.highVolumeJourneys.required -ne $true) { $failures += "highVolumeJourneys.required must be true" }
if ($m.highVolumeJourneys.exceptionOnlyBackOffice -ne $true) { $failures += "exceptionOnlyBackOffice must be true" }
foreach ($need in @("profile-correction", "leave-request", "payslip-tax", "documents", "manager-approvals", "delegation", "inbox")) {
    if (@($m.highVolumeJourneys.journeys) -notcontains $need) { $failures += "missing high-volume journey $need" }
}
if ($m.correctionVisibility.sourceVisible -ne $true) { $failures += "sourceVisible must be true" }
if ($m.correctionVisibility.statusVisible -ne $true) { $failures += "statusVisible must be true" }
if ($m.managerScope.enforced -ne $true) { $failures += "managerScope.enforced must be true" }
if ($m.managerScope.outsideScopeForbidden -ne $true) { $failures += "outsideScopeForbidden must be true" }
if ($m.usageTelemetry.required -ne $true) { $failures += "usageTelemetry.required must be true" }
if ($m.usageTelemetry.labStubAllowed -ne $true) { $failures += "labStubAllowed must be true" }
if ($m.usageTelemetry.liveForbidden -ne $true) { $failures += "usageTelemetry.liveForbidden must be true" }
if ($m.privacy.minimizationRequired -ne $true) { $failures += "minimizationRequired must be true" }
if ($m.deviceSession.lostSessionHandled -ne $true) { $failures += "lostSessionHandled must be true" }
if ($m.deviceSession.deviceSecurityRequired -ne $true) { $failures += "deviceSecurityRequired must be true" }
if ($m.personaUat.multiAgencyRequired -ne $true) { $failures += "multiAgencyRequired must be true" }
if ($m.personaUat.mobileDesktopRequired -ne $true) { $failures += "mobileDesktopRequired must be true" }
if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("journeyId", "actorRole", "agencyId", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}
if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveEmployeePortalForbidden -ne $true) { $failures += "liveEmployeePortalForbidden must be true" }
if ($m.mockSandbox.liveManagerPortalForbidden -ne $true) { $failures += "liveManagerPortalForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.employeeSelfServicePolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.employeeSelfServicePolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$bo = Get-Json "testdata/prod003/scenarios/backoffice-required-fail.json"
$csh = Get-Json "testdata/prod003/scenarios/correction-source-hidden-fail.json"
$mos = Get-Json "testdata/prod003/scenarios/manager-outside-scope-fail.json"
$uu = Get-Json "testdata/prod003/scenarios/usage-unmeasured-fail.json"
$lsu = Get-Json "testdata/prod003/scenarios/lost-session-unhandled-fail.json"
$xal = Get-Json "testdata/prod003/scenarios/cross-agency-leak-fail.json"

if ($bo.expectedValidate -ne "FAIL" -or $bo.completedWithoutBackOffice -eq $true) { $failures += "backoffice-required-fail invalid" }
if ($csh.expectedValidate -ne "FAIL" -or $csh.sourceVisible -eq $true) { $failures += "correction-source-hidden-fail invalid" }
if ($mos.expectedValidate -ne "FAIL" -or $mos.withinScope -eq $true) { $failures += "manager-outside-scope-fail invalid" }
if ($uu.expectedValidate -ne "FAIL" -or $uu.telemetryPresent -eq $true) { $failures += "usage-unmeasured-fail invalid" }
if ($lsu.expectedValidate -ne "FAIL" -or $lsu.recoveredSafely -eq $true) { $failures += "lost-session-unhandled-fail invalid" }
if ($xal.expectedValidate -ne "FAIL" -or $xal.withinScope -eq $true) { $failures += "cross-agency-leak-fail invalid" }

# Positive
$lr = Get-Json "testdata/prod003/scenarios/leave-request-pass.json"
$pc = Get-Json "testdata/prod003/scenarios/profile-correction-pass.json"
$msa = Get-Json "testdata/prod003/scenarios/manager-scoped-approve-pass.json"
$pv = Get-Json "testdata/prod003/scenarios/payslip-view-pass.json"
$lsr = Get-Json "testdata/prod003/scenarios/lost-session-recover-pass.json"
$ut = Get-Json "testdata/prod003/scenarios/usage-telemetry-pass.json"
$audit = Get-Json "testdata/prod003/audit/ess-audit-pass.json"
$ledger = Get-Json "testdata/prod003/harness/ess-run-ledger.json"

if ($lr.expectedValidate -ne "PASS") { $failures += "leave-request-pass invalid" }
if ($pc.expectedValidate -ne "PASS") { $failures += "profile-correction-pass invalid" }
if ($msa.expectedValidate -ne "PASS") { $failures += "manager-scoped-approve-pass invalid" }
if ($pv.expectedValidate -ne "PASS") { $failures += "payslip-view-pass invalid" }
if ($lsr.expectedValidate -ne "PASS") { $failures += "lost-session-recover-pass invalid" }
if ($ut.expectedValidate -ne "PASS") { $failures += "usage-telemetry-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "ess-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "ess-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.livePortal -eq $true) { $failures += "ledger run $($r.runId) livePortal must be false" }
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
    $pe = $cfg.ProdEss
    if ($null -eq $pe) { $failures += "$rel missing ProdEss"; continue }
    if ($pe.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pe.LiveEmployeeSelfServiceClaim -eq $true) { $failures += "$rel LiveEmployeeSelfServiceClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-003"
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

Write-Host "PASS: PROD-003 employee-selfservice runtime validate"
exit 0
