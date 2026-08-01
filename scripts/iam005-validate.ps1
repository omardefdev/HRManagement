# IAM-005: validate identity lifecycle deliverables.
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

Need "docs/IAM_005_IDENTITY_LIFECYCLE.md" | Out-Null
Need "scripts/iam005-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Iam005LifecycleTests.cs" | Out-Null

$jmlPath = Need "docs/iam/joiner-mover-leaver.json"
if (Test-Path $jmlPath) {
    $jml = Get-Content -Raw $jmlPath | ConvertFrom-Json
    if ($jml.card -ne "IAM-005") { $failures.Add("JML card must be IAM-005") }

    $types = @($jml.lifecycleEvents | ForEach-Object { $_.type })
    foreach ($need in @("Joiner", "Mover", "Leaver")) {
        if ($types -notcontains $need) { $failures.Add("lifecycle missing event type $need") }
    }

    if ($jml.feedOutage.failClosed -ne $true) { $failures.Add("feedOutage must be failClosed") }
    if ($jml.feedOutage.noNewGrants -ne $true) { $failures.Add("feedOutage must have noNewGrants") }
    if ($jml.orphanPolicy.dormantThresholdDays -gt 90) { $failures.Add("dormant threshold must be <= 90 days") }
}

if ($failures.Count -gt 0) {
    Write-Host "IAM-005 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "IAM-005 validation OK."
exit 0
