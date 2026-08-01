# REL-006: validate release train deliverables.
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

Need "docs/REL_006_RELEASE_TRAIN.md" | Out-Null
Need "scripts/rel006-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Rel006ReleaseTrainTests.cs" | Out-Null

$freezePath = Need "docs/rel/freeze-windows.json"
if (Test-Path $freezePath) {
    $fw = Get-Content -Raw $freezePath | ConvertFrom-Json
    if ($fw.card -ne "REL-006") { $failures.Add("freeze-windows card must be REL-006") }

    $windowIds = @($fw.freezeWindows | ForEach-Object { $_.id })
    foreach ($need in @("payroll-close", "fiscal-year-end", "national-holidays", "pilot-stabilization")) {
        if ($windowIds -notcontains $need) { $failures.Add("freeze-windows missing window $need") }
    }

    if ($fw.emergencyHotfixPolicy.requiredApprovals -lt 2) {
        $failures.Add("emergency hotfix must require >= 2 approvals")
    }
    if ($fw.emergencyHotfixPolicy.pirDeadlineHours -gt 24) {
        $failures.Add("PIR deadline must be <= 24 hours")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "REL-006 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "REL-006 validation OK."
exit 0
