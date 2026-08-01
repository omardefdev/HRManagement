# LEG-001: validate official records retention artifacts.
# Usage:
#   powershell -File scripts/leg001-validate.ps1

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
    "src/Shared/HRManagement.SharedKernel/Legal/OfficialRecord.cs",
    "src/Shared/HRManagement.SharedKernel/Legal/LegalHold.cs",
    "docs/LEG_001_RETENTION.md",
    "docs/leg/retention-schedule.json",
    "scripts/leg001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Leg001RetentionTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$scheduleFile = Resolve-RepoPath "docs/leg/retention-schedule.json"
if (Test-Path $scheduleFile) {
    $schedule = Get-Content -Raw -Path $scheduleFile | ConvertFrom-Json
    if ($schedule.card -ne "LEG-001") { $failures += "retention schedule card must be LEG-001" }
    if (-not $schedule.classes -or $schedule.classes.Count -lt 3) {
        $failures += "retention schedule must define at least 3 record classes"
    }
    foreach ($cls in $schedule.classes) {
        if (-not $cls.code) { $failures += "class missing code" }
        if (-not $cls.retentionYears -and $cls.retentionYears -ne 0) { $failures += "class $($cls.code) missing retentionYears" }
        if (-not $cls.legalBasis) { $failures += "class $($cls.code) missing legalBasis" }
    }
}

$docFile = Resolve-RepoPath "docs/LEG_001_RETENTION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "LEG-001") { $failures += "doc missing LEG-001 reference" }
    if ($doc -notmatch "legal hold") { $failures += "doc must mention legal hold" }
}

$outDir = Resolve-RepoPath "testdata/leg001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "LEG-001"
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

Write-Host "PASS: LEG-001 retention validate"
exit 0
