# L10N-002: validate Arabic normalization artifacts.
# Usage:
#   powershell -File scripts/l10n002-validate.ps1

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
    "docs/L10N_002_ARABIC_NORMALIZATION.md",
    "scripts/l10n002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/L10n002ArabicNormalizationTests.cs",
    "src/Tests/HRManagement.IntegrationTests/L10n002NormalizationIntegrationTests.cs",
    "src/Shared/HRManagement.SharedKernel/Localization/ArabicTextNormalizer.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/ArabicNameNormalizationInterceptor.cs",
    "src/Services/HRCore/HR.Core.Infrastructure/Migrations/20260731181000_L10N002_ArabicNormalization.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate doc content
$docFile = Resolve-RepoPath "docs/L10N_002_ARABIC_NORMALIZATION.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "L10N-002") { $failures += "doc missing L10N-002 reference" }
    if ($doc -notmatch "hamza|Hamza|همزة") { $failures += "doc must mention hamza normalization" }
    if ($doc -notmatch "diacrit") { $failures += "doc must mention diacritics" }
    if ($doc -notmatch "ArabicTextNormalizer") { $failures += "doc must mention ArabicTextNormalizer" }
    if ($doc -notmatch "RuleVersion") { $failures += "doc must mention versioned rules" }
}

# Validate normalizer source
$normFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Localization/ArabicTextNormalizer.cs"
if (Test-Path $normFile) {
    $norm = Get-Content -Raw -Path $normFile
    if ($norm -notmatch "Normalize\(") { $failures += "normalizer must have Normalize method" }
    if ($norm -notmatch "AreEquivalent") { $failures += "normalizer must have AreEquivalent method" }
    if ($norm -notmatch "RuleVersion") { $failures += "normalizer must have RuleVersion" }
    if ($norm -notmatch "\\u0627") { $failures += "normalizer must handle alef (U+0627)" }
    if ($norm -notmatch "\\u064B") { $failures += "normalizer must handle diacritics" }
    if ($norm -notmatch "\\u0640") { $failures += "normalizer must handle tatweel" }
}

# Validate Employee normalized columns
$employeeFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Domain/Entities/Employee.cs"
if (-not (Test-Path $employeeFile)) {
    $failures += "missing Employee.cs"
} else {
    $emp = Get-Content -Raw -Path $employeeFile
    foreach ($prop in @(
        "FirstNameNormalized",
        "MiddleNameNormalized",
        "LastNameNormalized",
        "FullNameNormalized",
        "NormalizationVersion"
    )) {
        if ($emp -notmatch [regex]::Escape($prop)) {
            $failures += "Employee missing $prop"
        }
    }
}

# Validate interceptor
$interceptorFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/ArabicNameNormalizationInterceptor.cs"
if (Test-Path $interceptorFile) {
    $ix = Get-Content -Raw -Path $interceptorFile
    if ($ix -notmatch "SaveChangesInterceptor") { $failures += "interceptor must extend SaveChangesInterceptor" }
    if ($ix -notmatch "ArabicTextNormalizer\.Normalize") { $failures += "interceptor must call ArabicTextNormalizer.Normalize" }
    if ($ix -notmatch "ArabicTextNormalizer\.RuleVersion") { $failures += "interceptor must set RuleVersion" }
    if ($ix -notmatch "FullNameNormalized") { $failures += "interceptor must stamp FullNameNormalized" }
}

# Validate LookupApiController uses ArabicTextNormalizer
$lookupFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Controllers/LookupApiController.cs"
if (-not (Test-Path $lookupFile)) {
    $failures += "missing LookupApiController.cs"
} else {
    $lookup = Get-Content -Raw -Path $lookupFile
    if ($lookup -notmatch "ArabicTextNormalizer") { $failures += "LookupApiController must use ArabicTextNormalizer" }
    if ($lookup -notmatch "FullNameNormalized") { $failures += "LookupApiController must search FullNameNormalized" }
}

# Validate Program.cs registers interceptor
$programFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Program.cs"
if (Test-Path $programFile) {
    $program = Get-Content -Raw -Path $programFile
    if ($program -notmatch "ArabicNameNormalizationInterceptor") {
        $failures += "Program.cs must register ArabicNameNormalizationInterceptor"
    }
}

$outDir = Resolve-RepoPath "testdata/l10n002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "L10N-002"
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

Write-Host "PASS: L10N-002 Arabic normalization validate"
exit 0
