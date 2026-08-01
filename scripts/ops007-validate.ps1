# OPS-007: validate component lifecycle docs, inventory, patch SLA policy.

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

Need "docs/OPS_007_COMPONENT_LIFECYCLE.md" | Out-Null
Need "docs/ops007/component-inventory.json" | Out-Null
Need "docs/ops007/patch-sla-policy.json" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Ops007LifecycleTests.cs" | Out-Null

$invPath = Need "docs/ops007/component-inventory.json"
if (Test-Path $invPath) {
    try {
        $inv = Get-Content -Raw $invPath | ConvertFrom-Json
        if ($inv.card -ne "OPS-007") { $failures.Add("inventory card must be OPS-007") }
        if (-not $inv.components -or $inv.components.Count -lt 5) { $failures.Add("need at least 5 components in inventory") }

        $tier0 = $inv.components | Where-Object { $_.tier -eq 0 }
        if ($tier0.Count -lt 3) { $failures.Add("need at least 3 Tier-0 components") }
    } catch {
        $failures.Add("component-inventory.json invalid JSON: $_")
    }
}

$policyPath = Need "docs/ops007/patch-sla-policy.json"
if (Test-Path $policyPath) {
    try {
        $policy = Get-Content -Raw $policyPath | ConvertFrom-Json
        if ($policy.card -ne "OPS-007") { $failures.Add("patch policy card must be OPS-007") }
        if (-not $policy.patchSLAs -or $policy.patchSLAs.Count -lt 4) { $failures.Add("need at least 4 patch SLA levels") }
        if (-not $policy.certAlertWindows -or $policy.certAlertWindows.Count -lt 4) { $failures.Add("need 4 cert alert windows") }

        $exploited = $policy.patchSLAs | Where-Object { $_.id -eq "exploited-critical" }
        if (-not $exploited) { $failures.Add("exploited-critical SLA required") }
        elseif ($exploited.slaHours -gt 24) { $failures.Add("exploited-critical SLA must be <= 24h") }
    } catch {
        $failures.Add("patch-sla-policy.json invalid JSON: $_")
    }
}

$lcDoc = Need "docs/OPS_007_COMPONENT_LIFECYCLE.md"
if (Test-Path $lcDoc) {
    $text = Get-Content -Raw $lcDoc
    if ($text -notmatch "EOL") { $failures.Add("doc must reference EOL policy") }
    if ($text -notmatch "24.hour") { $failures.Add("doc must reference 24h exploited-critical SLA") }
    if ($text -notmatch "60.*30.*15.*7") { $failures.Add("doc must define cert alert windows 60/30/15/7") }
}

if ($failures.Count -gt 0) {
    Write-Host "OPS-007 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PASS: OPS-007 validation OK."
exit 0
