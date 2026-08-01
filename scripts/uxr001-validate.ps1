# UXR-001: validate field user research deliverables.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

function Need([string]$rel) {
    $p = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { $script:failures.Add("missing $rel") }
    return $p
}

Need "docs/UXR_001_RESEARCH_FRAMEWORK.md" | Out-Null
Need "docs/uxr/journey-blueprints.md" | Out-Null
Need "docs/uxr/usability-metrics-baseline.json" | Out-Null
Need "docs/uxr/research-ethics-protocol.md" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Uxr001ResearchTests.cs" | Out-Null

$metricsPath = Need "docs/uxr/usability-metrics-baseline.json"
if (Test-Path $metricsPath) {
    $metrics = Get-Content -Raw $metricsPath | ConvertFrom-Json
    if ($metrics.card -ne "UXR-001") { $failures.Add("metrics baseline card must be UXR-001") }

    $journeyIds = @($metrics.journeys | ForEach-Object { $_.id })
    foreach ($need in @("journey-hire", "journey-payroll", "journey-leave", "journey-transfer")) {
        if ($journeyIds -notcontains $need) { $failures.Add("metrics missing journey $need") }
    }

    foreach ($arch in @($metrics.archetypesRequired)) {
        if (-not $arch) { $failures.Add("empty archetype in metrics") }
    }
    if ($metrics.archetypesRequired.Count -lt 5) {
        $failures.Add("metrics must require at least 5 archetypes")
    }
}

$blueprintPath = Need "docs/uxr/journey-blueprints.md"
if (Test-Path $blueprintPath) {
    $bp = Get-Content -Raw $blueprintPath
    foreach ($flow in @("journey-hire", "journey-payroll", "journey-leave", "journey-transfer")) {
        if ($bp -notmatch $flow) { $failures.Add("journey-blueprints missing $flow") }
    }
}

$ethicsPath = Need "docs/uxr/research-ethics-protocol.md"
if (Test-Path $ethicsPath) {
    $ethics = Get-Content -Raw $ethicsPath
    if ($ethics -notmatch "consent") { $failures.Add("ethics protocol must cover consent") }
    if ($ethics -notmatch "PRV") { $failures.Add("ethics protocol must reference privacy (PRV)") }
}

if ($failures.Count -gt 0) {
    Write-Host "UXR-001 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "UXR-001 validation OK."
exit 0
