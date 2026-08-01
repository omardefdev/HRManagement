# GOV-002 / DEC-001: validate legal decisions register closure tooling.
# Usage:
#   powershell -File scripts/gov002-validate.ps1

param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()

$registerFile = Resolve-RepoPath "docs/legal-decisions/register.json"
if (-not (Test-Path $registerFile)) {
    $failures += "missing docs/legal-decisions/register.json"
} else {
    $reg = Get-Content -Raw -Path $registerFile | ConvertFrom-Json
    if ($reg.card -ne "GOV-002") { $failures += "register.json card must be GOV-002" }
    if (-not $reg.closureTooling) { $failures += "register.json must define closureTooling" }
    if ($reg.closureTooling.validateScript -ne "scripts/gov002-validate.ps1") {
        $failures += "closureTooling.validateScript must be scripts/gov002-validate.ps1"
    }
    if ($reg.closureTooling.repoStatus -ne "Ready") {
        $failures += "closureTooling.repoStatus must be Ready"
    }
    $interim = @($reg.decisions | Where-Object { $_.approvalStatus -eq "ApprovedInterim" })
    $missingClosure = @($interim | Where-Object { -not $_.repoClosureStatus })
    if ($missingClosure.Count -gt 0) {
        $failures += "$($missingClosure.Count) ApprovedInterim decisions missing repoClosureStatus"
    }
}

$trackerFile = Resolve-RepoPath "docs/syd/SYD-001-018-TRACKER.md"
if (-not (Test-Path $trackerFile)) {
    $failures += "missing docs/syd/SYD-001-018-TRACKER.md"
} else {
    $tracker = Get-Content -Raw -Path $trackerFile
    if ($tracker -notmatch "closureTooling") { $failures += "SYD tracker must document closureTooling" }
    if ($tracker -notmatch "gov002-validate") { $failures += "SYD tracker must reference gov002-validate.ps1" }
}

$outDir = Resolve-RepoPath "testdata/gov002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "GOV-002"
    ok = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: GOV-002 legal register closure tooling validate"
exit 0
