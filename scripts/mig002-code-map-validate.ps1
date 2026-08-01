# MIG-002: validate canonical code-map matrix, coverage, round-trip, sign-off.
# Usage:
#   powershell -File scripts/mig002-code-map-validate.ps1

param(
    [string]$MatrixPath = "docs/mig-canonical/code-map-matrix.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/mig002/out"
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

if ($m.card -ne "MIG-002") { $failures += "card must be MIG-002" }

$journeyIds = @($m.journeys | ForEach-Object { $_.id })
foreach ($need in @("CanonicalModel", "CodeMapCoverage", "NoMassOther", "LegacyIdReference", "RoundTrip", "LegalBusinessSignOff")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$entityIds = @($m.canonicalEntities | ForEach-Object { $_.id })
foreach ($need in @("employee", "org", "job", "payroll", "document")) {
    if ($entityIds -notcontains $need) { $failures += "missing canonical entity $need" }
}

# Code maps
$mapFields = @($m.requiredCodeMapFields)
$mapIndex = @{}
$mapCount = 0
foreach ($cm in $m.codeMaps) {
    $mapCount++
    foreach ($f in $mapFields) {
        if ($null -eq $cm.$f -and $f -ne "targetValue") {
            $failures += "codeMap $($cm.mapId) missing $f"
        }
        elseif ($f -ne "targetValue" -and $cm.$f -is [string] -and [string]::IsNullOrWhiteSpace($cm.$f) -and $f -ne "sourceValue") {
            # sourceValue may be empty string for blank job title
            if ($f -ne "sourceValue") { $failures += "codeMap $($cm.mapId) empty $f" }
        }
    }
    if ($m.allowedDispositions -notcontains $cm.disposition) {
        $failures += "codeMap $($cm.mapId) bad disposition $($cm.disposition)"
    }
    if ($cm.disposition -eq "Map" -and ($null -eq $cm.targetValue -or ($cm.targetValue -is [string] -and [string]::IsNullOrWhiteSpace($cm.targetValue)))) {
        $failures += "codeMap $($cm.mapId) Map without targetValue"
    }
    if ($cm.disposition -eq "Quarantine" -and $null -ne $cm.targetValue -and "$($cm.targetValue)" -ne "") {
        # quarantine may have null target — ok if null
    }
    $key = "$($cm.sourceId)|$($cm.sourceField)|$($cm.sourceValue)"
    $mapIndex[$key] = $cm
    $catchAll = @($m.unmappedPolicy.forbiddenCatchAllValues)
    if ($catchAll -contains $cm.targetValue) {
        $failures += "codeMap $($cm.mapId) uses forbidden catch-all target $($cm.targetValue)"
    }
}
if ($mapCount -lt 10) { $failures += "need >= 10 codeMaps" }

# Coverage domains
foreach ($dom in $m.requiredCoverageDomains) {
    $found = $false
    foreach ($cm in $m.codeMaps) {
        if ($cm.sourceId -eq $dom.sourceId -and $cm.sourceField -eq $dom.sourceField) {
            $found = $true
            break
        }
    }
    if (-not $found) {
        $failures += "coverage domain $($dom.sourceId).$($dom.sourceField) has no maps"
    }
}

# Policies
if (-not $m.unmappedPolicy.forbidOther) { $failures += "forbidOther must be true" }
if (-not $m.legacyIdPolicy.storeAsReferenceOnly) { $failures += "storeAsReferenceOnly must be true" }
if (-not $m.legacyIdPolicy.neverUseAsAuthOrTenantKey) { $failures += "neverUseAsAuthOrTenantKey must be true" }
if ($m.localeCalendar.timezone -ne "Asia/Damascus") { $failures += "timezone must be Asia/Damascus" }

# Signing
$sig = $m.signing
if ([string]::IsNullOrWhiteSpace($sig.signedByRole) -or [string]::IsNullOrWhiteSpace($sig.reviewerRole)) {
    $failures += "signing missing roles"
}
if ($sig.signedByRole -eq $sig.reviewerRole) { $failures += "signing owner equals reviewer" }
if ([string]::IsNullOrWhiteSpace($sig.contentHash) -or [string]::IsNullOrWhiteSpace($sig.mappingVersion)) {
    $failures += "signing missing hash/version"
}

# Blockers
$blockerMap = @{}
foreach ($b in $m.blockers) {
    $blockerMap[$b.id] = $b.status
    if ($m.allowedBlockerStatuses -notcontains $b.status) {
        $failures += "blocker $($b.id) bad status $($b.status)"
    }
}
if ($blockerMap["MIG-001"] -ne "Satisfied") { $failures += "MIG-001 must be Satisfied" }
if ($blockerMap["TEN-001"] -ne "Satisfied") { $failures += "TEN-001 must be Satisfied" }
if ($blockerMap["GOV-003"] -ne "Satisfied") { $failures += "GOV-003 must be Satisfied" }
if ($blockerMap["agency-extract-access"] -ne "Blocked") { $failures += "agency-extract-access must be Blocked" }
if ($blockerMap["SYR-003"] -ne "Blocked") { $failures += "SYR-003 must be Blocked" }
if ($blockerMap["MDM-001"] -ne "Blocked") { $failures += "MDM-001 must be Blocked" }

function Assert-FixtureExpected([string]$rel, [string]$expect) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) {
        $script:failures += "missing fixture $rel"
        return $null
    }
    $obj = Get-Content -Raw -Path $full | ConvertFrom-Json
    if ($obj.expectedValidate -ne $expect) {
        $script:failures += "fixture $rel expectedValidate must be $expect"
    }
    return $obj
}

