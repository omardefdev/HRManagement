# NAT-008: validate national Go/No-Go decision template artifacts.
# Usage:
#   powershell -File scripts/nat008-validate.ps1

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
    "docs/NAT_008_GO_NO_GO.md",
    "docs/nat/go-no-go-decision.json",
    "scripts/nat008-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat008GoNoGoTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/go-no-go-decision.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-008") { $failures += "decision card must be NAT-008" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if ($check.liveDecisionStatus -ne "Blocked") { $failures += "liveDecisionStatus must be Blocked" }
    if (-not $check.natCertReview.requiredCerts -or $check.natCertReview.requiredCerts.Count -lt 7) {
        $failures += "must list NAT-001..007 for review"
    }
    if (-not $check.certification.releaseDigestRequired) {
        $failures += "releaseDigestRequired must be true"
    }
    if (-not $check.certification.expiryDateRequired) {
        $failures += "expiryDateRequired must be true"
    }
    if (-not $check.authoritySignatures -or $check.authoritySignatures.Count -lt 7) {
        $failures += "must define at least 7 authority signature slots"
    }
    if (-not $check.wavePlan.stopRollbackDefined) {
        $failures += "stopRollbackDefined must be true"
    }
}

$docFile = Resolve-RepoPath "docs/NAT_008_GO_NO_GO.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-008") { $failures += "doc missing NAT-008 reference" }
    if ($doc -notmatch "Go.?No.?Go|Go/No-Go") { $failures += "doc must describe Go/No-Go decision" }
    if ($doc -notmatch "digest|expiry|signature") { $failures += "doc must mention digest/expiry/signatures" }
}

$outDir = Resolve-RepoPath "testdata/nat008/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-008"
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

Write-Host "PASS: NAT-008 Go/No-Go validate"
exit 0
