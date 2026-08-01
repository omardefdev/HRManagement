# PROD-001: validate policy-platform contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/prod001-policy-platform-validate.ps1

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

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("VersionedChange", "HistoricalPin", "SimulationGate", "PublishSafety", "OverrideRollback")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if ($m.versionedTemplates.required -ne $true) { $failures += "versionedTemplates.required must be true" }
foreach ($need in @("policy", "rule", "workflow", "form")) {
    if (@($m.versionedTemplates.kinds) -notcontains $need) { $failures += "missing kind $need" }
}
if ($m.effectiveDating.required -ne $true) { $failures += "effectiveDating.required must be true" }
if ($m.effectiveDating.historicalVersionPinned -ne $true) { $failures += "historicalVersionPinned must be true" }
if ($m.fourEyesPublish.required -ne $true) { $failures += "fourEyesPublish.required must be true" }
if ($m.fourEyesPublish.publisherRoleEqualsReviewerForbidden -ne $true) { $failures += "publisherRoleEqualsReviewerForbidden must be true" }
if ($m.simulation.required -ne $true) { $failures += "simulation.required must be true" }
if ($m.simulation.syntheticOnly -ne $true) { $failures += "syntheticOnly must be true" }
if ($m.simulation.realDataForbidden -ne $true) { $failures += "realDataForbidden must be true" }
if ($m.rollback.required -ne $true) { $failures += "rollback.required must be true" }
if ($m.tenantOverride.allowed -ne $true) { $failures += "tenantOverride.allowed must be true" }
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
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

foreach ($rel in @(
    "testdata/prod001/scenarios/invalid-publish-fail.json",
    "testdata/prod001/scenarios/real-data-sim-fail.json",
    "testdata/prod001/scenarios/override-bypass-national-fail.json",
    "testdata/prod001/scenarios/same-role-publish-fail.json",
    "testdata/prod001/scenarios/unversioned-template-fail.json",
    "testdata/prod001/scenarios/historical-drift-fail.json",
    "testdata/prod001/scenarios/arbitrary-code-fail.json",
    "testdata/prod001/scenarios/law-change-pass.json",
    "testdata/prod001/scenarios/historical-pin-pass.json",
    "testdata/prod001/scenarios/synthetic-sim-pass.json",
    "testdata/prod001/scenarios/rollback-pass.json",
    "testdata/prod001/scenarios/agency-override-within-ceiling-pass.json",
    "testdata/prod001/audit/policy-audit-pass.json",
    "testdata/prod001/harness/policy-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$inv = Get-Json "testdata/prod001/scenarios/invalid-publish-fail.json"
if ($inv.expectedValidate -ne "FAIL" -or $inv.isValid -eq $true -or $inv.published -ne $true) { $failures += "invalid-publish-fail invalid" }
$real = Get-Json "testdata/prod001/scenarios/real-data-sim-fail.json"
if ($real.expectedValidate -ne "FAIL" -or $real.usesRealData -ne $true) { $failures += "real-data-sim-fail invalid" }
$bypass = Get-Json "testdata/prod001/scenarios/override-bypass-national-fail.json"
if ($bypass.expectedValidate -ne "FAIL" -or $bypass.overrideTouchesNationalMandatory -ne $true) { $failures += "override-bypass-national-fail invalid" }
$same = Get-Json "testdata/prod001/scenarios/same-role-publish-fail.json"
if ($same.expectedValidate -ne "FAIL" -or [string]$same.publisherRole -ne [string]$same.reviewerRole) { $failures += "same-role-publish-fail invalid" }
$unv = Get-Json "testdata/prod001/scenarios/unversioned-template-fail.json"
if ($unv.expectedValidate -ne "FAIL" -or $unv.hasVersion -eq $true) { $failures += "unversioned-template-fail invalid" }
$drift = Get-Json "testdata/prod001/scenarios/historical-drift-fail.json"
if ($drift.expectedValidate -ne "FAIL" -or $drift.resolvedVersionMatchesPin -eq $true) { $failures += "historical-drift-fail invalid" }
$code = Get-Json "testdata/prod001/scenarios/arbitrary-code-fail.json"
if ($code.expectedValidate -ne "FAIL" -or $code.containsExecutableCode -ne $true) { $failures += "arbitrary-code-fail invalid" }

$law = Get-Json "testdata/prod001/scenarios/law-change-pass.json"
if ($law.expectedValidate -ne "PASS" -or $law.hasVersion -ne $true) { $failures += "law-change-pass invalid" }
$pin = Get-Json "testdata/prod001/scenarios/historical-pin-pass.json"
if ($pin.expectedValidate -ne "PASS" -or $pin.resolvedVersionMatchesPin -ne $true) { $failures += "historical-pin-pass invalid" }
$syn = Get-Json "testdata/prod001/scenarios/synthetic-sim-pass.json"
if ($syn.expectedValidate -ne "PASS" -or $syn.usesRealData -eq $true) { $failures += "synthetic-sim-pass invalid" }
$rb = Get-Json "testdata/prod001/scenarios/rollback-pass.json"
if ($rb.expectedValidate -ne "PASS" -or $rb.hasRollbackRef -ne $true) { $failures += "rollback-pass invalid" }
$ov = Get-Json "testdata/prod001/scenarios/agency-override-within-ceiling-pass.json"
if ($ov.expectedValidate -ne "PASS" -or $ov.overrideTouchesNationalMandatory -eq $true) { $failures += "agency-override-within-ceiling-pass invalid" }

$audit = Get-Json "testdata/prod001/audit/policy-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "policy-audit-pass invalid" }
$ledger = Get-Json "testdata/prod001/harness/policy-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "policy-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveEngine -eq $true) { $failures += "ledger run $($r.runId) liveEngine must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PROD-001"
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

Write-Host "PASS: PROD-001 policy-platform validate"
exit 0
