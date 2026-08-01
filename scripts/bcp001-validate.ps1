# BCP-001: validate business continuity documentation and triggers.
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

Need "docs/BCP_001_MANUAL_PROCEDURES.md" | Out-Null
Need "docs/BCP_001_REENTRY_RECONCILIATION.md" | Out-Null
Need "docs/BCP_001_DRILL_PLAYBOOK.md" | Out-Null
Need "docs/bcp/degraded-mode-matrix.json" | Out-Null
Need "docs/bcp/degraded-mode-triggers.json" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Bcp001ContinuityTests.cs" | Out-Null

$manual = Need "docs/BCP_001_MANUAL_PROCEDURES.md"
if (Test-Path $manual) {
    $text = Get-Content -Raw $manual
    if ($text -notmatch "Tier-0") { $failures.Add("manual procedures missing Tier-0") }
    if ($text -notmatch "Payroll") { $failures.Add("manual procedures missing payroll") }
    if ($text -notmatch "Identity") { $failures.Add("manual procedures missing identity") }
    if ($text -notmatch "Leave") { $failures.Add("manual procedures missing leave") }
}

$drill = Need "docs/BCP_001_DRILL_PLAYBOOK.md"
if (Test-Path $drill) {
    $text = Get-Content -Raw $drill
    if ($text -notmatch "8") { $failures.Add("drill playbook must define 8h scenario") }
}

$matrixPath = Need "docs/bcp/degraded-mode-matrix.json"
if (Test-Path $matrixPath) {
    try {
        $matrix = Get-Content -Raw $matrixPath | ConvertFrom-Json
        if ($matrix.cardId -ne "BCP-001") { $failures.Add("matrix cardId must be BCP-001") }
        if (-not $matrix.journeys -or $matrix.journeys.Count -lt 4) { $failures.Add("matrix needs at least 4 journeys") }
        $tier0 = @($matrix.journeys | Where-Object { $_.tier -eq 0 })
        if ($tier0.Count -lt 3) { $failures.Add("matrix needs at least 3 Tier-0 journeys") }
    } catch {
        $failures.Add("degraded-mode-matrix.json invalid JSON: $_")
    }
}

$triggersPath = Need "docs/bcp/degraded-mode-triggers.json"
if (Test-Path $triggersPath) {
    try {
        $json = Get-Content -Raw $triggersPath | ConvertFrom-Json
        if ($json.cardId -ne "BCP-001") { $failures.Add("triggers cardId must be BCP-001") }
        if (-not $json.triggers -or $json.triggers.Count -lt 3) { $failures.Add("triggers need at least 3 entries") }
    } catch {
        $failures.Add("degraded-mode-triggers.json invalid JSON: $_")
    }
}

$reentry = Need "docs/BCP_001_REENTRY_RECONCILIATION.md"
if (Test-Path $reentry) {
    $text = Get-Content -Raw $reentry
    if ($text -notmatch "duplicate") { $failures.Add("reentry doc must cover duplicate detection") }
}

if ($failures.Count -gt 0) {
    Write-Host "BCP-001 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "BCP-001 validation OK."
