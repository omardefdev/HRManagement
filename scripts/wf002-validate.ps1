# WF-002: validate unified work inbox artifacts.
# Usage:
#   powershell -File scripts/wf002-validate.ps1

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
    "docs/WF_002_WORK_INBOX.md",
    "src/Shared/HRManagement.ServiceDefaults/Workflow/WorkInboxItem.cs",
    "src/Shared/HRManagement.ServiceDefaults/Workflow/IWorkInboxService.cs",
    "scripts/wf002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Wf002WorkInboxTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify WorkInboxItem entity
$itemFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Workflow/WorkInboxItem.cs"
if (Test-Path $itemFile) {
    $content = Get-Content -Raw -Path $itemFile
    $requiredProps = @("AssigneeId", "SourceSystem", "SourceReferenceId", "ItemType", "Priority", "Status", "DueDate", "IsEscalated", "DelegatedFromId")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "WorkInboxItem missing required property: $prop"
        }
    }
}

# Verify IWorkInboxService interface
$svcFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Workflow/IWorkInboxService.cs"
if (Test-Path $svcFile) {
    $content = Get-Content -Raw -Path $svcFile
    $requiredMethods = @("GetPersonalInboxAsync", "GetTeamInboxAsync", "CreateItemAsync", "CompleteItemAsync", "DelegateItemAsync", "EscalateItemAsync")
    foreach ($method in $requiredMethods) {
        if ($content -notmatch $method) {
            $failures += "IWorkInboxService missing required method: $method"
        }
    }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/WF_002_WORK_INBOX.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "WF-002") { $failures += "doc missing WF-002 reference" }
    if ($doc -notmatch "WorkInboxItem") { $failures += "doc must describe WorkInboxItem" }
    if ($doc -notmatch "IWorkInboxService") { $failures += "doc must describe IWorkInboxService" }
}

$outDir = Resolve-RepoPath "testdata/wf002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "WF-002"
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

Write-Host "PASS: WF-002 work inbox validate"
exit 0
