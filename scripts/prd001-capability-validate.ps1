# PRD-001: validate agency archetypes and capability map completeness.
# Usage:
#   powershell -File scripts/prd001-capability-validate.ps1

param(
    [string]$ArchetypesPath = "docs/prd-product/agency-archetypes.json",
    [string]$CapabilityMapPath = "docs/prd-product/capability-map.json",
    [string]$PersonasPath = "docs/prd-product/personas.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/prd001/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$requiredModules = @(
    "hr-core", "hr-contracts", "hr-health", "hr-movements", "hr-training",
    "payroll", "idcard", "records-archive", "admin-logistics", "it-infra",
    "dev-analytics", "compliance", "auth-directory", "file-storage"
)

$requiredArchetypes = @(
    "central-ministry", "governorate", "autonomous-institution",
    "branch-office", "specialized-body"
)

$allowedEntitlements = @("mandatory", "optional", "configurable", "disabled")

$paths = @{
    archetypes = Join-Path $RepoRoot $ArchetypesPath
    capability = Join-Path $RepoRoot $CapabilityMapPath
    personas = Join-Path $RepoRoot $PersonasPath
    overview = Join-Path $RepoRoot "docs/PRD_001_CAPABILITY_MAP.md"
    baseline = Join-Path $RepoRoot "docs/PRD_001_COMPETITIVE_BASELINE.md"
    uat = Join-Path $RepoRoot "docs/prd-product/uat-walkthrough-template.md"
}

$failures = @()
foreach ($key in $paths.Keys) {
    if (-not (Test-Path $paths[$key])) {
        $failures += "missing file: $($paths[$key])"
    }
}

if ($failures.Count -gt 0) {
    Write-Host "FAIL (prerequisite files):"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

$arch = Get-Content -Raw -Path $paths.archetypes -Encoding utf8 | ConvertFrom-Json
$cap = Get-Content -Raw -Path $paths.capability -Encoding utf8 | ConvertFrom-Json

if ($arch.card -ne "PRD-001") { $failures += "archetypes card must be PRD-001" }
if ($cap.card -ne "PRD-001") { $failures += "capability map card must be PRD-001" }

foreach ($id in $requiredModules) {
    if ($arch.requiredModuleIds -notcontains $id) {
        $failures += "archetypes requiredModuleIds missing $id"
    }
}

$serviceById = @{}
foreach ($s in @($cap.services)) {
    if ($serviceById.ContainsKey($s.moduleId)) {
        $failures += "duplicate service $($s.moduleId)"
    }
    $serviceById[$s.moduleId] = $s
    if ([string]::IsNullOrWhiteSpace($s.ownerRole)) {
        $failures += "$($s.moduleId) missing ownerRole"
    }
    if ([string]::IsNullOrWhiteSpace($s.capabilityDomain)) {
        $failures += "$($s.moduleId) missing capabilityDomain"
    }
    if ([string]::IsNullOrWhiteSpace($s.sloRef)) {
        $failures += "$($s.moduleId) missing sloRef"
    }
}

foreach ($id in $requiredModules) {
    if (-not $serviceById.ContainsKey($id)) {
        $failures += "capability map missing service $id"
    }
}

foreach ($a in @($arch.archetypes)) {
    if ($requiredArchetypes -notcontains $a.id) {
        $failures += "unexpected archetype $($a.id)"
    }
    $mandatoryCount = 0
    foreach ($id in $requiredModules) {
        $ent = [string]$a.moduleEntitlements.$id
        if ([string]::IsNullOrWhiteSpace($ent)) {
            $failures += "$($a.id) missing entitlement for $id"
            continue
        }
        if ($allowedEntitlements -notcontains $ent) {
            $failures += "$($a.id)/$id bad entitlement $ent"
        }
        if ($ent -eq "mandatory") { $mandatoryCount++ }

        $mapEnt = [string]$serviceById[$id].archetypeClassification.$($a.id)
        if ($mapEnt -and $mapEnt -ne $ent) {
            $failures += "$($a.id)/$id archetype ($ent) != capability map ($mapEnt)"
        }
    }
    if ($mandatoryCount -lt 1) {
        $failures += "$($a.id) must have at least one mandatory module"
    }
}

foreach ($need in $requiredArchetypes) {
    if (@($arch.archetypes | ForEach-Object { $_.id }) -notcontains $need) {
        $failures += "missing archetype $need"
    }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "PRD-001"
    ok = ($failures.Count -eq 0)
    archetypeCount = @($arch.archetypes).Count
    serviceCount = $serviceById.Count
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

Write-Host "PASS: PRD-001 capability validate"
exit 0
