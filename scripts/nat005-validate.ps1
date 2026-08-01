# NAT-005: validate accessibility/field certification checklist artifacts.
# Usage:
#   powershell -File scripts/nat005-validate.ps1

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
    "docs/NAT_005_ACCESSIBILITY_CERT.md",
    "docs/nat/nat005-checklist.json",
    "scripts/nat005-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Nat005AccessibilityCertTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$checkFile = Resolve-RepoPath "docs/nat/nat005-checklist.json"
if (Test-Path $checkFile) {
    $check = Get-Content -Raw -Path $checkFile | ConvertFrom-Json
    if ($check.card -ne "NAT-005") { $failures += "checklist card must be NAT-005" }
    if ($check.harnessStatus -ne "Done") { $failures += "harnessStatus must be Done" }
    if ($check.wcagVersion -ne "2.2") { $failures += "wcagVersion must be 2.2" }
    if ($check.conformanceLevel -ne "AA") { $failures += "conformanceLevel must be AA" }
    if ($check.accessibilityBlockers -ne 0) { $failures += "accessibilityBlockers must be 0" }
    if (-not $check.localization.rtlFirst) { $failures += "rtlFirst must be true" }
    if (-not $check.criticalJourneys -or $check.criticalJourneys.Count -lt 3) {
        $failures += "must define at least 3 critical journeys"
    }
    if (-not $check.verificationMethods -or $check.verificationMethods.Count -lt 3) {
        $failures += "must define at least 3 verification methods"
    }
}

$docFile = Resolve-RepoPath "docs/NAT_005_ACCESSIBILITY_CERT.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "NAT-005") { $failures += "doc missing NAT-005 reference" }
    if ($doc -notmatch "WCAG|accessibility") { $failures += "doc must mention WCAG/accessibility" }
    if ($doc -notmatch "RTL|Arabic|field") { $failures += "doc must mention RTL/Arabic or field acceptance" }
}

$outDir = Resolve-RepoPath "testdata/nat005/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "NAT-005"
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

Write-Host "PASS: NAT-005 accessibility/field cert validate"
exit 0
