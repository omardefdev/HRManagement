# SRE-001: validate on-call artifacts.
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()
foreach ($rel in @(
    "docs/ON_CALL_ROSTER.md",
    "docs/SRE_001_ESCALATION_POLICY.md",
    "docs/SRE_001_ACCESS_MATRIX.md",
    "docs/SRE_001_DRILL_PLAYBOOK.md",
    "docs/SRE_001_VERIFICATION.md"
)) {
    if (-not (Test-Path (Join-Path $RepoRoot $rel))) { $failures.Add("missing $rel") }
}

$roster = Get-Content -Raw (Join-Path $RepoRoot "docs/ON_CALL_ROSTER.md")
foreach ($need in @("Tier-0", "Tier-1", "Tier-2", "Handover", "backup")) {
    if ($roster -notmatch [regex]::Escape($need)) { $failures.Add("roster missing $need") }
}

$esc = Get-Content -Raw (Join-Path $RepoRoot "docs/SRE_001_ESCALATION_POLICY.md")
if ($esc -notmatch "5 minutes|≤ 5") { $failures.Add("escalation missing P1 ack ≤5m") }
if ($esc -notmatch "10 minutes|≤ 10") { $failures.Add("escalation missing IC ≤10m") }

if ($failures.Count -gt 0) {
    Write-Host "SRE-001 FAILED:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}
Write-Host "SRE-001 OK: on-call artifacts present."
exit 0
