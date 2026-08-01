# SYR-016: validate Syrian cybersecurity crosswalk artifacts.
# Usage:
#   powershell -File scripts/syr016-validate.ps1

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

$requiredFiles = @(
    "docs/SYR_016_CYBERSECURITY.md",
    "docs/syr/cyber-control-crosswalk.json",
    "scripts/syr016-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr016CyberTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$crosswalkFile = Resolve-RepoPath "docs/syr/cyber-control-crosswalk.json"
if (Test-Path $crosswalkFile) {
    $json = Get-Content -Raw -Path $crosswalkFile | ConvertFrom-Json
    if ($json.card -ne "SYR-016") { $failures += "cyber-control-crosswalk.json card must be SYR-016" }
    if ($json.legalFramework.Count -lt 4) { $failures += "legalFramework must have at least 4 entries" }
    if ($json.controls.Count -lt 10) { $failures += "controls must have at least 10 entries" }
    if (-not $json.incidentResponse.unifiedClockWithPrivacy) { $failures += "incidentResponse must unify clock with privacy" }
    if (-not $json.reAccreditation.zeroCriticalGapRequired) { $failures += "reAccreditation must require zero Critical gap" }
    foreach ($ctrl in $json.controls) {
        if (-not $ctrl.articleRef) { $failures += "control $($ctrl.id) missing articleRef" }
        if (-not $ctrl.implementation) { $failures += "control $($ctrl.id) missing implementation" }
        if (-not $ctrl.evidence) { $failures += "control $($ctrl.id) missing evidence" }
        if (-not $ctrl.owner) { $failures += "control $($ctrl.id) missing owner" }
        if ($ctrl.gapSeverity -eq "critical") { $failures += "control $($ctrl.id) has Critical gap" }
    }
}

$docFile = Resolve-RepoPath "docs/SYR_016_CYBERSECURITY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-016") { $failures += "doc missing SYR-016 reference" }
    if ($doc -notmatch "20/2022|Law 20") { $failures += "doc must reference Law 20/2022" }
    if ($doc -notmatch "NAITS") { $failures += "doc must reference NAITS" }
    if ($doc -notmatch "SIEM|SOC") { $failures += "doc must address SIEM/SOC" }
    if ($doc -notmatch "playbook") { $failures += "doc must address playbooks" }
}

$outDir = Resolve-RepoPath "testdata/syr016/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-016"
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

Write-Host "PASS: SYR-016 cybersecurity validate"
exit 0
