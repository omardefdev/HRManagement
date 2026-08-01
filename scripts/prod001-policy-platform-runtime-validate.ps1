# PROD-001: runtime/production policy-platform binding validate.
# Usage:
#   powershell -File scripts/prod001-policy-platform-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/prod-policy/policy-platform-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prod001/out"
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

if ($m.card -ne "PROD-001") { $failures += "card must be PROD-001" }

foreach ($need in @(
    @{ Id = "PAY-002"; Status = "Satisfied" },
    @{ Id = "GOV-002"; Status = "Satisfied" },
    @{ Id = "STR-002"; Status = "Satisfied" },
    @{ Id = "live-policy-engine"; Status = "Blocked" },
    @{ Id = "live-workflow-runtime"; Status = "Blocked" },
    @{ Id = "live-form-designer"; Status = "Blocked" },
    @{ Id = "PROD-002"; Status = "Satisfied" },
    @{ Id = "ONB-001"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.versionedTemplates.required -ne $true) { $failures += "versionedTemplates.required must be true" }
foreach ($need in @("policy", "rule", "workflow", "form")) {
    if (@($m.versionedTemplates.kinds) -notcontains $need) { $failures += "missing kind $need" }
}
if ($m.effectiveDating.historicalVersionPinned -ne $true) { $failures += "historicalVersionPinned must be true" }
if ($m.fourEyesPublish.publisherRoleEqualsReviewerForbidden -ne $true) { $failures += "publisherRoleEqualsReviewerForbidden must be true" }
if ($m.simulation.syntheticOnly -ne $true) { $failures += "syntheticOnly must be true" }
if ($m.simulation.realDataForbidden -ne $true) { $failures += "realDataForbidden must be true" }
if ($m.rollback.required -ne $true) { $failures += "rollback.required must be true" }
if ($m.tenantOverride.cannotBypassNationalMandatory -ne $true) { $failures += "cannotBypassNationalMandatory must be true" }
if ($m.noArbitraryCode -ne $true) { $failures += "noArbitraryCode must be true" }
if ($m.invalidPublishForbidden -ne $true) { $failures += "invalidPublishForbidden must be true" }
if ($m.changeWithoutDeployOrFork -ne $true) { $failures += "changeWithoutDeployOrFork must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("templateId", "kind", "version", "outcome", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveEngineForbidden -ne $true) { $failures += "liveEngineForbidden must be true" }
if ($m.mockSandbox.livePublishForbidden -ne $true) { $failures += "livePublishForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.policyPlatformPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.policyPlatformPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$inv = Get-Json "testdata/prod001/scenarios/invalid-publish-fail.json"
$real = Get-Json "testdata/prod001/scenarios/real-data-sim-fail.json"
$bypass = Get-Json "testdata/prod001/scenarios/override-bypass-national-fail.json"
$same = Get-Json "testdata/prod001/scenarios/same-role-publish-fail.json"
$unv = Get-Json "testdata/prod001/scenarios/unversioned-template-fail.json"
$drift = Get-Json "testdata/prod001/scenarios/historical-drift-fail.json"
$code = Get-Json "testdata/prod001/scenarios/arbitrary-code-fail.json"

if ($inv.expectedValidate -ne "FAIL" -or $inv.published -ne $true) { $failures += "invalid-publish-fail invalid" }
if ($real.expectedValidate -ne "FAIL" -or $real.usesRealData -ne $true) { $failures += "real-data-sim-fail invalid" }
if ($bypass.expectedValidate -ne "FAIL" -or $bypass.overrideTouchesNationalMandatory -ne $true) { $failures += "override-bypass-national-fail invalid" }
if ($same.expectedValidate -ne "FAIL") { $failures += "same-role-publish-fail invalid" }
if ($unv.expectedValidate -ne "FAIL" -or $unv.hasVersion -eq $true) { $failures += "unversioned-template-fail invalid" }
if ($drift.expectedValidate -ne "FAIL" -or $drift.resolvedVersionMatchesPin -eq $true) { $failures += "historical-drift-fail invalid" }
if ($code.expectedValidate -ne "FAIL" -or $code.containsExecutableCode -ne $true) { $failures += "arbitrary-code-fail invalid" }

# Positive
$law = Get-Json "testdata/prod001/scenarios/law-change-pass.json"
$pin = Get-Json "testdata/prod001/scenarios/historical-pin-pass.json"
$syn = Get-Json "testdata/prod001/scenarios/synthetic-sim-pass.json"
$rb = Get-Json "testdata/prod001/scenarios/rollback-pass.json"
$ov = Get-Json "testdata/prod001/scenarios/agency-override-within-ceiling-pass.json"
$audit = Get-Json "testdata/prod001/audit/policy-audit-pass.json"
$ledger = Get-Json "testdata/prod001/harness/policy-run-ledger.json"

if ($law.expectedValidate -ne "PASS") { $failures += "law-change-pass invalid" }
if ($pin.expectedValidate -ne "PASS") { $failures += "historical-pin-pass invalid" }
if ($syn.expectedValidate -ne "PASS") { $failures += "synthetic-sim-pass invalid" }
if ($rb.expectedValidate -ne "PASS") { $failures += "rollback-pass invalid" }
if ($ov.expectedValidate -ne "PASS") { $failures += "agency-override-within-ceiling-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "policy-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "policy-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveEngine -eq $true) { $failures += "ledger run $($r.runId) liveEngine must be false" }
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
    $pp = $cfg.ProdPolicy
    if ($null -eq $pp) { $failures += "$rel missing ProdPolicy"; continue }
    if ($pp.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($pp.LivePolicyEngineClaim -eq $true) { $failures += "$rel LivePolicyEngineClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-001"
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

Write-Host "PASS: PROD-001 policy-platform runtime validate"
exit 0
