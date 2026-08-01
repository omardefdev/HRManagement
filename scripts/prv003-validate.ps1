# PRV-003: validate breach response deliverables.
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

Need "docs/PRV_003_BREACH_RESPONSE.md" | Out-Null
Need "scripts/prv003-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Prv003BreachTests.cs" | Out-Null

$wfPath = Need "docs/prv/breach-response-workflow.json"
if (Test-Path $wfPath) {
    $wf = Get-Content -Raw $wfPath | ConvertFrom-Json
    if ($wf.card -ne "PRV-003") { $failures.Add("workflow card must be PRV-003") }

    $phaseIds = @($wf.phases | ForEach-Object { $_.id })
    foreach ($need in @("detection-triage", "scoping", "risk-assessment", "regulator-notification", "individual-notification", "remediation", "post-incident-review")) {
        if ($phaseIds -notcontains $need) { $failures.Add("workflow missing phase $need") }
    }

    $regPhase = $wf.phases | Where-Object { $_.id -eq "regulator-notification" }
    if ($regPhase.targetHours -gt 72) { $failures.Add("regulator notification must be <= 72 hours") }
    if ($regPhase.statutory -ne $true) { $failures.Add("regulator notification must be statutory") }

    if ($wf.processorObligation.notifyControllerMaxHours -gt 24) {
        $failures.Add("processor must notify controller within 24 hours")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "PRV-003 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PRV-003 validation OK."
exit 0
