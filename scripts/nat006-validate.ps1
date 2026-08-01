# NAT-006: validate performance/resilience certification checklist artifacts.
# Usage:
#   powershell -File scripts/nat006-validate.ps1

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
    "docs/NAT_006_PERFORMANCE_CERT.md",
    "docs/nat/nat006-checklist.json",
    "docs/nat/nat006-witnessed-run-template.json",
    "docs/nat/nat006-witnessed-run-template.md",
    "scripts/nat006-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat006PerformanceCertTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/nat006-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-006") { $failures += "checklist card must be NAT-006" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if ($check.workloadEnvelope.soakHours -lt 72) {
        $failures += "soakHours must be >= 72"
    }
    if ($check.workloadEnvelope.stressLoadPercent -lt 150) {
        $failures += "stressLoadPercent must be >= 150"
    }
    if ($check.workloadEnvelope.spikeLoadPercent -lt 200) {
        $failures += "spikeLoadPercent must be >= 200"
    }
    if ($check.workloadEnvelope.headroomPercent -lt 30) {
        $failures += "headroomPercent must be >= 30"
    }
    $capRef = Resolve-RepoPath $check.cap001Ref
    if (-not (Test-Path $capRef)) {
        $failures += "missing cap001Ref: $($check.cap001Ref)"
    }
    if (-not $check.resilience.zeroLossCorruptionRequired) {
        $failures += "zeroLossCorruptionRequired must be true"
    }
}

$witnessTemplate = Resolve-RepoPath "docs/nat/nat006-witnessed-run-template.json"
if (Test-Path $witnessTemplate) {
    $tpl = Get-Content -Raw -Path $witnessTemplate | ConvertFrom-Json
    if ($tpl.card -ne "NAT-006") { $failures += "witnessed-run template card must be NAT-006" }
    if (-not $tpl.templateOnly) { $failures += "witnessed-run template must declare templateOnly" }
    if ($tpl.liveCertificationStatus -ne "Blocked") {
        $failures += "witnessed-run template liveCertificationStatus must remain Blocked until live run"
    }
    if ($tpl.loadPhases.Count -lt 3) { $failures += "witnessed-run template must define soak/stress/spike phases" }
}

$docFile = Resolve-RepoPath "docs/NAT_006_PERFORMANCE_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-006") { $failures += "doc missing NAT-006 reference" }
    if ($doc -notmatch "performance|CAP-001|headroom") { $failures += "doc must describe performance certification" }
    if ($doc -notmatch "soak|stress|spike") { $failures += "doc must mention soak/stress/spike" }
}

$outDir = Resolve-RepoPath "testdata/nat006/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-006"
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

Write-Host "PASS: NAT-006 performance/resilience cert validate"
exit 0
