# ARC-002: validate reference-architecture contract matrix and harness fixtures.
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

$journeyPackIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("ContextsOwnership", "MultiSiteTopology", "CouplingFitness", "ClaimGate")) {
    if ($journeyPackIds -notcontains $need) { $failures += "missing journey pack $need" }
}

if ($m.boundedContexts.required -ne $true) { $failures += "boundedContexts.required must be true" }
if ($m.boundedContexts.everyContextHasOwnerAndRationaleRequired -ne $true) { $failures += "everyContextHasOwnerAndRationaleRequired must be true" }
if ($m.boundedContexts.noUnjustifiedDistributedComplexityRequired -ne $true) { $failures += "noUnjustifiedDistributedComplexityRequired must be true" }
if ($m.syncVsEvent.required -ne $true) { $failures += "syncVsEvent.required must be true" }
if ($m.syncVsEvent.interactionStyleDocumentedRequired -ne $true) { $failures += "interactionStyleDocumentedRequired must be true" }
if ($m.dataOwnership.required -ne $true) { $failures += "dataOwnership.required must be true" }
if ($m.dataOwnership.singleWriterPerAggregateRequired -ne $true) { $failures += "singleWriterPerAggregateRequired must be true" }
if ($m.multiSiteProfiles.required -ne $true) { $failures += "multiSiteProfiles.required must be true" }
if ($m.multiSiteProfiles.sameArtifactPrimaryAndDrRequired -ne $true) { $failures += "sameArtifactPrimaryAndDrRequired must be true" }
if ($m.multiSiteProfiles.primaryAndDrProfilesRequired -ne $true) { $failures += "primaryAndDrProfilesRequired must be true" }
if ($m.tenancyBoundaries.required -ne $true) { $failures += "tenancyBoundaries.required must be true" }
if ($m.tenancyBoundaries.tenantModelAlignedRequired -ne $true) { $failures += "tenantModelAlignedRequired must be true" }
if ($m.controlDataPlanes.required -ne $true) { $failures += "controlDataPlanes.required must be true" }
if ($m.controlDataPlanes.planesSeparatedRequired -ne $true) { $failures += "planesSeparatedRequired must be true" }
if ($m.supportedTopologies.required -ne $true) { $failures += "supportedTopologies.required must be true" }
if ($m.supportedTopologies.unsupportedTopologyRejectedRequired -ne $true) { $failures += "unsupportedTopologyRejectedRequired must be true" }
if ($m.supportedTopologies.envelopeValidatedRequired -ne $true) { $failures += "envelopeValidatedRequired must be true" }
if ($m.couplingGuardrails.required -ne $true) { $failures += "couplingGuardrails.required must be true" }
if ($m.couplingGuardrails.noDirectDbCrossBoundaryRequired -ne $true) { $failures += "noDirectDbCrossBoundaryRequired must be true" }
if ($m.couplingGuardrails.noDomainCouplingAcrossContextsRequired -ne $true) { $failures += "noDomainCouplingAcrossContextsRequired must be true" }
if ($m.fitnessEnvelope.required -ne $true) { $failures += "fitnessEnvelope.required must be true" }
if ($m.fitnessEnvelope.referenceImplementationRequired -ne $true) { $failures += "referenceImplementationRequired must be true" }
if ($m.fitnessEnvelope.labFitnessHarnessRequired -ne $true) { $failures += "labFitnessHarnessRequired must be true" }

if ($m.audit.required -ne $true) { $failures += "audit.required must be true" }
foreach ($need in @("contextId", "ownerRole", "interactionStyle", "dataOwnerRef", "siteProfileRef", "topologyRef", "couplingScanRef", "fitnessRef", "actor", "occurredAt", "evidenceRef", "outcome", "rationaleRef")) {
    if (@($m.audit.requiredFields) -notcontains $need) { $failures += "audit missing $need" }
}

