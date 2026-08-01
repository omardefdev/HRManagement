# NAT-002: validate security/privacy certification checklist artifacts.
# Usage:
#   powershell -File scripts/nat002-validate.ps1

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
    "docs/NAT_002_SECURITY_CERT.md",
    "docs/nat/nat002-checklist.json",
    "scripts/nat002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat002SecurityCertTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/nat002-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-002") { $failures += "checklist card must be NAT-002" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if ($check.liveCertificationStatus -ne "Blocked") { $failures += "liveCertificationStatus must be Blocked" }
    if (-not $check.sovReferences -or $check.sovReferences.Count -lt 7) {
        $failures += "must reference SOV-001..007"
    }
    if (-not $check.evidenceDomains -or $check.evidenceDomains.Count -lt 6) {
        $failures += "must define at least 6 evidence domains"
    }
    $hasResidual = $false
    foreach ($d in $check.evidenceDomains) {
        if ($d.id -eq "residual-risk") { $hasResidual = $true }
    }
    if (-not $hasResidual) { $failures += "must include residual-risk evidence domain" }
    if (-not $check.ato.expiryMonths -or $check.ato.expiryMonths -le 0) {
        $failures += "ato.expiryMonths must be positive"
    }
    if ($check.ato.liveAtoClaim -ne $false) { $failures += "ato.liveAtoClaim must be false" }
}

$docFile = Resolve-RepoPath "docs/NAT_002_SECURITY_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-002") { $failures += "doc missing NAT-002 reference" }
    if ($doc -notmatch "security|privacy|assessor") { $failures += "doc must describe security/privacy certification" }
    if ($doc -notmatch "SOV-006|pen") { $failures += "doc must reference pen/red or SOV-006" }
}

$outDir = Resolve-RepoPath "testdata/nat002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-002"
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

Write-Host "PASS: NAT-002 security/privacy cert validate"
exit 0
