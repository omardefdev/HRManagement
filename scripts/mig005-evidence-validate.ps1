# MIG-005: validate reconciliation/evidence matrix and harness fixtures.
# Usage:
#   powershell -File scripts/mig005-evidence-validate.ps1

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

$matrixFull = Resolve-RepoPath $MatrixPath
if (-not (Test-Path $matrixFull)) { Write-Error "matrix not found: $matrixFull" }

$m = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()

if ($m.card -ne "MIG-005") { $failures += "card must be MIG-005" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("FullAccounted", "FinancialControls", "BusinessSamples", "ImmutableEvidence", "ChecksumRecon", "DualControlSignOff")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$bucketIds = @($m.accountedBuckets | ForEach-Object { $_.id })
foreach ($need in @("Migrated", "Rejected", "Deferred")) {
    if ($bucketIds -notcontains $need) { $failures += "missing bucket $need" }
}
foreach ($b in $m.accountedBuckets) {
    if ($b.id -in @("Rejected", "Deferred") -and -not $b.reasonRequired) {
        $failures += "bucket $($b.id) must require reason"
    }
}

$dims = @($m.reconDimensions)
foreach ($need in @("counts", "sums", "hashes", "payrollControlTotals", "rejects", "lineage")) {
    if ($dims -notcontains $need) { $failures += "missing recon dimension $need" }
}

if (-not $m.financialPolicy.unexplainedVarianceForbidden) {
    $failures += "unexplainedVarianceForbidden must be true"
}
if ([double]$m.financialPolicy.controlTotalTolerance -ne 0) {
    $failures += "lab controlTotalTolerance must be 0"
}

if ([int]$m.dualControl.minSigners -lt 2) { $failures += "minSigners must be >= 2" }
if (-not $m.dualControl.requireDistinctRoles) { $failures += "requireDistinctRoles must be true" }

if (-not $m.evidencePackage.immutable) { $failures += "evidencePackage.immutable must be true" }
$arts = @($m.evidencePackage.requiredArtifacts)
foreach ($need in @("source-counts", "bucket-breakdown", "payroll-control", "lineage-export", "reject-register", "checksum-manifest", "sample-results", "dual-control-signoff")) {
    if ($arts -notcontains $need) { $failures += "evidence missing artifact $need" }
}

if (-not $m.sampling.businessSignOffRequired) { $failures += "businessSignOffRequired must be true" }
if ([int]$m.sampling.statisticalMin -lt 1) { $failures += "statisticalMin invalid" }
if (-not $m.agencyWaveSignOff.perWaveRequired) { $failures += "perWaveRequired must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace($sig.contentHash)) { $failures += "signing contentHash required" }
if ($sig.signedByRole -eq $sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["MIG-003"] -ne "Satisfied") { $failures += "MIG-003 must be Satisfied" }
if ($blockerMap["OBS-003"] -ne "Satisfied") { $failures += "OBS-003 must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["live-agency-recon"] -ne "Blocked") { $failures += "live-agency-recon must be Blocked" }
if ($blockerMap["live-independent-sql"] -ne "Blocked") { $failures += "live-independent-sql must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if (-not [string]::IsNullOrEmpty($expect) -and $obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$full = Assert-FixtureExpected $m.simulationPaths.fullAccountedPass "PASS"
$gap = Assert-FixtureExpected $m.simulationPaths.unexplainedGapFail "FAIL"
$fin = Assert-FixtureExpected $m.simulationPaths.financialVarianceFail "FAIL"
$dual = Assert-FixtureExpected $m.simulationPaths.dualControlMissingFail "FAIL"
$tamp = Assert-FixtureExpected $m.simulationPaths.tamperEvidenceFail "FAIL"
$chk = Assert-FixtureExpected $m.simulationPaths.checksumReconPass "PASS"
$biz = Assert-FixtureExpected $m.simulationPaths.businessSampleSignoff "PASS"
$stat = Assert-FixtureExpected $m.simulationPaths.statisticalSamplePass "PASS"
$pkg = Assert-FixtureExpected $m.simulationPaths.evidencePackageStub $null
$wave = Assert-FixtureExpected $m.simulationPaths.agencyWaveSignoff "PASS"

if ($null -ne $full) {
    $sum = [int]$full.migrated + [int]$full.rejected + [int]$full.deferred
    if ($sum -ne [int]$full.sourceCount) { $failures += "full-accounted buckets must equal sourceCount" }
    if ([int]$full.unexplainedGap -ne 0) { $failures += "full-accounted unexplainedGap must be 0" }
    if ($full.rejectedReasonsPresent -ne $true -or $full.deferredReasonsPresent -ne $true) {
        $failures += "full-accounted must declare reasons present"
    }
}

if ($null -ne $gap) {
    if ([int]$gap.unexplainedGap -le 0) { $failures += "gap fixture must have unexplainedGap > 0" }
    $sum = [int]$gap.migrated + [int]$gap.rejected + [int]$gap.deferred + [int]$gap.unexplainedGap
    if ($sum -ne [int]$gap.sourceCount) { $failures += "gap fixture arithmetic inconsistent" }
}

if ($null -ne $fin) {
    if ([double]$fin.variance -eq 0) { $failures += "financial FAIL fixture needs non-zero variance" }
    if (-not [string]::IsNullOrWhiteSpace([string]$fin.explainedAdjustmentReason)) {
        $failures += "financial FAIL fixture must lack explained reason"
    }
}

if ($null -ne $dual) {
    if (@($dual.signers).Count -ge [int]$m.dualControl.minSigners -and $dual.distinctRoles -eq $true) {
        $failures += "dual-missing FAIL fixture must not satisfy dual control"
    }
}

if ($null -ne $tamp) {
    if ($tamp.declaredContentHash -eq $tamp.recomputedContentHash) {
        $failures += "tamper fixture hashes must differ"
    }
}

if ($null -ne $chk) {
    if ($chk.allMatched -ne $true) { $failures += "checksum recon must allMatched" }
    foreach ($f in $chk.files) {
        if ($f.match -ne $true) { $failures += "checksum file $($f.path) not matched" }
    }
}

if ($null -ne $biz) {
    if ($biz.approved -ne $true) { $failures += "business sample must be approved" }
    if ($biz.signedByRole -ne "agency-data-owner") { $failures += "business sample signer role" }
}

if ($null -ne $stat) {
    if ([int]$stat.sampleSize -lt [int]$m.sampling.statisticalMin) {
        $failures += "statistical sample below statisticalMin"
    }
    if ($stat.liveIndependentSql -eq $true) {
        $failures += "liveIndependentSql must be false while Blocked"
    }
}

if ($null -ne $pkg) {
    if ($pkg.immutable -ne $true) { $failures += "package stub must be immutable" }
    $pkgArts = @($pkg.artifacts)
    foreach ($need in $arts) {
        if ($pkgArts -notcontains $need) { $failures += "package stub missing $need" }
    }
    if ([string]::IsNullOrWhiteSpace($pkg.contentHash)) { $failures += "package stub contentHash required" }
}

if ($null -ne $wave) {
    $roles = @($wave.signers | ForEach-Object { $_.role })
    foreach ($need in @($m.agencyWaveSignOff.roles)) {
        if ($roles -notcontains $need) { $failures += "wave signoff missing role $need" }
    }
    if ($wave.liveAgencyExtract -eq $true) {
        $failures += "liveAgencyExtract must be false while Blocked"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-005"
    ok = ($failures.Count -eq 0)
    bucketCount = @($m.accountedBuckets).Count
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

Write-Host "PASS: MIG-005 evidence validate"
exit 0
