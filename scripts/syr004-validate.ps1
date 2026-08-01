# SYR-004: validate Syrian currency conversion artifacts.
# Usage:
#   powershell -File scripts/syr004-validate.ps1

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
    "docs/SYR_004_CURRENCY.md",
    "docs/syr/currency-era.json",
    "src/Shared/HRManagement.SharedKernel/Currency/SyrianCurrencyConverter.cs",
    "scripts/syr004-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr004CurrencyTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify SyrianCurrencyConverter
$converterFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Currency/SyrianCurrencyConverter.cs"
if (Test-Path $converterFile) {
    $content = Get-Content -Raw -Path $converterFile
    $requiredMembers = @("ConversionRatio", "ToNewLira", "ToOldLira", "GetEra", "Normalize", "RoundOfficial", "CurrencyEra", "100")
    foreach ($member in $requiredMembers) {
        if ($content -notmatch [regex]::Escape($member)) {
            $failures += "SyrianCurrencyConverter missing required member: $member"
        }
    }
    if ($content -match "float|double") {
        $failures += "SyrianCurrencyConverter must not use float/double for monetary values"
    }
}

# Verify currency-era.json structure
$eraFile = Resolve-RepoPath "docs/syr/currency-era.json"
if (Test-Path $eraFile) {
    $json = Get-Content -Raw -Path $eraFile | ConvertFrom-Json
    if ($json.conversionRatio -ne 100) { $failures += "currency-era.json conversionRatio must be 100" }
    if ($json.card -ne "SYR-004") { $failures += "currency-era.json card must be SYR-004" }
    if (-not $json.cutoverDate) { $failures += "currency-era.json must have cutoverDate" }
    if (-not $json.rules.noFloatingPoint) { $failures += "currency-era.json must enforce noFloatingPoint" }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/SYR_004_CURRENCY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-004") { $failures += "doc missing SYR-004 reference" }
    if ($doc -notmatch "100:1|100.*1") { $failures += "doc must mention 100:1 conversion ratio" }
    if ($doc -notmatch "CurrencyEra") { $failures += "doc must describe CurrencyEra" }
}

$outDir = Resolve-RepoPath "testdata/syr004/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-004"
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

Write-Host "PASS: SYR-004 currency conversion validate"
exit 0
