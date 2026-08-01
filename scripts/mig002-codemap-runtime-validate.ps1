# MIG-002: runtime/production code-map binding validate.
# Usage:
#   powershell -File scripts/mig002-codemap-runtime-validate.ps1

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

function Get-Json([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { throw "missing $rel ($full)" }
    return Get-Content -Raw -Path $full | ConvertFrom-Json
}

$failures = @()
$m = Get-Json $MatrixPath

if ($m.card -ne "MIG-002") { $failures += "card must be MIG-002" }

foreach ($need in @(
    @{ Id = "MIG-001"; Status = "Satisfied" },
    @{ Id = "TEN-001"; Status = "Satisfied" },
    @{ Id = "GOV-003"; Status = "Satisfied" },
    @{ Id = "agency-extract-access"; Status = "Blocked" },
    @{ Id = "SYR-003"; Status = "Blocked" },
    @{ Id = "MDM-001"; Status = "Blocked" }
)) {
    $b = @($m.blockers | Where-Object { $_.id -eq $need.Id } | Select-Object -First 1)
    if ($b.Count -eq 0) { $failures += "blocker $($need.Id) missing" }
    elseif ([string]$b[0].status -ne $need.Status) {
        $failures += "blocker $($need.Id) must be $($need.Status) (got $($b[0].status))"
    }
}

$allowed = @($m.allowedDispositions)
if ($allowed.Count -eq 0) { $allowed = @("Map", "Transform", "Quarantine") }
$catchAlls = @("Other", "OTHER", "misc", "unknown-dump")
if ($null -ne $m.unmappedPolicy -and @($m.unmappedPolicy.forbiddenCatchAllValues).Count -gt 0) {
    $catchAlls = @($m.unmappedPolicy.forbiddenCatchAllValues)
}

$mapCount = 0
foreach ($cm in @($m.codeMaps)) {
    $mapCount++
    if ([string]::IsNullOrWhiteSpace([string]$cm.disposition)) {
        $failures += "codeMap $($cm.mapId): missing disposition"
        continue
    }
    if ($allowed -notcontains [string]$cm.disposition) {
        $failures += "codeMap $($cm.mapId): bad disposition $($cm.disposition)"
    }
    if ([string]$cm.disposition -eq "Map" -and [string]::IsNullOrWhiteSpace([string]$cm.targetValue)) {
        $failures += "codeMap $($cm.mapId): Map without targetValue"
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$cm.targetValue) -and ($catchAlls -contains [string]$cm.targetValue)) {
        $failures += "codeMap $($cm.mapId): uses forbidden catch-all target $($cm.targetValue)"
    }
}
if ($mapCount -lt 10) { $failures += "need >= 10 codeMaps (got $mapCount)" }

$up = $m.unmappedPolicy
if ($null -eq $up) {
    $failures += "unmappedPolicy missing"
}
else {
    if ($up.forbidOther -ne $true) { $failures += "forbidOther must be true" }
    if ($null -eq $up.otherBucketMaxRate -or [double]$up.otherBucketMaxRate -lt 0) {
        $failures += "otherBucketMaxRate missing or negative"
    }
    foreach ($need in @("Other", "OTHER", "misc", "unknown-dump")) {
        if (@($up.forbiddenCatchAllValues) -notcontains $need) {
            $failures += "forbiddenCatchAllValues missing $need"
        }
    }
}

$lp = $m.legacyIdPolicy
if ($null -eq $lp) {
    $failures += "legacyIdPolicy missing"
}
else {
    if ($lp.storeAsReferenceOnly -ne $true) { $failures += "storeAsReferenceOnly must be true" }
    if ($lp.neverUseAsAuthOrTenantKey -ne $true) { $failures += "neverUseAsAuthOrTenantKey must be true" }
    if (@($lp.referenceFields).Count -lt 1) { $failures += "legacyIdPolicy referenceFields empty" }
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
    $version = $sig.mappingVersion
    if ([string]::IsNullOrWhiteSpace([string]$version)) { $version = $m.mappingVersion }
    if ([string]::IsNullOrWhiteSpace([string]$sig.contentHash) -or [string]::IsNullOrWhiteSpace([string]$version)) {
        $failures += "signing missing hash/version"
    }
}

