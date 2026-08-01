# SYR-017: validate Syrian phased payroll launch plan artifacts.
# Usage:
#   powershell -File scripts/syr017-validate.ps1

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
    "docs/SYR_017_LAUNCH_CERT.md",
    "docs/syr/phased-launch-plan.json",
    "scripts/syr017-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr017LaunchTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/syr/phased-launch-plan.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "SYR-017") { $failures += "plan card must be SYR-017" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if ($check.payrollVolume.baselinePayrollCount -lt 1250000) {
        $failures += "baselinePayrollCount must be >= 1250000"
    }
    if ($check.payrollVolume.baselineWindowHours -lt 4) {
        $failures += "baselineWindowHours must be >= 4"
    }
    if ($check.payrollVolume.governorateCount -ne 14) {
        $failures += "governorateCount must be 14"
    }
    if ($check.shadowCycles.consecutiveRequired -lt 3) {
        $failures += "shadowCycles.consecutiveRequired must be >= 3"
    }
    if (-not $check.waves -or $check.waves.Count -lt 4) {
        $failures += "must define at least 4 launch waves"
    }
    foreach ($wave in $check.waves) {
        if ($wave.autoExpand -ne $false) {
            $failures += "wave $($wave.waveId) must have autoExpand false"
        }
        if ($wave.monitoringDays -lt 30) {
            $failures += "wave $($wave.waveId) monitoringDays must be >= 30"
        }
    }
    if (-not $check.waveControls.noAutoExpansion) {
        $failures += "noAutoExpansion must be true"
    }
    if (-not $check.nat008CrossRef.releaseDigestRequired) {
        $failures += "nat008CrossRef.releaseDigestRequired must be true"
    }
    if (-not $check.nat008CrossRef.certificationExpiryRequired) {
        $failures += "certificationExpiryRequired must be true"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_017_LAUNCH_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-017") { $failures += "doc missing SYR-017 reference" }
    if ($doc -notmatch "payroll|launch|wave") { $failures += "doc must describe phased payroll launch" }
    if ($doc -notmatch "1\.25M|1250000|shadow") { $failures += "doc must mention baseline volume or shadow cycles" }
}

$outDir = Resolve-RepoPath "testdata/syr017/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-017"
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

Write-Host "PASS: SYR-017 Syrian launch cert validate"
exit 0
