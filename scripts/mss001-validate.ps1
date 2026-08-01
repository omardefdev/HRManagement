# MSS-001: validate manager self-service artifacts.
# Usage:
#   powershell -File scripts/mss001-validate.ps1

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
    "docs/MSS_001_MANAGER_SELF_SERVICE.md",
    "docs/mss/manager-capabilities.json",
    "scripts/mss001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Mss001ManagerTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify capabilities JSON structure
$capFile = Resolve-RepoPath "docs/mss/manager-capabilities.json"
if (Test-Path $capFile) {
    $content = Get-Content -Raw -Path $capFile
    $requiredKeys = @("capabilities", "conflict_rules", "delegation_policy")
    foreach ($key in $requiredKeys) {
        if ($content -notmatch $key) {
            $failures += "manager-capabilities.json missing key: $key"
        }
    }
    $requiredCaps = @("team.view", "approval.leave", "delegation.grant", "dashboard.team")
    foreach ($cap in $requiredCaps) {
        if ($content -notmatch [regex]::Escape($cap)) {
            $failures += "manager-capabilities.json missing capability: $cap"
        }
    }
}

$docFile = Resolve-RepoPath "docs/MSS_001_MANAGER_SELF_SERVICE.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "MSS-001") { $failures += "doc missing MSS-001 reference" }
    if ($doc -notmatch "delegation") { $failures += "doc must describe delegation" }
    if ($doc -notmatch "conflict") { $failures += "doc must describe conflict of interest" }
}

$outDir = Resolve-RepoPath "testdata/mss001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "MSS-001"
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

Write-Host "PASS: MSS-001 manager self-service validate"
exit 0