if ($m.mockSandbox.labStub -ne $true) { $failures += "labStub must be true" }
if ($m.mockSandbox.liveTwoSiteDeploymentForbidden -ne $true) { $failures += "liveTwoSiteDeploymentForbidden must be true" }
if ($m.mockSandbox.liveArchitectureFitnessEnvelopeForbidden -ne $true) { $failures += "liveArchitectureFitnessEnvelopeForbidden must be true" }
if ($m.mockSandbox.liveCouplingBoundaryScanForbidden -ne $true) { $failures += "liveCouplingBoundaryScanForbidden must be true" }
if ($m.mockSandbox.liveReferenceImplementationSignoffForbidden -ne $true) { $failures += "liveReferenceImplementationSignoffForbidden must be true" }

$sig = $m.signing
if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash)) { $failures += "signing contentHash required" }
if ([string]$sig.signedByRole -eq [string]$sig.reviewerRole) { $failures += "signedByRole and reviewerRole must differ" }

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

$contexts = @($m.contexts)
if ($contexts.Count -lt 6) { $failures += "contexts need >= 6 lab entries" }
foreach ($needId in @("gateway-edge", "hr-core", "payroll", "auth-identity", "file-storage", "integration-messaging")) {
    if (-not ($contexts | Where-Object { $_.id -eq $needId })) { $failures += "context $needId missing" }
}
$profiles = @($m.siteProfiles)
if (-not ($profiles | Where-Object { $_.role -eq "primary" })) { $failures += "siteProfiles missing primary" }
if (-not ($profiles | Where-Object { $_.role -eq "dr" })) { $failures += "siteProfiles missing dr" }
if (@($m.unsupportedTopologies).Count -lt 1) { $failures += "unsupportedTopologies need >= 1 entry" }

