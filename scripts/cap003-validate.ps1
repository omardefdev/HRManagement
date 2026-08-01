# CAP-003: validate 3-year capacity plan artifacts.
# Usage:
#   powershell -File scripts/cap003-validate.ps1

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
    "docs/CAP_003_CAPACITY_PLAN.md",
    "docs/cap/capacity-forecast.json",
    "scripts/cap003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Cap003CapacityTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$forecastFile = Resolve-RepoPath "docs/cap/capacity-forecast.json"
if (Test-Path $forecastFile) {
    $forecast = Get-Content -Raw -Path $forecastFile | ConvertFrom-Json
    if ($forecast.card -ne "CAP-003") { $failures += "forecast card must be CAP-003" }

    if (-not $forecast.forecastHorizons -or $forecast.forecastHorizons.Count -lt 3) {
        $failures += "forecast must define at least 3 horizons (12/24/36 months)"
    }

    if (-not $forecast.resources -or $forecast.resources.Count -lt 5) {
        $failures += "forecast must define at least 5 resource types"
    }

    foreach ($resource in $forecast.resources) {
        if (-not $resource.triggerThresholdPercent -or $resource.triggerThresholdPercent -lt 50) {
            $failures += "resource $($resource.id) trigger threshold must be >= 50%"
        }
        if (-not $resource.triggerOwner) {
            $failures += "resource $($resource.id) must have a trigger owner"
        }
        if (-not $resource.fundingLink) {
            $failures += "resource $($resource.id) must link to funding (FIN-001)"
        }
        if (-not $resource.procurementLeadWeeks -or $resource.procurementLeadWeeks -lt 1) {
            $failures += "resource $($resource.id) must define procurement lead time"
        }
    }

    if (-not $forecast.drCapacity) {
        $failures += "forecast must define DR capacity requirements"
    }

    if (-not $forecast.reviewSchedule -or $forecast.reviewSchedule.frequency -ne "quarterly") {
        $failures += "forecast must have quarterly review schedule"
    }
}

$docFile = Resolve-RepoPath "docs/CAP_003_CAPACITY_PLAN.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "CAP-003") { $failures += "doc missing CAP-003 reference" }
    if ($doc -notmatch "36.month|3.year|three.year") { $failures += "doc must reference 3-year horizon" }
}

$outDir = Resolve-RepoPath "testdata/cap003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "CAP-003"
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

Write-Host "PASS: CAP-003 capacity plan validate"
exit 0