$tz = $m.localeCalendar.timezone
if ($null -ne $tz -and [string]$tz -ne "Asia/Damascus") {
    $failures += "timezone must be Asia/Damascus"
}

# Negative fixtures must fail semantics
$massOther = Get-Json "testdata/mig002/scenarios/mass-other-fail.json"
$noQuarantine = Get-Json "testdata/mig002/scenarios/unmapped-no-quarantine-fail.json"
$legacyAuth = Get-Json "testdata/mig002/scenarios/legacy-id-as-auth-key-fail.json"

if ($massOther.expectedValidate -ne "FAIL") { $failures += "mass-other fixture expectedValidate must be FAIL" }
if ([double]$massOther.otherBucketRate -le [double]$massOther.otherBucketMaxRate) {
    $failures += "mass-other fixture must exceed otherBucketMaxRate"
}
if ([string]$massOther.catchAllTarget -ne "Other") {
    $failures += "mass-other fixture must use catchAllTarget Other"
}

if ($noQuarantine.expectedValidate -ne "FAIL") { $failures += "unmapped-no-quarantine fixture expectedValidate must be FAIL" }
if ($null -ne $noQuarantine.disposition -and -not [string]::IsNullOrWhiteSpace([string]$noQuarantine.disposition)) {
    $failures += "unmapped-no-quarantine fixture disposition must be null"
}

if ($legacyAuth.expectedValidate -ne "FAIL") { $failures += "legacy-id-as-auth fixture expectedValidate must be FAIL" }
if ([string]$legacyAuth.proposedAuthKey -ne "EmpNo") {
    $failures += "legacy-id-as-auth fixture must propose EmpNo as auth key"
}

# Positive fixtures
$coverage = Get-Json "testdata/mig002/coverage/mapping-coverage.json"
$roundtrip = Get-Json "testdata/mig002/roundtrip/status-org-roundtrip.json"
$signoff = Get-Json "testdata/mig002/signoff/legal-business-signoff.json"

if ($coverage.expectedValidate -ne "PASS") { $failures += "mapping-coverage expectedValidate must be PASS" }
if (@($coverage.domains).Count -lt 3) { $failures += "mapping-coverage needs >= 3 domains" }
foreach ($d in @($coverage.domains)) {
    if ($d.allDispositioned -ne $true) {
        $failures += "coverage domain $($d.sourceId)/$($d.sourceField) not allDispositioned"
    }
}

if ($roundtrip.expectedValidate -ne "PASS") { $failures += "roundtrip expectedValidate must be PASS" }
if (@($roundtrip.samples).Count -lt 2) { $failures += "roundtrip needs >= 2 samples" }

if ($signoff.expectedValidate -ne "PASS") { $failures += "legal-business-signoff expectedValidate must be PASS" }
if ([string]::IsNullOrWhiteSpace([string]$signoff.contentHash) -or [string]::IsNullOrWhiteSpace([string]$signoff.mappingVersion)) {
    $failures += "signoff missing hash/version"
}
if ([string]$signoff.signedByRole -eq [string]$signoff.reviewerRole) {
    $failures += "signoff owner equals reviewer"
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
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { $failures += "missing $rel"; continue }
    $cfg = Get-Content -Raw -Path $full | ConvertFrom-Json
    $mc = $cfg.MigCanonical
    if ($null -eq $mc) { $failures += "$rel missing MigCanonical"; continue }
    if ($mc.EnforceOnStartup -ne $true) { $failures += "$rel EnforceOnStartup must be true" }
    if ($mc.LiveCodeMapEngineClaim -eq $true) { $failures += "$rel LiveCodeMapEngineClaim must be false" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "MIG-002"
    mode = "runtime-production-binding"
    ok = ($failures.Count -eq 0)
    mapCount = $mapCount
    entityCount = @($m.canonicalEntities).Count
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

Write-Host "PASS: MIG-002 codemap runtime validate"
exit 0
