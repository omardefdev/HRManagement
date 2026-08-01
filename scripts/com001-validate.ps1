# COM-001: validate multi-channel notification artifacts.
# Usage:
#   powershell -File scripts/com001-validate.ps1

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
    "src/Shared/HRManagement.SharedKernel/Notifications/NotificationMessage.cs",
    "src/Shared/HRManagement.ServiceDefaults/Notifications/INotificationService.cs",
    "src/Shared/HRManagement.ServiceDefaults/Notifications/InAppNotificationService.cs",
    "docs/COM_001_NOTIFICATIONS.md",
    "scripts/com001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Com001NotificationTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$entityFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Notifications/NotificationMessage.cs"
if (Test-Path $entityFile) {
    $content = Get-Content -Raw -Path $entityFile
    if ($content -notmatch "NotificationChannel") { $failures += "NotificationMessage missing Channel enum" }
    if ($content -notmatch "NotificationStatus") { $failures += "NotificationMessage missing Status enum" }
    if ($content -notmatch "IdempotencyKey") { $failures += "NotificationMessage missing IdempotencyKey" }
}

$serviceFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Notifications/INotificationService.cs"
if (Test-Path $serviceFile) {
    $content = Get-Content -Raw -Path $serviceFile
    if ($content -notmatch "SendAsync") { $failures += "INotificationService missing SendAsync" }
    if ($content -notmatch "GetUnreadAsync") { $failures += "INotificationService missing GetUnreadAsync" }
    if ($content -notmatch "MarkReadAsync") { $failures += "INotificationService missing MarkReadAsync" }
}

$docFile = Resolve-RepoPath "docs/COM_001_NOTIFICATIONS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "COM-001") { $failures += "doc missing COM-001 reference" }
}

$outDir = Resolve-RepoPath "testdata/com001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "COM-001"
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

Write-Host "PASS: COM-001 notification validate"
exit 0
