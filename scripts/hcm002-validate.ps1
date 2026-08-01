# HCM-002: validate time and attendance artifacts.
# Usage:
#   powershell -File scripts/hcm002-validate.ps1

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
    "docs/HCM_002_ATTENDANCE.md",
    "src/Shared/HRManagement.SharedKernel/Time/AttendanceRecord.cs",
    "src/Shared/HRManagement.SharedKernel/Time/ShiftDefinition.cs",
    "scripts/hcm002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm002AttendanceTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify AttendanceRecord entity has required properties
$attendanceFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Time/AttendanceRecord.cs"
if (Test-Path $attendanceFile) {
    $content = Get-Content -Raw -Path $attendanceFile
    $requiredProps = @("EmployeeId", "WorkDate", "ClockIn", "ClockOut",
                       "Status", "WorkedMinutes", "OvertimeMinutes",
                       "ShiftDefinitionId", "SourceDeviceId", "IsPayrollLocked")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "AttendanceRecord missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "AttendanceRecord must extend BaseEntity"
    }
}

# Verify ShiftDefinition entity has required properties
$shiftFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Time/ShiftDefinition.cs"
if (Test-Path $shiftFile) {
    $content = Get-Content -Raw -Path $shiftFile
    $requiredProps = @("ShiftCode", "ShiftName", "StartTime", "EndTime",
                       "BreakMinutes", "CrossesMidnight", "OvertimeThresholdMinutes",
                       "EffectiveFrom", "IsActive")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "ShiftDefinition missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "ShiftDefinition must extend BaseEntity"
    }
}

$docFile = Resolve-RepoPath "docs/HCM_002_ATTENDANCE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "HCM-002") { $failures += "doc missing HCM-002 reference" }
    if ($doc -notmatch "AttendanceRecord") { $failures += "doc must describe AttendanceRecord entity" }
    if ($doc -notmatch "ShiftDefinition") { $failures += "doc must describe ShiftDefinition entity" }
}

$outDir = Resolve-RepoPath "testdata/hcm002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-002"
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

Write-Host "PASS: HCM-002 time and attendance validate"
exit 0
