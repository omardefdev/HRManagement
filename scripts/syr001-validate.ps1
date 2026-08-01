# SYR-001: validate Syrian legislation repository artifacts.
# Usage:
#   powershell -File scripts/syr001-validate.ps1

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
    "docs/SYR_001_LEGISLATION_REPOSITORY.md",
    "docs/syr/legislation-register.json",
    "docs/syr/legal-source-binding.md",
    "scripts/syr001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr001LegislationTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate legislation register
$regFile = Resolve-RepoPath "docs/syr/legislation-register.json"
if (Test-Path $regFile) {
    $reg = Get-Content -Raw -Path $regFile | ConvertFrom-Json
    if ($reg.card -ne "SYR-001") { $failures += "register card must be SYR-001" }
    if (-not $reg.register -or $reg.register.Count -lt 2) {
        $failures += "register must have at least 2 legislation entries"
    }

    $hasLaw50 = $false
    $hasLaw53 = $false
    foreach ($entry in $reg.register) {
        if (-not $entry.legalSourceId) { $failures += "entry missing legalSourceId" }
        if (-not $entry.type) { $failures += "entry $($entry.legalSourceId) missing type" }
        if (-not $entry.number) { $failures += "entry $($entry.legalSourceId) missing number" }
        if (-not $entry.effectiveDate) { $failures += "entry $($entry.legalSourceId) missing effectiveDate" }
        if ($entry.number -eq "50/2004") { $hasLaw50 = $true }
        if ($entry.number -eq "53/2021") { $hasLaw53 = $true }
    }
    if (-not $hasLaw50) { $failures += "register must include Law 50/2004" }
    if (-not $hasLaw53) { $failures += "register must include Law 53/2021" }
}

# Validate binding doc
$bindFile = Resolve-RepoPath "docs/syr/legal-source-binding.md"
if (Test-Path $bindFile) {
    $bind = Get-Content -Raw -Path $bindFile
    if ($bind -notmatch "legal_source_id") { $failures += "binding doc must mention legal_source_id" }
    if ($bind -notmatch "article") { $failures += "binding doc must mention article references" }
    if ($bind -notmatch "effective") { $failures += "binding doc must mention effective dates" }
}

# Validate main doc
$docFile = Resolve-RepoPath "docs/SYR_001_LEGISLATION_REPOSITORY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-001") { $failures += "doc missing SYR-001 reference" }
    if ($doc -notmatch "50/2004") { $failures += "doc must mention Law 50/2004" }
    if ($doc -notmatch "53/2021") { $failures += "doc must mention Law 53/2021" }
}

$outDir = Resolve-RepoPath "testdata/syr001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-001"
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

Write-Host "PASS: SYR-001 Syrian legislation repository validate"
exit 0
