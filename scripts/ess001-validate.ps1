# ESS-001: validate employee service center artifacts.
# Usage:
#   powershell -File scripts/ess001-validate.ps1

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
    "docs/ESS_001_SERVICE_CENTER.md",
    "src/Shared/HRManagement.SharedKernel/Ess/EmployeeServiceCase.cs",
    "scripts/ess001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Ess001ServiceCenterTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify EmployeeServiceCase entity
$caseFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Ess/EmployeeServiceCase.cs"
if (Test-Path $caseFile) {
    $content = Get-Content -Raw -Path $caseFile
    $requiredProps = @("CaseId", "EmployeeId", "CaseType", "Status", "SlaDeadline", "SlaBreached", "WorkflowInstanceId")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "EmployeeServiceCase missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "EmployeeServiceCase must extend BaseEntity"
    }
    if ($content -notmatch "ServiceCaseStatus") {
        $failures += "EmployeeServiceCase must use ServiceCaseStatus enum"
    }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/ESS_001_SERVICE_CENTER.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "ESS-001") { $failures += "doc missing ESS-001 reference" }
    if ($doc -notmatch "EmployeeServiceCase") { $failures += "doc must describe EmployeeServiceCase" }
    if ($doc -notmatch "SLA") { $failures += "doc must address SLA enforcement" }
    if ($doc -notmatch "CaseId") { $failures += "doc must describe durable Case ID" }
}

$outDir = Resolve-RepoPath "testdata/ess001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "ESS-001"
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

Write-Host "PASS: ESS-001 employee service center validate"
exit 0
