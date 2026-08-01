# EXT-006: runtime/production sandbox-cert binding validate.
# Usage:
#   powershell -File scripts/ext006-sandbox-cert-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-sandbox/sandbox-cert-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext006/out"
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

if ($m.card -ne "EXT-006") { $failures += "card must be EXT-006" }

foreach ($need in @(
    @{ Id = "EXT-005"; Status = "Satisfied" },
    @{ Id = "REL-001"; Status = "Satisfied" },
    @{ Id = "live-developer-portal"; Status = "Blocked" },
    @{ Id = "real-pii-in-sandbox"; Status = "Blocked" },
    @{ Id = "partner-cert-fleet-live"; Status = "Blocked" },
    @{ Id = "EXT-007"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.syntheticDataOnly -ne $true) { $failures += "syntheticDataOnly must be true" }
if ($m.realPiiForbidden -ne $true) { $failures += "realPiiForbidden must be true" }
if ($m.credentials.ephemeralRequired -ne $true) { $failures += "ephemeralRequired must be true" }
if ($m.credentials.manualSecretsForbidden -ne $true) { $failures += "manualSecretsForbidden must be true" }
if ($m.credentials.noProdAccess -ne $true) { $failures += "noProdAccess must be true" }
if ($m.conformance.negativeCasesRequired -ne $true) { $failures += "negativeCasesRequired must be true" }
if ($m.conformance.incompatibleClientMustFail -ne $true) { $failures += "incompatibleClientMustFail must be true" }
if ($m.certification.requiredBeforeProdJoin -ne $true) { $failures += "requiredBeforeProdJoin must be true" }
if ($m.certification.revocationSupported -ne $true) { $failures += "revocationSupported must be true" }
if ($m.docsMatchPublishedContract -ne $true) { $failures += "docsMatchPublishedContract must be true" }
if ($m.rateErrorSimulation.required -ne $true) { $failures += "rateErrorSimulation.required must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("partnerId", "credentialId", "suiteId", "outcome", "actor", "occurredAt")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.livePortalForbidden -ne $true) { $failures += "livePortalForbidden must be true" }
if ($m.mockSandbox.realDataForbidden -ne $true) { $failures += "realDataForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]::IsNullOrWhiteSpace([string]$sig.signedByRole) -or [string]::IsNullOrWhiteSpace([string]$sig.reviewerRole)) {
        $failures += "signing missing roles"
    }
    elseif ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) {
        $failures += "signing owner equals reviewer"
    }
    $version = $sig.sandboxCertPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.sandboxCertPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative
$prodAccess = Get-Json "testdata/ext006/scenarios/prod-access-fail.json"
$realPii = Get-Json "testdata/ext006/scenarios/real-pii-fail.json"
$manual = Get-Json "testdata/ext006/scenarios/manual-secret-fail.json"
$incompat = Get-Json "testdata/ext006/scenarios/incompatible-client-pass-as-certified-fail.json"
$docsDrift = Get-Json "testdata/ext006/scenarios/docs-drift-fail.json"
$unrevoked = Get-Json "testdata/ext006/scenarios/unrevoked-compromised-key-fail.json"

if ($prodAccess.expectedValidate -ne "FAIL" -or $prodAccess.attemptedProdAccess -ne $true) { $failures += "prod-access-fail invalid" }
if ($realPii.expectedValidate -ne "FAIL" -or $realPii.containsRealPii -ne $true) { $failures += "real-pii-fail invalid" }
if ($manual.expectedValidate -ne "FAIL" -or $manual.manualSecretIssued -ne $true) { $failures += "manual-secret-fail invalid" }
if ($incompat.expectedValidate -ne "FAIL" -or $incompat.treatedAsCertified -ne $true) { $failures += "incompatible-client fail invalid" }
if ($docsDrift.expectedValidate -ne "FAIL" -or $docsDrift.docsMatchPublishedContract -eq $true) { $failures += "docs-drift-fail invalid" }
if ($unrevoked.expectedValidate -ne "FAIL" -or $unrevoked.revoked -eq $true) { $failures += "unrevoked-compromised-key-fail invalid" }

# Positive
$onboard = Get-Json "testdata/ext006/scenarios/partner-onboard-pass.json"
$neg = Get-Json "testdata/ext006/scenarios/negative-conformance-pass.json"
$keyRot = Get-Json "testdata/ext006/scenarios/key-rotation-pass.json"
$rate = Get-Json "testdata/ext006/scenarios/rate-error-sim-pass.json"
$audit = Get-Json "testdata/ext006/audit/cert-audit-pass.json"
$ledger = Get-Json "testdata/ext006/harness/sandbox-run-ledger.json"

if ($onboard.expectedValidate -ne "PASS") { $failures += "partner-onboard-pass invalid" }
if ($neg.expectedValidate -ne "PASS") { $failures += "negative-conformance-pass invalid" }
if ($keyRot.expectedValidate -ne "PASS") { $failures += "key-rotation-pass invalid" }
if ($rate.expectedValidate -ne "PASS") { $failures += "rate-error-sim-pass invalid" }
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "cert-audit-pass invalid" }

$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "sandbox-run-ledger needs >= 2 runs" }
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
    $es = $cfg.ExtSandbox
    if ($null -eq $es) { $failures += "$rel missing ExtSandbox"; continue }
    if ($es.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($es.LiveSandboxPortalClaim -eq $true) { $failures += "$rel LiveSandboxPortalClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-006"
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

Write-Host "PASS: EXT-006 sandbox-cert runtime validate"
exit 0
