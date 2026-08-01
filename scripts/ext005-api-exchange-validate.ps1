# EXT-005: validate API/exchange contract matrix and harness fixtures.
# Usage:
#   powershell -File scripts/ext005-api-exchange-validate.ps1

param(
    [string]$MatrixPath = "docs/ext-api/api-exchange-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/ext005/out"
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

if ($m.card -ne "EXT-005") { $failures += "card must be EXT-005" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("CatalogPublish", "ContractOwnerSla", "BackwardCompatMeasured", "NoSharedDb", "PurposeBoundExport", "ConsumerDrivenBreak", "SchemaRegistryCompat", "ThrottleAndCorrelation", "DeprecationWindow", "InteropHarness")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

if (-not $m.catalogRequired) { $failures += "catalogRequired must be true" }
$catFields = @($m.requiredCatalogFields)
foreach ($need in @("integrationId", "contractRef", "owner", "sla")) {
    if ($catFields -notcontains $need) { $failures += "catalog field missing $need" }
}

if (-not $m.versioning.semverRequired -or -not $m.versioning.breakingRequiresMajorBump) {
    $failures += "versioning flags"
}
if (-not $m.compatibility.measureBackwardCompat -or -not $m.compatibility.supportNMinus1) {
    $failures += "compatibility flags"
}
if (-not $m.sharedDbForbidden) { $failures += "sharedDbForbidden" }
if (-not $m.purposeBoundExport.consentRequired) { $failures += "consentRequired" }
if (@($m.purposeBoundExport.purposeAllowlist).Count -lt 3) { $failures += "purposeAllowlist too short" }
if ([int]$m.throttling.defaultRpm -le 0) { $failures += "defaultRpm invalid" }
if ([int]$m.throttling.onExceed -ne 429) { $failures += "onExceed must be 429" }
if (-not $m.nonRepudiation.correlationIdRequired -or -not $m.nonRepudiation.auditRequired) {
    $failures += "nonRepudiation flags"
}
if (-not $m.tenant.tenantHeaderRequired -or -not $m.tenant.crossTenantDeny) {
    $failures += "tenant flags"
}
if (-not $m.errorModel.problemDetailsRequired) { $failures += "problemDetailsRequired" }
if ([int]$m.deprecation.noticeDaysMin -lt 90) { $failures += "noticeDaysMin must be >= 90" }
if (-not $m.deprecation.sunsetRequiresMajor) { $failures += "sunsetRequiresMajor" }
if (-not $m.dataMinimization.forbidExcessFields) { $failures += "forbidExcessFields" }
if (-not $m.schemaRegistry.labMockAllowed -or -not $m.schemaRegistry.liveForbidden) {
    $failures += "schemaRegistry flags"
}
if (-not $m.consumerDriven.certifiedConsumersProtected) { $failures += "certifiedConsumersProtected" }
if (-not $m.audit.required) { $failures += "audit.required" }

$auditFields = @($m.audit.requiredFields)
foreach ($need in @("exchangeId", "integrationId", "actor", "tenantId", "outcome", "correlationId", "occurredAt")) {
    if ($auditFields -notcontains $need) { $failures += "audit missing $need" }
}

if (-not $m.mockSandbox.labStub -or -not $m.mockSandbox.liveBusForbidden) {
    $failures += "mockSandbox must be labStub and liveBusForbidden"
}

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
if ($blockerMap["IAM-004"] -ne "Satisfied") { $failures += "IAM-004 must be Satisfied" }
if ($blockerMap["TEN-003"] -ne "Satisfied") { $failures += "TEN-003 must be Satisfied" }
if ($blockerMap["INT-002"] -ne "Satisfied") { $failures += "INT-002 must be Satisfied" }
if ($blockerMap["PROD-007"] -ne "Satisfied") { $failures += "PROD-007 must be Satisfied" }
if ($blockerMap["gov-exchange-bus-live"] -ne "Blocked") { $failures += "gov-exchange-bus-live must be Blocked" }
if ($blockerMap["schema-registry-live"] -ne "Blocked") { $failures += "schema-registry-live must be Blocked" }
if ($blockerMap["partner-cdc-cert"] -ne "Blocked") { $failures += "partner-cdc-cert must be Blocked" }
if ($blockerMap["national-openapi-publish"] -ne "Blocked") { $failures += "national-openapi-publish must be Blocked" }

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

$miss = Assert-FixtureExpected $m.simulationPaths.missingOwnerSlaFail "FAIL"
$brk = Assert-FixtureExpected $m.simulationPaths.breakingWithoutBumpFail "FAIL"
$n1 = Assert-FixtureExpected $m.simulationPaths.n1CompatPass "PASS"
$db = Assert-FixtureExpected $m.simulationPaths.sharedDbFail "FAIL"
$cdc = Assert-FixtureExpected $m.simulationPaths.certifiedConsumerBreakFail "FAIL"
$purp = Assert-FixtureExpected $m.simulationPaths.purposeDeniedExportFail "FAIL"
$sch = Assert-FixtureExpected $m.simulationPaths.schemaIncompatFail "FAIL"
$thr = Assert-FixtureExpected $m.simulationPaths.throttleExceedPass "PASS"
$dep = Assert-FixtureExpected $m.simulationPaths.deprecationTooShortFail "FAIL"
$iop = Assert-FixtureExpected $m.simulationPaths.interopHarnessPass "PASS"
$audit = Assert-FixtureExpected $m.simulationPaths.exchangeAuditPass "PASS"
$ledger = Assert-FixtureExpected $m.simulationPaths.apiExchangeRunLedger $null

if ($null -ne $miss) {
    $ownerEmpty = ($null -eq $miss.owner) -or ([string]::IsNullOrWhiteSpace([string]$miss.owner))
    $slaEmpty = ($null -eq $miss.sla) -or ([string]::IsNullOrWhiteSpace([string]$miss.sla))
    if (-not $ownerEmpty -and -not $slaEmpty) { $failures += "missing-owner-sla FAIL must omit owner or sla" }
}

if ($null -ne $brk) {
    if ($brk.breakingChange -ne $true -or $brk.majorBumped -eq $true) {
        $failures += "breaking-without-bump FAIL incomplete"
    }
}

if ($null -ne $n1) {
    if ($n1.backwardCompatMeasured -ne $true -or $n1.compatResult -ne "pass") {
        $failures += "n1-compat PASS incomplete"
    }
}

if ($null -ne $db) {
    if ($db.sharedDbJoin -ne $true -or $db.treatedAsIntegration -ne $true) {
        $failures += "shared-db FAIL incomplete"
    }
}

if ($null -ne $cdc) {
    if ($cdc.contractBroken -ne $true -or $cdc.majorBumped -eq $true) {
        $failures += "certified-consumer-break FAIL incomplete"
    }
}

if ($null -ne $purp) {
    if (@($m.purposeBoundExport.purposeAllowlist) -contains $purp.purposeCode) {
        $failures += "purpose FAIL must use non-allowlisted purpose"
    }
    if ($purp.exportAttempted -ne $true) { $failures += "purpose FAIL needs exportAttempted" }
}

if ($null -ne $sch) {
    if ($sch.schemaCompatible -eq $true -or $sch.treatedAsPublished -ne $true) {
        $failures += "schema-incompat FAIL incomplete"
    }
}

if ($null -ne $thr) {
    if ([int]$thr.requestsPerMinute -le [int]$m.throttling.defaultRpm) {
        $failures += "throttle PASS rpm must exceed defaultRpm"
    }
    if ([int]$thr.httpStatus -ne 429) { $failures += "throttle PASS needs 429" }
    if ($thr.correlationIdPresent -ne $true) { $failures += "throttle PASS needs correlationId" }
}

if ($null -ne $dep) {
    if ([int]$dep.noticeDays -ge [int]$m.deprecation.noticeDaysMin) {
        $failures += "deprecation FAIL noticeDays must be below min"
    }
    if ($dep.sunsetAttempted -ne $true) { $failures += "deprecation FAIL needs sunsetAttempted" }
}

if ($null -ne $iop) {
    if ($iop.producerOk -ne $true -or $iop.consumerOk -ne $true -or $iop.liveBus -eq $true) {
        $failures += "interop PASS incomplete"
    }
}

if ($null -ne $audit) {
    if (@($audit.entries).Count -lt 1) { $failures += "audit entries empty" }
    foreach ($e in $audit.entries) {
        foreach ($f in $auditFields) {
            if ($null -eq $e.$f -or ($e.$f -is [string] -and [string]::IsNullOrWhiteSpace($e.$f))) {
                $failures += "audit entry missing $f"
            }
        }
    }
}

if ($null -ne $ledger) {
    if (@($ledger.runs).Count -lt 1) { $failures += "api exchange ledger empty" }
    foreach ($r in $ledger.runs) {
        if ($r.liveBus -eq $true) { $failures += "ledger run must not be liveBus" }
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "EXT-005"
    ok = ($failures.Count -eq 0)
    journeyCount = $journeyIds.Count
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

Write-Host "PASS: EXT-005 api-exchange validate"
exit 0
