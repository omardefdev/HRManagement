# NAT-007: validate pilot/ops readiness certification checklist artifacts.
# Usage:
#   powershell -File scripts/nat007-validate.ps1

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
    "docs/NAT_007_PILOT_CERT.md",
    "docs/nat/nat007-checklist.json",
    "scripts/nat007-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat007PilotCertTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/nat007-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-007") { $failures += "checklist card must be NAT-007" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if (-not $check.pilotRequirements.rel002Complete) {
        $failures += "rel002Complete must be true"
    }
    if (-not $check.pilotRequirements.rel003Complete) {
        $failures += "rel003Complete must be true"
    }
    if ($check.pilotRequirements.consecutivePayrollCycles -lt 3) {
        $failures += "consecutivePayrollCycles must be >= 3"
    }
    if ($check.pilotRequirements.sloWindowDays -lt 30) {
        $failures += "sloWindowDays must be >= 30"
    }
    if ($check.pilotRequirements.severity1IncidentCount -ne 0) {
        $failures += "severity1IncidentCount must be 0"
    }
    if (-not $check.scorecardDomains -or $check.scorecardDomains.Count -lt 10) {
        $failures += "must define at least 10 scorecard domains"
    }
    $pilotRef = Resolve-RepoPath $check.relPilotRef
    if (-not (Test-Path $pilotRef)) {
        $failures += "missing relPilotRef: $($check.relPilotRef)"
    }
}

$docFile = Resolve-RepoPath "docs/NAT_007_PILOT_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-007") { $failures += "doc missing NAT-007 reference" }
    if ($doc -notmatch "pilot|REL-002") { $failures += "doc must describe pilot certification" }
    if ($doc -notmatch "SLO|scorecard") { $failures += "doc must mention SLO or scorecard" }
}

$outDir = Resolve-RepoPath "testdata/nat007/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-007"
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

Write-Host "PASS: NAT-007 pilot/ops readiness cert validate"
exit 0
