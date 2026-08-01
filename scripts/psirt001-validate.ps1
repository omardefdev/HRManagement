# PSIRT-001: validate vulnerability disclosure deliverables.
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

Need "docs/PSIRT_001_VULNERABILITY.md" | Out-Null
Need "docs/psirt/disclosure-channel.md" | Out-Null
Need "scripts/psirt001-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Psirt001VulnerabilityTests.cs" | Out-Null

$slaPath = Need "docs/psirt/severity-sla.json"
if (Test-Path $slaPath) {
    $sla = Get-Content -Raw $slaPath | ConvertFrom-Json
    if ($sla.card -ne "PSIRT-001") { $failures.Add("severity-sla card must be PSIRT-001") }

    $severities = @($sla.severityLevels | ForEach-Object { $_.severity })
    foreach ($need in @("Critical-Exploited", "Critical", "High", "Medium", "Low")) {
        if ($severities -notcontains $need) { $failures.Add("severity-sla missing level $need") }
    }

    $critExploited = $sla.severityLevels | Where-Object { $_.severity -eq "Critical-Exploited" }
    if ($critExploited.triageMaxHours -gt 4) { $failures.Add("Critical-Exploited triage must be <= 4 hours") }
    if ($critExploited.fixDeployedMaxHours -gt 24) { $failures.Add("Critical-Exploited fix must be <= 24 hours") }

    if ($sla.fixPolicy.requiredReviewers -lt 2) { $failures.Add("fix must require >= 2 reviewers") }
    if ($sla.fixPolicy.regressionTestRequired -ne $true) { $failures.Add("regression test required") }
    if ($sla.fixPolicy.rootCauseActionRequired -ne $true) { $failures.Add("root cause action required") }
}

if ($failures.Count -gt 0) {
    Write-Host "PSIRT-001 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PSIRT-001 validation OK."
exit 0
