# ARC-002: runtime/production reference-architecture binding validate.
param(
    [string]$MatrixPath = "docs/arc-reference/reference-architecture-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/arc002/out"
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

if ($m.card -ne "ARC-002") { $failures += "card must be ARC-002" }

foreach ($need in @(
    @{ Id = "ARC-001"; Status = "Satisfied" },
    @{ Id = "TEN-001"; Status = "Satisfied" },
    @{ Id = "INT-001"; Status = "Satisfied" },
    @{ Id = "OPS-005"; Status = "Satisfied" },
    @{ Id = "live-two-site-deployment"; Status = "Blocked" },
    @{ Id = "live-architecture-fitness-envelope"; Status = "Blocked" },
    @{ Id = "live-coupling-boundary-scan"; Status = "Blocked" },
    @{ Id = "live-reference-implementation-signoff"; Status = "Blocked" },
    @{ Id = "QA-005"; Status = "Satisfied" },
    @{ Id = "PHY-001"; Status = "Satisfied" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.boundedContexts.noUnjustifiedDistributedComplexityRequired -ne $true) { $failures += "noUnjustifiedDistributedComplexityRequired must be true" }
if ($m.multiSiteProfiles.sameArtifactPrimaryAndDrRequired -ne $true) { $failures += "sameArtifactPrimaryAndDrRequired must be true" }
if ($m.supportedTopologies.unsupportedTopologyRejectedRequired -ne $true) { $failures += "unsupportedTopologyRejectedRequired must be true" }
if ($m.couplingGuardrails.noDirectDbCrossBoundaryRequired -ne $true) { $failures += "noDirectDbCrossBoundaryRequired must be true" }
if ($m.fitnessEnvelope.labFitnessHarnessRequired -ne $true) { $failures += "labFitnessHarnessRequired must be true" }
if ($m.mockSandbox.liveTwoSiteDeploymentForbidden -ne $true) { $failures += "liveTwoSiteDeploymentForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.referenceArchitecturePolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.referenceArchitecturePolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$ud = Get-Json "testdata/arc002/scenarios/unjustified-distribution-fail.json"
if ($ud.expectedValidate -ne "FAIL" -or $ud.unjustifiedDistributionPresent -ne $true) { $failures += "unjustified-distribution-fail invalid" }
$ad = Get-Json "testdata/arc002/scenarios/artifact-drift-primary-dr-fail.json"
if ($ad.expectedValidate -ne "FAIL" -or $ad.sameArtifactPrimaryAndDr -ne $false) { $failures += "artifact-drift-primary-dr-fail invalid" }
$ut = Get-Json "testdata/arc002/scenarios/unsupported-topology-fail.json"
if ($ut.expectedValidate -ne "FAIL" -or $ut.unsupportedTopologyPresent -ne $true) { $failures += "unsupported-topology-fail invalid" }
$db = Get-Json "testdata/arc002/scenarios/direct-db-cross-boundary-fail.json"
if ($db.expectedValidate -ne "FAIL" -or $db.directDbCrossBoundary -ne $true) { $failures += "direct-db-cross-boundary-fail invalid" }
$lr = Get-Json "testdata/arc002/scenarios/live-reference-claim-fail.json"
if ($lr.expectedValidate -ne "FAIL" -or $lr.liveReferenceClaim -ne $true) { $failures += "live-reference-claim-fail invalid" }

$ledger = Get-Json "testdata/arc002/harness/reference-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "reference-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveReference -eq $true) { $failures += "ledger run $($r.runId) liveReference must be false" }
}

# ARC-001 sync: ARC-002 Satisfied (reference-architecture binding; live two-site later)
$arc1 = Get-Json "docs/arc-criticality/criticality-matrix.json"
$arc2b = @($arc1.blockers | Where-Object { $_.id -eq "ARC-002" } | Select-Object -First 1)
if ($arc2b.Count -eq 0) { $failures += "ARC-001 matrix missing ARC-002 blocker" }
elseif ([string]$arc2b[0].status -ne "Satisfied") {
    $failures += "ARC-001 matrix ARC-002 must be Satisfied (got $($arc2b[0].status))"
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
    $ar = $cfg.ArcReference
    if ($null -eq $ar) { $failures += "$rel missing ArcReference"; continue }
    if ($ar.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($ar.LiveReferenceClaim -eq $true) { $failures += "$rel LiveReferenceClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "ARC-002"
    mode = "runtime-validate"
    ok = ($failures.Count -eq 0)
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

Write-Host "PASS: ARC-002 reference runtime validate"
exit 0
