# MIG-005: runtime/production evidence binding validate.
# Usage:
#   powershell -File scripts/mig005-evidence-runtime-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-evidence/recon-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig005/out"
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

if ($m.card -ne "MIG-005") { $failures += "card must be MIG-005" }

foreach ($need in @(
    @{ Id = "MIG-003"; Status = "Satisfied" },
    @{ Id = "OBS-003"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "live-agency-recon"; Status = "Blocked" },
    @{ Id = "live-independent-sql"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$buckets = @($m.accountedBuckets)
if ($buckets.Count -lt 3) { $failures += "need >= 3 accountedBuckets" }
foreach ($need in @("Migrated", "Rejected", "Deferred")) {
    $b = @($buckets | Where-Object { $_.id -eq $need } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "missing accounted bucket $need"; continue }
    if (($need -eq "Rejected" -or $need -eq "Deferred") -and $b[0].reasonRequired -ne $true) {
        $failures += "bucket ${need}: reasonRequired must be true"
    }
}

$dims = @($m.reconDimensions)
if ($dims.Count -lt 6) { $failures += "need >= 6 reconDimensions" }
foreach ($need in @("counts", "sums", "hashes", "payrollControlTotals", "rejects", "lineage")) {
    if ($dims -notcontains $need) { $failures += "missing reconDimension $need" }
}

$fp = $m.financialPolicy
if ($null -eq $fp) {
    $failures += "financialPolicy missing"
}
else {
    if ($fp.unexplainedVarianceForbidden -ne $true) { $failures += "unexplainedVarianceForbidden must be true" }
    if ([double]$fp.controlTotalTolerance -ne 0) { $failures += "controlTotalTolerance must be 0" }
    if ($fp.payrollControlRequired -ne $true) { $failures += "payrollControlRequired must be true" }
}

$dc = $m.dualControl
if ($null -eq $dc) {
    $failures += "dualControl missing"
}
else {
    if ([int]$dc.minSigners -lt 2) { $failures += "dualControl minSigners must be >= 2" }
    if ($dc.requireDistinctRoles -ne $true) { $failures += "requireDistinctRoles must be true" }
    foreach ($need in @("migration-qa", "internal-audit")) {
        if (@($dc.roles) -notcontains $need) { $failures += "dualControl missing role $need" }
    }
}

$ep = $m.evidencePackage
$requiredArtifacts = @(
    "source-counts", "bucket-breakdown", "payroll-control", "lineage-export",
    "reject-register", "checksum-manifest", "sample-results", "dual-control-signoff"
)
if ($null -eq $ep) {
    $failures += "evidencePackage missing"
}
else {
    if ($ep.immutable -ne $true) { $failures += "evidencePackage.immutable must be true" }
    if ([string]$ep.contentHashAlg -ne "sha256") { $failures += "contentHashAlg must be sha256" }
    if (@($ep.requiredArtifacts).Count -lt 8) { $failures += "need >= 8 requiredArtifacts" }
    foreach ($need in $requiredArtifacts) {
        if (@($ep.requiredArtifacts) -notcontains $need) { $failures += "evidencePackage missing artifact $need" }
    }
}

$samp = $m.sampling
if ($null -eq $samp) {
    $failures += "sampling missing"
}
else {
    if ($samp.businessSignOffRequired -ne $true) { $failures += "businessSignOffRequired must be true" }
    if ([int]$samp.statisticalMin -lt 1) { $failures += "statisticalMin must be >= 1" }
}

$wave = $m.agencyWaveSignOff
if ($null -eq $wave) {
    $failures += "agencyWaveSignOff missing"
}
else {
    if ($wave.perWaveRequired -ne $true) { $failures += "perWaveRequired must be true" }
    foreach ($need in @("agency-data-owner", "migration-qa", "internal-audit")) {
        if (@($wave.roles) -notcontains $need) { $failures += "agencyWaveSignOff missing role $need" }
    }
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
    $version = $sig.evidencePolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.evidencePolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

# Negative fixtures
$gap = Get-Json "testdata/mig005/scenarios/unexplained-gap-fail.json"
$fin = Get-Json "testdata/mig005/scenarios/financial-variance-fail.json"
$dual = Get-Json "testdata/mig005/scenarios/dual-control-missing-fail.json"
$tamper = Get-Json "testdata/mig005/scenarios/tamper-evidence-fail.json"

if ($gap.expectedValidate -ne "FAIL") { $failures += "unexplained-gap fixture expectedValidate must be FAIL" }
if ([int]$gap.unexplainedGap -le 0) { $failures += "unexplained-gap fixture must have gap > 0" }

if ($fin.expectedValidate -ne "FAIL") { $failures += "financial-variance fixture expectedValidate must be FAIL" }
if ($null -ne $fin.explainedAdjustmentReason -and -not [string]::IsNullOrWhiteSpace([string]$fin.explainedAdjustmentReason)) {
    $failures += "financial-variance fixture must lack explainedAdjustmentReason"
}

if ($dual.expectedValidate -ne "FAIL") { $failures += "dual-control-missing fixture expectedValidate must be FAIL" }
if (@($dual.signers).Count -ge 2) { $failures += "dual-control-missing fixture must have < 2 signers" }

if ($tamper.expectedValidate -ne "FAIL") { $failures += "tamper-evidence fixture expectedValidate must be FAIL" }
if ([string]$tamper.declaredContentHash -eq [string]$tamper.recomputedContentHash) {
    $failures += "tamper-evidence fixture hashes must differ"
}

# Positive fixtures
$full = Get-Json "testdata/mig005/scenarios/full-accounted-pass.json"
$checksum = Get-Json "testdata/mig005/recon/checksum-recon-pass.json"
$biz = Get-Json "testdata/mig005/samples/business-sample-signoff.json"
$stat = Get-Json "testdata/mig005/samples/statistical-sample-pass.json"
$pkg = Get-Json "testdata/mig005/harness/evidence-package-stub.json"
$waveFix = Get-Json "testdata/mig005/harness/agency-wave-signoff.json"

if ($full.expectedValidate -ne "PASS") { $failures += "full-accounted expectedValidate must be PASS" }
if (([int]$full.migrated + [int]$full.rejected + [int]$full.deferred) -ne [int]$full.sourceCount) {
    $failures += "full-accounted buckets must sum to sourceCount"
}

if ($checksum.expectedValidate -ne "PASS") { $failures += "checksum-recon expectedValidate must be PASS" }
if ($checksum.allMatched -ne $true) { $failures += "checksum-recon allMatched must be true" }

if ($biz.expectedValidate -ne "PASS") { $failures += "business-sample-signoff expectedValidate must be PASS" }
if ($biz.approved -ne $true) { $failures += "business-sample must be approved" }

if ($stat.expectedValidate -ne "PASS") { $failures += "statistical-sample expectedValidate must be PASS" }
if ($stat.liveIndependentSql -eq $true) { $failures += "statistical-sample liveIndependentSql must be false" }

if ($pkg.immutable -ne $true) { $failures += "evidence-package-stub must be immutable" }
if (@($pkg.artifacts).Count -lt 8) { $failures += "evidence-package-stub needs >= 8 artifacts" }

if ($waveFix.expectedValidate -ne "PASS") { $failures += "agency-wave-signoff expectedValidate must be PASS" }
if ($waveFix.liveAgencyExtract -eq $true) { $failures += "agency-wave-signoff liveAgencyExtract must be false" }

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
    $me = $cfg.MigEvidence
    if ($null -eq $me) { $failures += "$rel missing MigEvidence"; continue }
    if ($me.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($me.LiveAgencyReconClaim -eq $true) { $failures += "$rel LiveAgencyReconClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-005"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    bucketCount = $buckets.Count
    artifactCount = @($ep.requiredArtifacts).Count
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

Write-Host "PASS: MIG-005 evidence runtime validate"
exit 0