$cov = Assert-FixtureExpected $m.simulationPaths.mappingCoverage "PASS"
$rt = Assert-FixtureExpected $m.simulationPaths.roundTrip "PASS"
$mass = Assert-FixtureExpected $m.simulationPaths.massOtherFail "FAIL"
$unmap = Assert-FixtureExpected $m.simulationPaths.unmappedNoQuarantineFail "FAIL"
$auth = Assert-FixtureExpected $m.simulationPaths.legacyIdAsAuthFail "FAIL"
$sign = Assert-FixtureExpected $m.simulationPaths.legalBusinessSignoff "PASS"

if ($null -ne $cov) {
    foreach ($d in $cov.domains) {
        if (-not $d.allDispositioned) { $failures += "coverage domain not allDispositioned" }
        foreach ($v in @($d.distinctValues)) {
            $key = "$($d.sourceId)|$($d.sourceField)|$v"
            if (-not $mapIndex.ContainsKey($key)) {
                $failures += "coverage value missing map: $key"
            }
        }
    }
}
if ($null -ne $rt) {
    foreach ($s in $rt.samples) {
        $key = "$($s.sourceId)|$($s.sourceField)|$($s.sourceValue)"
        if (-not $mapIndex.ContainsKey($key)) {
            $failures += "roundtrip sample missing map $key"
            continue
        }
        $cm = $mapIndex[$key]
        if ($cm.disposition -ne "Map" -and $cm.disposition -ne "Transform") {
            $failures += "roundtrip sample $key not Map/Transform"
        }
        if ("$($cm.targetValue)" -ne "$($s.forwardTarget)" -and $cm.sourceValue -ne "*") {
            $failures += "roundtrip forward mismatch for $key"
        }
        if ($s.roundTripSource -ne $s.sourceValue) {
            $failures += "roundtrip reverse mismatch for $key"
        }
    }
}
if ($null -ne $mass) {
    if ([double]$mass.otherBucketRate -le [double]$mass.otherBucketMaxRate) {
        $failures += "mass-other fixture rate must exceed max"
    }
}
if ($null -ne $unmap) {
    if ($null -ne $unmap.disposition -and "$($unmap.disposition)" -ne "") {
        $failures += "unmapped-no-quarantine fixture must have null disposition"
    }
}
if ($null -ne $auth) {
    $refs = @($m.legacyIdPolicy.referenceFields)
    if ($refs -notcontains $auth.proposedAuthKey) {
        $failures += "legacy-id-auth fixture must propose a reference field"
    }
}
if ($null -ne $sign) {
    if ($sign.mappingVersion -ne $m.signing.mappingVersion) {
        $failures += "signoff mappingVersion mismatch"
    }
    if ($sign.contentHash -ne $m.signing.contentHash) {
        $failures += "signoff contentHash mismatch"
    }
    if ($sign.signedByRole -ne $m.signing.signedByRole -or $sign.reviewerRole -ne $m.signing.reviewerRole) {
        $failures += "signoff roles mismatch"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-002"
    ok = ($failures.Count -eq 0)
    codeMapCount = $mapCount
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

Write-Host "PASS: MIG-002 code map validate"
exit 0
