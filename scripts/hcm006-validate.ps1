# HCM-006: validate employee relations artifacts.
# Usage:
#   powershell -File scripts/hcm006-validate.ps1

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
    "docs/HCM_006_EMPLOYEE_RELATIONS.md",
    "src/Shared/HRManagement.SharedKernel/Cases/EmployeeRelationCase.cs",
    "scripts/hcm006-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Hcm006EmployeeRelationsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify EmployeeRelationCase entity
$caseFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Cases/EmployeeRelationCase.cs"
if (Test-Path $caseFile) {
    $content = Get-Content -Raw -Path $caseFile
    $requiredProps = @("CaseNumber", "CaseType", "Status", "EmployeeId", "IsWhistleblower", "IsConfidential", "Deadline", "LegalHoldReference")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "EmployeeRelationCase missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "EmployeeRelationCase must extend BaseEntity"
    }
    # Must support Grievance, Discipline, Investigation, Whistleblower case types
    foreach ($ctype in @("Grievance", "Discipline", "Investigation", "Whistleblower")) {
        if ($content -notmatch $ctype) {
            $failures += "EmployeeRelationCase missing case type: $ctype"
        }
    }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/HCM_006_EMPLOYEE_RELATIONS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "HCM-006") { $failures += "doc missing HCM-006 reference" }
    if ($doc -notmatch "EmployeeRelationCase") { $failures += "doc must describe EmployeeRelationCase" }
    if ($doc -notmatch "Whistleblower") { $failures += "doc must address whistleblower protection" }
}

$outDir = Resolve-RepoPath "testdata/hcm006/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "HCM-006"
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

Write-Host "PASS: HCM-006 employee relations validate"
exit 0
