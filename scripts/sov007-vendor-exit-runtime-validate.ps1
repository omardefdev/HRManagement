# SOV-007: runtime/production vendor-exit binding validate.
param(
    [string]$MatrixPath = "docs/sov-vendor/vendor-exit-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov007/out"
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

if ($m.card -ne "SOV-007") { $failures += "card must be SOV-007" }

foreach ($need in @(
    @{ Id = "SUP-001"; Status = "Satisfied" },
    @{ Id = "OPS-005"; Status = "Satisfied" },
    @{ Id = "DB-001"; Status = "Satisfied" },
    @{ Id = "SOV-006"; Status = "Satisfied" },
    @{ Id = "live-clean-room-rebuild"; Status = "Blocked" },
    @{ Id = "live-vendor-exit-drill"; Status = "Blocked" },
    @{ Id = "live-full-export-import"; Status = "Blocked" },
    @{ Id = "live-vendor-destruction-attestation"; Status = "Blocked" },
    @{ Id = "VEN-001"; Status = "NotStarted" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

if ($m.ownership.ipSourceBuildIacSbomDocsRequired -ne $true) { $failures += "ipSourceBuildIacSbomDocsRequired must be true" }
if ($m.escrowLicenses.escrowRequired -ne $true) { $failures += "escrowRequired must be true" }
if ($m.openExport.fullDataExportRequired -ne $true) { $failures += "fullDataExportRequired must be true" }
if ($m.criticalAlternates.noCriticalWithoutAlternateOrException -ne $true) { $failures += "noCriticalWithoutAlternateOrException must be true" }
if ($m.cleanRoomRebuild.ownedArtifactsOnlyRequired -ne $true) { $failures += "ownedArtifactsOnlyRequired must be true" }
if ($m.destructionAttestation.priorVendorProofRequired -ne $true) { $failures += "priorVendorProofRequired must be true" }
if ($m.mockSandbox.liveCleanRoomRebuildForbidden -ne $true) { $failures += "liveCleanRoomRebuildForbidden must be true" }
if ($m.mockSandbox.liveVendorExitDrillForbidden -ne $true) { $failures += "liveVendorExitDrillForbidden must be true" }

$sig = $m.signing
if ($null -eq $sig) { $failures += "signing missing" }
else {
    if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signing owner equals reviewer" }
    $version = $sig.vendorExitPolicyVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.vendorExitPolicyVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$ru = Get-Json "testdata/sov007/scenarios/rebuild-unowned-artifacts-fail.json"
if ($ru.expectedValidate -ne "FAIL" -or $ru.ownedArtifactsOnly -ne $false) { $failures += "rebuild-unowned-artifacts-fail invalid" }
$ie = Get-Json "testdata/sov007/scenarios/incomplete-export-fail.json"
if ($ie.expectedValidate -ne "FAIL" -or $ie.fullExportPresent -ne $false) { $failures += "incomplete-export-fail invalid" }
$cn = Get-Json "testdata/sov007/scenarios/critical-no-alternate-fail.json"
if ($cn.expectedValidate -ne "FAIL" -or $cn.criticalWithoutAlternate -ne $true) { $failures += "critical-no-alternate-fail invalid" }
$du = Get-Json "testdata/sov007/scenarios/destruction-unattested-fail.json"
if ($du.expectedValidate -ne "FAIL" -or $du.attested -ne $false) { $failures += "destruction-unattested-fail invalid" }
$lv = Get-Json "testdata/sov007/scenarios/live-vendor-claim-fail.json"
if ($lv.expectedValidate -ne "FAIL" -or $lv.liveVendorClaim -ne $true) { $failures += "live-vendor-claim-fail invalid" }

$ledger = Get-Json "testdata/sov007/harness/vendor-exit-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "vendor-exit-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveVendor -eq $true) { $failures += "ledger run $($r.runId) liveVendor must be false" }
}

# STR-006 sync: SOV-007 Satisfied (vendor-exit binding; live exit drill = VEN-001)
$str6 = Get-Json "docs/str-ownership/ownership-matrix.json"
$sov7b = @($str6.blockers | Where-Object { $_.id -eq "SOV-007" } | Select-Object -First 1)
if ($sov7b.Count -eq 0) { $failures += "STR-006 matrix missing SOV-007 blocker" }
elseif ([string]$sov7b[0].status -ne "Satisfied") {
    $failures += "STR-006 matrix SOV-007 must be Satisfied (got $($sov7b[0].status))"
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
    $sv = $cfg.SovVendor
    if ($null -eq $sv) { $failures += "$rel missing SovVendor"; continue }
    if ($sv.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($sv.LiveVendorClaim -eq $true) { $failures += "$rel LiveVendorClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-007"
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

Write-Host "PASS: SOV-007 vendor-exit runtime validate"
exit 0
