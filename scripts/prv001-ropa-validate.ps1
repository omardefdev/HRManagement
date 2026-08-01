# PRV-001: validate ROPA register completeness.
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()
$path = Join-Path $RepoRoot "docs/prv-ropa/processing-activities.json"
if (-not (Test-Path $path)) { throw "missing ROPA json" }

$j = Get-Content -Raw $path | ConvertFrom-Json
if ($j.card -ne "PRV-001") { $failures.Add("card must be PRV-001") }

$servicesNeeded = @(
    "HR.Core", "Payroll", "HR.Health", "HR.Contracts", "HR.Movements", "HR.Training",
    "IDCard", "File.Storage", "Compliance", "Dev.Analytics", "IT.Infra",
    "Auth.Directory", "Admin.Logistics", "Records.Archive"
)
$present = @($j.activities | ForEach-Object { $_.service }) | Select-Object -Unique
foreach ($s in $servicesNeeded) {
    if ($present -notcontains $s) { $failures.Add("ROPA missing service $s") }
}

foreach ($a in @($j.activities)) {
    foreach ($need in @("id", "purpose", "legalBasis", "retentionPolicy")) {
        if (-not $a.$need) { $failures.Add("activity $($a.id) missing $need") }
    }
    if ($a.highRisk -eq $true -and -not $a.dpiaRef) {
        $failures.Add("high-risk activity $($a.id) missing dpiaRef")
    }
    if ($a.dpiaRef) {
        $dpia = Join-Path $RepoRoot ("docs/" + $a.dpiaRef)
        if (-not (Test-Path $dpia)) { $failures.Add("missing DPIA file $($a.dpiaRef)") }
    }
}

foreach ($doc in @(
    "docs/PRV_001_ROPA.md",
    "docs/PRV_001_DATA_SHARING.md",
    "docs/PRV_001_SUBPROCESSOR_REGISTER.md"
)) {
    if (-not (Test-Path (Join-Path $RepoRoot $doc))) { $failures.Add("missing $doc") }
}

if ($failures.Count -gt 0) {
    Write-Host "PRV-001 FAILED:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}
Write-Host "PRV-001 OK: ROPA covers $($present.Count) services / $($j.activities.Count) activities."
exit 0
