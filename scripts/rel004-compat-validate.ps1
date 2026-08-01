# REL-004: validate release compatibility pack (inventories, policy docs, deprecation register, baselines).
# Usage:
#   powershell -File scripts/rel004-compat-validate.ps1

param(
    [string]$RepoRoot = "",
    [string]$ApiInventoryPath = "docs/rel-compat/api-client-inventory.json",
    [string]$EventInventoryPath = "docs/rel-compat/event-schema-inventory.json",
    [string]$DeprecationPath = "docs/rel-compat/deprecation-register.json",
    [string]$OutDir = "testdata/rel004/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()
$warnings = @()

function Require-File([string]$rel) {
    $full = Resolve-RepoPath $rel
    if (-not (Test-Path $full)) { $script:failures += "missing file $rel" }
    return $full
}

# Required policy docs
foreach ($doc in @(
    "docs/REL_004_COMPATIBILITY_POLICY.md",
    "docs/REL_004_RELEASE_NOTES_TEMPLATE.md",
    "docs/REL_004_ROLLBACK_POLICY.md",
    "docs/rel-compat/compatibility-matrix-template.md",
    "docs/rel-compat/openapi-baseline/README.md",
    "docs/rel-compat/openapi-baseline/hr-core-v1.openapi.json",
    "scripts/rel004-openapi-diff.ps1"
)) {
    Require-File $doc | Out-Null
}

# API client inventory
$apiFull = Require-File $ApiInventoryPath
if (Test-Path $apiFull) {
    $api = Get-Content -Raw -Path $apiFull | ConvertFrom-Json
    if ($api.card -ne "REL-004") { $failures += "api inventory card must be REL-004" }
    if (-not $api.semverPolicy.supportNMinus1) { $failures += "supportNMinus1 must be true" }
    if ([int]$api.semverPolicy.deprecationReleaseTrains -lt 2) { $failures += "deprecationReleaseTrains must be >= 2" }

    $types = @($api.consumers | ForEach-Object { $_.consumerType })
    foreach ($need in @("gateway", "inter-service", "external")) {
        if ($types -notcontains $need) { $failures += "api inventory missing consumerType $need" }
    }

    $requiredConsumerFields = @("id", "consumerType", "minSupportedApiVersion", "maxTestedApiVersion", "owner", "certificationStatus")
    foreach ($c in @($api.consumers)) {
        foreach ($f in $requiredConsumerFields) {
            if (-not ($c.PSObject.Properties.Name -contains $f) -or [string]::IsNullOrWhiteSpace($c.$f)) {
                $failures += "consumer $($c.id) missing $f"
            }
        }
    }
    if (@($api.consumers).Count -lt 10) { $failures += "api inventory too small (need gateway + inter + external coverage)" }
}

# Event schema inventory
$evtFull = Require-File $EventInventoryPath
if (Test-Path $evtFull) {
    $evt = Get-Content -Raw -Path $evtFull | ConvertFrom-Json
    if ($evt.card -ne "REL-004") { $failures += "event inventory card must be REL-004" }
    if (-not $evt.additiveOnlyWithinMinor) { $failures += "additiveOnlyWithinMinor must be true" }

    $requiredContractFields = @("id", "kind", "schemaVersion", "namespace", "compatRule")
    $contractIds = @()
    foreach ($c in @($evt.contracts)) {
        $contractIds += $c.id
        foreach ($f in $requiredContractFields) {
            if ($f -eq "compatRule" -and ($c.category -eq "dead-end")) { continue }
            if (-not ($c.PSObject.Properties.Name -contains $f)) {
                if ($f -eq "compatRule" -and ($c.PSObject.Properties.Name -contains "category")) { continue }
                $failures += "contract $($c.id) missing $f"
            }
        }
        if ([int]$c.schemaVersion -lt 1) { $failures += "contract $($c.id) invalid schemaVersion" }
    }

    foreach ($must in @("IEmployeeCreated", "IContractActivated", "IPayrollProcessed", "ILeaveApproved", "IExportJobRequested")) {
        if ($contractIds -notcontains $must) { $failures += "event inventory missing $must" }
    }
    if (@($evt.contracts).Count -lt 15) { $failures += "event inventory too small" }
}

# Deprecation register
$depFull = Require-File $DeprecationPath
if (Test-Path $depFull) {
    $dep = Get-Content -Raw -Path $depFull | ConvertFrom-Json
    if ($dep.card -ne "REL-004") { $failures += "deprecation register card must be REL-004" }
    if ([int]$dep.policy.deprecationReleaseTrains -lt 2) { $failures += "deprecation policy trains must be >= 2" }
    if ([int]$dep.policy.noticeDaysMin -lt 90) { $failures += "noticeDaysMin must be >= 90" }

    foreach ($e in @($dep.entries)) {
        foreach ($f in @("id", "artifactType", "status", "announcedRelease", "earliestRemovalRelease", "owner")) {
            if (-not ($e.PSObject.Properties.Name -contains $f) -or [string]::IsNullOrWhiteSpace($e.$f)) {
                $failures += "deprecation $($e.id) missing $f"
            }
        }
    }
}

# OpenAPI diff self-test: baseline vs itself must PASS
$diffScript = Resolve-RepoPath "scripts/rel004-openapi-diff.ps1"
$baseline = Resolve-RepoPath "docs/rel-compat/openapi-baseline/hr-core-v1.openapi.json"
if ((Test-Path $diffScript) -and (Test-Path $baseline)) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "powershell"
    $psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$diffScript`" -Baseline `"$baseline`" -Candidate `"$baseline`" -RepoRoot `"$RepoRoot`""
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.WorkingDirectory = $RepoRoot
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.WaitForExit()
    if ($p.ExitCode -ne 0) {
        $failures += "openapi-diff self-test failed exit $($p.ExitCode): $($p.StandardError.ReadToEnd())"
    }
}

# EXT-005 alignment spot-check
$ext005 = Resolve-RepoPath "docs/ext-api/api-exchange-matrix.json"
if (Test-Path $ext005) {
    $ext = Get-Content -Raw -Path $ext005 | ConvertFrom-Json
    if (-not $ext.compatibility.supportNMinus1) { $warnings += "EXT-005 supportNMinus1 false; REL-004 assumes true" }
    if ([int]$ext.deprecation.noticeDaysMin -lt 90) { $warnings += "EXT-005 noticeDaysMin below 90" }
}

# Write ledger
$outFull = Resolve-RepoPath $OutDir
if (-not (Test-Path $outFull)) { New-Item -ItemType Directory -Path $outFull -Force | Out-Null }
$ledger = [ordered]@{
    card      = "REL-004"
    validated = (Get-Date).ToUniversalTime().ToString("o")
    verdict   = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
    failures  = @($failures)
    warnings  = @($warnings)
}
$ledger | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $outFull "compat-validate-ledger.json") -Encoding UTF8

if ($warnings.Count -gt 0) {
    Write-Host "WARNINGS:"
    $warnings | ForEach-Object { Write-Host "  $_" }
}

if ($failures.Count -gt 0) {
    Write-Host ("FAIL: REL-004 compat validate (" + $failures.Count + " failures)")
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "PASS: REL-004 compat validate"
exit 0
