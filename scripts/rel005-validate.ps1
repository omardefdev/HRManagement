# REL-005: validate progressive delivery docs and canary gates.

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

Need "docs/REL_005_PROGRESSIVE_DELIVERY.md" | Out-Null
Need "docs/rel/canary-gates.json" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Rel005ProgressiveDeliveryTests.cs" | Out-Null

$gatesPath = Need "docs/rel/canary-gates.json"
if (Test-Path $gatesPath) {
    try {
        $gates = Get-Content -Raw $gatesPath | ConvertFrom-Json
        if ($gates.card -ne "REL-005") { $failures.Add("canary gates card must be REL-005") }
        if (-not $gates.stages -or $gates.stages.Count -lt 4) { $failures.Add("need at least 4 canary stages") }
        if ($gates.rollbackSlaMinutes -gt 15) { $failures.Add("rollback SLA must be <= 15 min") }

        $hasPayroll = $false
        foreach ($stage in $gates.stages) {
            if ($stage.gates) {
                foreach ($gate in $stage.gates) {
                    if ($gate.type -eq "business") { $hasPayroll = $true }
                }
            }
        }
        if (-not $hasPayroll) { $failures.Add("need at least one business gate") }
    } catch {
        $failures.Add("canary-gates.json invalid JSON: $_")
    }
}

$relDoc = Need "docs/REL_005_PROGRESSIVE_DELIVERY.md"
if (Test-Path $relDoc) {
    $text = Get-Content -Raw $relDoc
    if ($text -notmatch "canary") { $failures.Add("doc must reference canary deployment") }
    if ($text -notmatch "rollback") { $failures.Add("doc must reference rollback") }
    if ($text -notmatch "15.min") { $failures.Add("doc must specify 15 min rollback SLA") }
    if ($text -notmatch "feature.flag") { $failures.Add("doc must reference feature flags") }
}

if ($failures.Count -gt 0) {
    Write-Host "REL-005 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PASS: REL-005 validation OK."
exit 0
