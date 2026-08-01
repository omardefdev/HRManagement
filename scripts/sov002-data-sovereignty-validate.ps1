# SOV-002: validate data-sovereignty contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/sov002-data-sovereignty-validate.ps1

param(
    [string]$MatrixPath = "docs/sov-data/data-sovereignty-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/sov002/out"
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

if ($m.card -ne "SOV-002") { $failures += "card must be SOV-002" }

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("InventoryFlow", "ResidencyEgress", "SupportDestruction", "AuditClaim")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.dataCopyInventory.required -ne $true) { $failures += "dataCopyInventory.required must be true" }
if ($m.dataCopyInventory.classificationRequired -ne $true) { $failures += "classificationRequired must be true" }
if ($m.dataCopyInventory.approvedLocationRequired -ne $true) { $failures += "approvedLocationRequired must be true" }
foreach ($need in @("db", "object", "backups", "logs", "traces", "ci-artifacts", "support-dumps")) {
    if (@($m.dataCopyInventory.copyTypes) -notcontains $need) { $failures += "missing copyType $need" }
}
if ($m.dataFlowMap.required -ne $true) { $failures += "dataFlowMap.required must be true" }
if ($m.residency.required -ne $true) { $failures += "residency.required must be true" }
if ($m.residency.approvedRegionsRequired -ne $true) { $failures += "approvedRegionsRequired must be true" }
if ($m.residency.outsideScopeForbidden -ne $true) { $failures += "outsideScopeForbidden must be true" }
if ($m.egress.required -ne $true) { $failures += "egress.required must be true" }
if ($m.egress.denyByDefault -ne $true) { $failures += "denyByDefault must be true" }
if ($m.remoteSupport.required -ne $true) { $failures += "remoteSupport.required must be true" }
if ($m.remoteSupport.jitRequired -ne $true) { $failures += "jitRequired must be true" }
if ($m.remoteSupport.loggedRequired -ne $true) { $failures += "loggedRequired must be true" }
if ($m.remoteSupport.approvalRequired -ne $true) { $failures += "approvalRequired must be true" }
if ($m.subcontractors.required -ne $true) { $failures += "subcontractors.required must be true" }
if ($m.subcontractors.inventoryRequired -ne $true) { $failures += "subcontractors.inventoryRequired must be true" }
if ($m.telemetry.required -ne $true) { $failures += "telemetry.required must be true" }
if ($m.telemetry.residencyBound -ne $true) { $failures += "residencyBound must be true" }
if ($m.destruction.required -ne $true) { $failures += "destruction.required must be true" }
if ($m.destruction.certificateRequired -ne $true) { $failures += "certificateRequired must be true" }
if ($m.lawfulDisclosure.required -ne $true) { $failures += "lawfulDisclosure.required must be true" }
if ($m.lawfulDisclosure.loggedDisclosureRequired -ne $true) { $failures += "loggedDisclosureRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("copyId", "classification", "location", "egressDecision", "supportTicketId", "actor", "occurredAt", "evidenceRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveEgressForbidden -ne $true) { $failures += "liveEgressForbidden must be true" }
if ($m.mockSandbox.liveSupportJitForbidden -ne $true) { $failures += "liveSupportJitForbidden must be true" }
if ($m.mockSandbox.liveDeletionSystemForbidden -ne $true) { $failures += "liveDeletionSystemForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

foreach ($need in @(
    @{ Id = "GOV-001"; Status = "Satisfied" },
    @{ Id = "OPS-003"; Status = "Satisfied" },
    @{ Id = "STR-002"; Status = "Satisfied" },
    @{ Id = "SOV-001"; Status = "Satisfied" },
    @{ Id = "live-egress-enforcement"; Status = "Blocked" },
    @{ Id = "live-support-jit-workers"; Status = "Blocked" },
    @{ Id = "live-deletion-certificate-system"; Status = "Blocked" },
    @{ Id = "SYR-008"; Status = "NotStarted" },
    @{ Id = "SOV-003"; Status = "Satisfied" },
    @{ Id = "NAT-001"; Status = "NotStarted" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

foreach ($rel in @(
    "testdata/sov002/scenarios/unclassified-copy-fail.json",
    "testdata/sov002/scenarios/unapproved-location-fail.json",
    "testdata/sov002/scenarios/egress-allow-default-fail.json",
    "testdata/sov002/scenarios/support-without-jit-fail.json",
    "testdata/sov002/scenarios/support-without-approval-fail.json",
    "testdata/sov002/scenarios/copy-outside-scope-fail.json",
    "testdata/sov002/scenarios/missing-deletion-cert-fail.json",
    "testdata/sov002/scenarios/live-sovereignty-claim-fail.json",
    "testdata/sov002/scenarios/classified-inventory-pass.json",
    "testdata/sov002/scenarios/approved-residency-pass.json",
    "testdata/sov002/scenarios/egress-deny-default-pass.json",
    "testdata/sov002/scenarios/jit-support-pass.json",
    "testdata/sov002/scenarios/deletion-cert-pass.json",
    "testdata/sov002/scenarios/data-flow-map-pass.json",
    "testdata/sov002/scenarios/supplier-audit-pass.json",
    "testdata/sov002/audit/sovereignty-audit-pass.json",
    "testdata/sov002/harness/sovereignty-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$uc = Get-Json "testdata/sov002/scenarios/unclassified-copy-fail.json"
if ($uc.expectedValidate -ne "FAIL" -or $uc.classified -eq $true) { $failures += "unclassified-copy-fail invalid" }
$ul = Get-Json "testdata/sov002/scenarios/unapproved-location-fail.json"
if ($ul.expectedValidate -ne "FAIL" -or $ul.approvedLocation -eq $true) { $failures += "unapproved-location-fail invalid" }
$ea = Get-Json "testdata/sov002/scenarios/egress-allow-default-fail.json"
if ($ea.expectedValidate -ne "FAIL" -or $ea.denyByDefault -eq $true) { $failures += "egress-allow-default-fail invalid" }
$sj = Get-Json "testdata/sov002/scenarios/support-without-jit-fail.json"
if ($sj.expectedValidate -ne "FAIL" -or $sj.jit -eq $true) { $failures += "support-without-jit-fail invalid" }
$sa = Get-Json "testdata/sov002/scenarios/support-without-approval-fail.json"
if ($sa.expectedValidate -ne "FAIL" -or $sa.approved -eq $true) { $failures += "support-without-approval-fail invalid" }
$co = Get-Json "testdata/sov002/scenarios/copy-outside-scope-fail.json"
if ($co.expectedValidate -ne "FAIL" -or $co.insideApprovedRegion -eq $true) { $failures += "copy-outside-scope-fail invalid" }
$md = Get-Json "testdata/sov002/scenarios/missing-deletion-cert-fail.json"
if ($md.expectedValidate -ne "FAIL" -or $md.certificatePresent -eq $true) { $failures += "missing-deletion-cert-fail invalid" }
$ls = Get-Json "testdata/sov002/scenarios/live-sovereignty-claim-fail.json"
if ($ls.expectedValidate -ne "FAIL" -or $ls.liveSovereigntyClaim -ne $true) { $failures += "live-sovereignty-claim-fail invalid" }

$ci = Get-Json "testdata/sov002/scenarios/classified-inventory-pass.json"
if ($ci.expectedValidate -ne "PASS" -or $ci.classified -ne $true -or $ci.approvedLocation -ne $true) { $failures += "classified-inventory-pass invalid" }
$ar = Get-Json "testdata/sov002/scenarios/approved-residency-pass.json"
if ($ar.expectedValidate -ne "PASS" -or $ar.insideApprovedRegion -ne $true) { $failures += "approved-residency-pass invalid" }
$ed = Get-Json "testdata/sov002/scenarios/egress-deny-default-pass.json"
if ($ed.expectedValidate -ne "PASS" -or $ed.denyByDefault -ne $true) { $failures += "egress-deny-default-pass invalid" }
$jp = Get-Json "testdata/sov002/scenarios/jit-support-pass.json"
if ($jp.expectedValidate -ne "PASS" -or $jp.jit -ne $true -or $jp.logged -ne $true -or $jp.approved -ne $true) { $failures += "jit-support-pass invalid" }
$dc = Get-Json "testdata/sov002/scenarios/deletion-cert-pass.json"
if ($dc.expectedValidate -ne "PASS" -or $dc.certificatePresent -ne $true) { $failures += "deletion-cert-pass invalid" }
$df = Get-Json "testdata/sov002/scenarios/data-flow-map-pass.json"
if ($df.expectedValidate -ne "PASS" -or $df.mapPresent -ne $true) { $failures += "data-flow-map-pass invalid" }
$su = Get-Json "testdata/sov002/scenarios/supplier-audit-pass.json"
if ($su.expectedValidate -ne "PASS" -or $su.supplierAudited -ne $true) { $failures += "supplier-audit-pass invalid" }

$audit = Get-Json "testdata/sov002/audit/sovereignty-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "sovereignty-audit-pass invalid" }
$ledger = Get-Json "testdata/sov002/harness/sovereignty-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "sovereignty-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveSovereignty -eq $true) { $failures += "ledger run $($r.runId) liveSovereignty must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "SOV-002"
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

Write-Host "PASS: SOV-002 data-sovereignty pack validate"
exit 0
