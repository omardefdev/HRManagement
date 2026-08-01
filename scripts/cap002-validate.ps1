# CAP-002: validate load protection artifacts.
# Usage:
#   powershell -File scripts/cap002-validate.ps1

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
    "src/Shared/HRManagement.ServiceDefaults/Capacity/TenantAdmissionControl.cs",
    "docs/CAP_002_LOAD_PROTECTION.md",
    "docs/cap/tenant-quota-policy.json",
    "scripts/cap002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Cap002LoadProtectionTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$policyFile = Resolve-RepoPath "docs/cap/tenant-quota-policy.json"
if (Test-Path $policyFile) {
    $policy = Get-Content -Raw -Path $policyFile | ConvertFrom-Json
    if ($policy.card -ne "CAP-002") { $failures += "policy card must be CAP-002" }
    if (-not $policy.defaultQuota) { $failures += "policy missing defaultQuota" }
    if (-not $policy.tiers -or $policy.tiers.Count -lt 2) {
        $failures += "policy must define at least 2 quota tiers"
    }
}

$controlFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Capacity/TenantAdmissionControl.cs"
if (Test-Path $controlFile) {
    $content = Get-Content -Raw -Path $controlFile
    if ($content -notmatch "TryAdmit") { $failures += "TenantAdmissionControl missing TryAdmit" }
    if ($content -notmatch "Release") { $failures += "TenantAdmissionControl missing Release" }
    if ($content -notmatch "AdmissionDecision") { $failures += "TenantAdmissionControl missing AdmissionDecision" }
}

$docFile = Resolve-RepoPath "docs/CAP_002_LOAD_PROTECTION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "CAP-002") { $failures += "doc missing CAP-002 reference" }
    if ($doc -notmatch "200%") { $failures += "doc must mention 200% load" }
}

$outDir = Resolve-RepoPath "testdata/cap002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "CAP-002"
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

Write-Host "PASS: CAP-002 load protection validate"
exit 0