foreach ($rel in @(
    "testdata/arc002/scenarios/unjustified-distribution-fail.json",
    "testdata/arc002/scenarios/artifact-drift-primary-dr-fail.json",
    "testdata/arc002/scenarios/unsupported-topology-fail.json",
    "testdata/arc002/scenarios/direct-db-cross-boundary-fail.json",
    "testdata/arc002/scenarios/domain-coupling-fail.json",
    "testdata/arc002/scenarios/context-without-owner-fail.json",
    "testdata/arc002/scenarios/envelope-unvalidated-fail.json",
    "testdata/arc002/scenarios/live-reference-claim-fail.json",
    "testdata/arc002/scenarios/context-owned-pass.json",
    "testdata/arc002/scenarios/same-artifact-primary-dr-pass.json",
    "testdata/arc002/scenarios/supported-topology-pass.json",
    "testdata/arc002/scenarios/coupling-guardrail-pass.json",
    "testdata/arc002/scenarios/fitness-lab-pass.json",
    "testdata/arc002/scenarios/tenancy-aligned-pass.json",
    "testdata/arc002/scenarios/planes-separated-pass.json",
    "testdata/arc002/audit/reference-audit-pass.json",
    "testdata/arc002/harness/reference-run-ledger.json"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$ud = Get-Json "testdata/arc002/scenarios/unjustified-distribution-fail.json"
if ($ud.expectedValidate -ne "FAIL" -or $ud.unjustifiedDistributionPresent -ne $true) { $failures += "unjustified-distribution-fail invalid" }
$ad = Get-Json "testdata/arc002/scenarios/artifact-drift-primary-dr-fail.json"
if ($ad.expectedValidate -ne "FAIL" -or $ad.sameArtifactPrimaryAndDr -ne $false) { $failures += "artifact-drift-primary-dr-fail invalid" }
$ut = Get-Json "testdata/arc002/scenarios/unsupported-topology-fail.json"
if ($ut.expectedValidate -ne "FAIL" -or $ut.unsupportedTopologyPresent -ne $true) { $failures += "unsupported-topology-fail invalid" }
$db = Get-Json "testdata/arc002/scenarios/direct-db-cross-boundary-fail.json"
if ($db.expectedValidate -ne "FAIL" -or $db.directDbCrossBoundary -ne $true) { $failures += "direct-db-cross-boundary-fail invalid" }
$dc = Get-Json "testdata/arc002/scenarios/domain-coupling-fail.json"
if ($dc.expectedValidate -ne "FAIL" -or $dc.domainCouplingAcrossContexts -ne $true) { $failures += "domain-coupling-fail invalid" }
$co = Get-Json "testdata/arc002/scenarios/context-without-owner-fail.json"
if ($co.expectedValidate -ne "FAIL" -or $co.everyContextOwned -ne $false) { $failures += "context-without-owner-fail invalid" }
$eu = Get-Json "testdata/arc002/scenarios/envelope-unvalidated-fail.json"
if ($eu.expectedValidate -ne "FAIL" -or $eu.envelopeValidated -ne $false) { $failures += "envelope-unvalidated-fail invalid" }
$lr = Get-Json "testdata/arc002/scenarios/live-reference-claim-fail.json"
if ($lr.expectedValidate -ne "FAIL" -or $lr.liveReferenceClaim -ne $true) { $failures += "live-reference-claim-fail invalid" }

$cp = Get-Json "testdata/arc002/scenarios/context-owned-pass.json"
if ($cp.expectedValidate -ne "PASS" -or $cp.everyContextOwned -ne $true) { $failures += "context-owned-pass invalid" }
$sa = Get-Json "testdata/arc002/scenarios/same-artifact-primary-dr-pass.json"
if ($sa.expectedValidate -ne "PASS" -or $sa.sameArtifactPrimaryAndDr -ne $true) { $failures += "same-artifact-primary-dr-pass invalid" }
$st = Get-Json "testdata/arc002/scenarios/supported-topology-pass.json"
if ($st.expectedValidate -ne "PASS" -or $st.envelopeValidated -ne $true) { $failures += "supported-topology-pass invalid" }
$cg = Get-Json "testdata/arc002/scenarios/coupling-guardrail-pass.json"
if ($cg.expectedValidate -ne "PASS" -or $cg.directDbCrossBoundary -eq $true) { $failures += "coupling-guardrail-pass invalid" }
$fl = Get-Json "testdata/arc002/scenarios/fitness-lab-pass.json"
if ($fl.expectedValidate -ne "PASS" -or $fl.labFitnessHarnessPresent -ne $true) { $failures += "fitness-lab-pass invalid" }

$audit = Get-Json "testdata/arc002/audit/reference-audit-pass.json"
if ($audit.expectedValidate -ne "PASS" -or @($audit.entries).Count -lt 1) { $failures += "reference-audit-pass invalid" }
$ledger = Get-Json "testdata/arc002/harness/reference-run-ledger.json"
$runs = @($ledger.runs)
if ($runs.Count -lt 2) { $failures += "reference-run-ledger needs >= 2 runs" }
foreach ($r in $runs) {
    if ($r.liveReference -eq $true) { $failures += "ledger run $($r.runId) liveReference must be false" }
}

foreach ($rel in @(
    "docs/ADR-ARC-002-reference-architecture.md",
    "docs/ARC_002_JOURNEY_MATRIX.md",
    "docs/ARC_002_CONTEXTS.md",
    "docs/ARC_002_SYNC_EVENT.md",
    "docs/ARC_002_DATA_OWNERSHIP.md",
    "docs/ARC_002_MULTI_SITE.md",
    "docs/ARC_002_TENANCY.md",
    "docs/ARC_002_TOPOLOGY.md",
    "docs/ARC_002_COUPLING.md",
    "docs/ARC_002_FITNESS.md",
    "docs/ARC_002_READINESS_CHECKLIST.md",
    "docs/ARC_002_UAT_PACK.md",
    "docs/ARC_002_VERIFICATION.md"
)) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) { $failures += "missing $rel" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "ARC-002"
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

Write-Host "PASS: ARC-002 reference pack validate"
exit 0
