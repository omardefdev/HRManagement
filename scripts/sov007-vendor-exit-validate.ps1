# SOV-007: validate vendor-exit contract matrix and harness fixtures.
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

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("OwnershipEscrow", "ExportAlternates", "CleanRoomExit", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.ownership.required -ne $true) { $failures += "ownership.required must be true" }
if ($m.ownership.ipSourceBuildIacSbomDocsRequired -ne $true) { $failures += "ipSourceBuildIacSbomDocsRequired must be true" }
if ($m.escrowLicenses.required -ne $true) { $failures += "escrowLicenses.required must be true" }
if ($m.escrowLicenses.escrowRequired -ne $true) { $failures += "escrowRequired must be true" }
if ($m.escrowLicenses.licenseRightsRequired -ne $true) { $failures += "licenseRightsRequired must be true" }
if ($m.escrowLicenses.subcontractorDisclosureRequired -ne $true) { $failures += "subcontractorDisclosureRequired must be true" }
if ($m.openExport.required -ne $true) { $failures += "openExport.required must be true" }
if ($m.openExport.fullDataExportRequired -ne $true) { $failures += "fullDataExportRequired must be true" }
if ($m.openExport.attachmentsExportRequired -ne $true) { $failures += "attachmentsExportRequired must be true" }
if ($m.openExport.contractsExportRequired -ne $true) { $failures += "contractsExportRequired must be true" }
if ($m.openExport.documentedFormatsRequired -ne $true) { $failures += "documentedFormatsRequired must be true" }
if ($m.criticalAlternates.required -ne $true) { $failures += "criticalAlternates.required must be true" }
if ($m.criticalAlternates.noCriticalWithoutAlternateOrException -ne $true) { $failures += "noCriticalWithoutAlternateOrException must be true" }
if ($m.cleanRoomRebuild.required -ne $true) { $failures += "cleanRoomRebuild.required must be true" }
if ($m.cleanRoomRebuild.ownedArtifactsOnlyRequired -ne $true) { $failures += "ownedArtifactsOnlyRequired must be true" }
if ($m.cleanRoomRebuild.alternateSiteRequired -ne $true) { $failures += "alternateSiteRequired must be true" }
if ($m.knowledgeTransfer.required -ne $true) { $failures += "knowledgeTransfer.required must be true" }
if ($m.exitAssistance.required -ne $true) { $failures += "exitAssistance.required must be true" }
if ($m.destructionAttestation.required -ne $true) { $failures += "destructionAttestation.required must be true" }
if ($m.destructionAttestation.priorVendorProofRequired -ne $true) { $failures += "priorVendorProofRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("artifactId", "ownershipType", "exportRef", "alternateRef", "rebuildRef", "actor", "occurredAt", "evidenceRef", "outcome", "destructionRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveCleanRoomRebuildForbidden -ne $true) { $failures += "liveCleanRoomRebuildForbidden must be true" }
if ($m.mockSandbox.liveVendorExitDrillForbidden -ne $true) { $failures += "liveVendorExitDrillForbidden must be true" }
if ($m.mockSandbox.liveFullExportImportForbidden -ne $true) { $failures += "liveFullExportImportForbidden must be true" }
if ($m.mockSandbox.liveVendorDestructionAttestationForbidden -ne $true) { $failures += "liveVendorDestructionAttestationForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

foreach ($rel in @(
    "testdata/sov007/scenarios/rebuild-unowned-artifacts-fail.json",
    "testdata/sov007/scenarios/incomplete-export-fail.json",
    "testdata/sov007/scenarios/critical-no-alternate-fail.json",
    "testdata/sov007/scenarios/escrow-missing-fail.json",
    "testdata/sov007/scenarios/destruction-unattested-fail.json",
    "testdata/sov007/scenarios/knowledge-transfer-missing-fail.json",
    "testdata/sov007/scenarios/exit-assistance-missing-fail.json",
    "testdata/sov007/scenarios/live-vendor-claim-fail.json",
    "testdata/sov007/scenarios/owned-artifacts-pass.json",
    "testdata/sov007/scenarios/full-export-pass.json",
    "testdata/sov007/scenarios/critical-alternate-pass.json",
    "testdata/sov007/scenarios/escrow-license-pass.json",
    "testdata/sov007/scenarios/clean-room-lab-pass.json",
    "testdata/sov007/scenarios/destruction-attested-pass.json",
    "testdata/sov007/scenarios/knowledge-exit-pass.json",
    "testdata/sov007/audit/vendor-exit-audit-pass.json",
    "testdata/sov007/harness/vendor-exit-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$ru = Get-Json "testdata/sov007/scenarios/rebuild-unowned-artifacts-fail.json"
if ($ru.expectedValidate -ne "FAIL" -or $ru.ownedArtifactsOnly -ne $false) { $failures += "rebuild-unowned-artifacts-fail invalid" }
$ie = Get-Json "testdata/sov007/scenarios/incomplete-export-fail.json"
if ($ie.expectedValidate -ne "FAIL" -or $ie.fullExportPresent -ne $false) { $failures += "incomplete-export-fail invalid" }
$cn = Get-Json "testdata/sov007/scenarios/critical-no-alternate-fail.json"
if ($cn.expectedValidate -ne "FAIL" -or $cn.criticalWithoutAlternate -ne $true) { $failures += "critical-no-alternate-fail invalid" }
$em = Get-Json "testdata/sov007/scenarios/escrow-missing-fail.json"
if ($em.expectedValidate -ne "FAIL" -or $em.escrowPresent -ne $false) { $failures += "escrow-missing-fail invalid" }
$du = Get-Json "testdata/sov007/scenarios/destruction-unattested-fail.json"
if ($du.expectedValidate -ne "FAIL" -or $du.attested -ne $false) { $failures += "destruction-unattested-fail invalid" }
$kt = Get-Json "testdata/sov007/scenarios/knowledge-transfer-missing-fail.json"
if ($kt.expectedValidate -ne "FAIL" -or $kt.transferPresent -ne $false) { $failures += "knowledge-transfer-missing-fail invalid" }
$ea = Get-Json "testdata/sov007/scenarios/exit-assistance-missing-fail.json"
if ($ea.expectedValidate -ne "FAIL" -or $ea.assistancePresent -ne $false) { $failures += "exit-assistance-missing-fail invalid" }
$lv = Get-Json "testdata/sov007/scenarios/live-vendor-claim-fail.json"
if ($lv.expectedValidate -ne "FAIL" -or $lv.liveVendorClaim -ne $true) { $failures += "live-vendor-claim-fail invalid" }

$oa = Get-Json "testdata/sov007/scenarios/owned-artifacts-pass.json"
if ($oa.expectedValidate -ne "PASS" -or $oa.ownedArtifactsPresent -ne $true) { $failures += "owned-artifacts-pass invalid" }
$fe = Get-Json "testdata/sov007/scenarios/full-export-pass.json"
if ($fe.expectedValidate -ne "PASS" -or $fe.fullExportPresent -ne $true) { $failures += "full-export-pass invalid" }
$ca = Get-Json "testdata/sov007/scenarios/critical-alternate-pass.json"
if ($ca.expectedValidate -ne "PASS" -or $ca.criticalWithoutAlternate -eq $true) { $failures += "critical-alternate-pass invalid" }
$el = Get-Json "testdata/sov007/scenarios/escrow-license-pass.json"
if ($el.expectedValidate -ne "PASS" -or $el.escrowPresent -ne $true) { $failures += "escrow-license-pass invalid" }
$cr = Get-Json "testdata/sov007/scenarios/clean-room-lab-pass.json"
if ($cr.expectedValidate -ne "PASS" -or $cr.ownedArtifactsOnly -ne $true) { $failures += "clean-room-lab-pass invalid" }
$da = Get-Json "testdata/sov007/scenarios/destruction-attested-pass.json"
if ($da.expectedValidate -ne "PASS" -or $da.attested -ne $true) { $failures += "destruction-attested-pass invalid" }
$ke = Get-Json "testdata/sov007/scenarios/knowledge-exit-pass.json"
if ($ke.expectedValidate -ne "PASS" -or $ke.transferPresent -ne $true) { $failures += "knowledge-exit-pass invalid" }

$audit = Get-Json "testdata/sov007/audit/vendor-exit-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "vendor-exit-audit-pass invalid" }
$ledger = Get-Json "testdata/sov007/harness/vendor-exit-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "vendor-exit-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveVendor -eq $true) { $failures += "ledger run $($r.runId) liveVendor must be false" }
}

foreach ($rel in @(
    "docs/ADR-SOV-007-vendor-exit.md",
    "docs/SOV_007_JOURNEY_MATRIX.md",
    "docs/SOV_007_OWNERSHIP.md",
    "docs/SOV_007_ESCROW.md",
    "docs/SOV_007_EXPORT.md",
    "docs/SOV_007_ALTERNATES.md",
    "docs/SOV_007_CLEAN_ROOM.md",
    "docs/SOV_007_KNOWLEDGE.md",
    "docs/SOV_007_DESTRUCTION.md",
    "docs/SOV_007_READINESS_CHECKLIST.md",
    "docs/SOV_007_UAT_PACK.md",
    "docs/SOV_007_VERIFICATION.md"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-007"
    mode = "pack-validate"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "pack-validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: SOV-007 vendor-exit pack validate"
exit 0
