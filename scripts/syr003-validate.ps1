# SYR-003: validate service regimes artifacts.
# Usage:
#   powershell -File scripts/syr003-validate.ps1

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
    "docs/SYR_003_SERVICE_REGIMES.md",
    "docs/syr/service-regimes.json",
    "src/Shared/HRManagement.SharedKernel/Legal/LegalRegime.cs",
    "scripts/syr003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr003ServiceRegimeTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify LegalRegime entity has required properties
$regimeFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Legal/LegalRegime.cs"
if (Test-Path $regimeFile) {
    $content = Get-Content -Raw -Path $regimeFile
    $requiredProps = @("LegalRegimeId", "RegimeName", "Type", "LegislationRef",
                       "AllowedEmploymentTypes", "EffectiveFrom", "IsActive",
                       "TransitionDecisionRef", "ConflictNotes")
    foreach ($prop in $requiredProps) {
        if ($content -notmatch $prop) {
            $failures += "LegalRegime missing required property: $prop"
        }
    }
    if ($content -notmatch "BaseEntity") {
        $failures += "LegalRegime must extend BaseEntity"
    }
    if ($content -notmatch "Law50") {
        $failures += "LegalRegime must define Law50 regime type"
    }
    if ($content -notmatch "Law53") {
        $failures += "LegalRegime must define Law53 regime type"
    }
    if ($content -notmatch "Special") {
        $failures += "LegalRegime must define Special regime type"
    }
}

# Verify service-regimes.json structure
$jsonFile = Resolve-RepoPath "docs/syr/service-regimes.json"
if (Test-Path $jsonFile) {
    $content = Get-Content -Raw -Path $jsonFile
    $requiredKeys = @("regimes", "employment_types", "transition_rules", "exception_handling")
    foreach ($key in $requiredKeys) {
        if ($content -notmatch $key) {
            $failures += "service-regimes.json missing key: $key"
        }
    }
    if ($content -notmatch "law50") {
        $failures += "service-regimes.json must define law50 regime"
    }
    if ($content -notmatch "law53") {
        $failures += "service-regimes.json must define law53 regime"
    }
    if ($content -notmatch "blocks_final_payroll") {
        $failures += "service-regimes.json must specify payroll blocking for unclassified"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_003_SERVICE_REGIMES.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-003") { $failures += "doc missing SYR-003 reference" }
    if ($doc -notmatch "Law 50") { $failures += "doc must reference Law 50" }
    if ($doc -notmatch "Law 53") { $failures += "doc must reference Law 53" }
    if ($doc -notmatch "LegalRegime") { $failures += "doc must describe LegalRegime entity" }
}

$outDir = Resolve-RepoPath "testdata/syr003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-003"
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

Write-Host "PASS: SYR-003 service regimes validate"
exit 0
