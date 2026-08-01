# SYR-009: validate civil registry identity match rules artifacts.
# Usage:
#   powershell -File scripts/syr009-validate.ps1

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
    "docs/SYR_009_CIVIL_REGISTRY.md",
    "docs/syr/civil-identity-match-rules.json",
    "scripts/syr009-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr009CivilIdentityTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$rulesFile = Resolve-RepoPath "docs/syr/civil-identity-match-rules.json"
if (Test-Path $rulesFile) {
    $rules = Get-Content -Raw -Path $rulesFile | ConvertFrom-Json
    if ($rules.card -ne "SYR-009") { $failures += "rules card must be SYR-009" }

    if (-not $rules.officialIdentifier -or $rules.officialIdentifier.field -ne "nationalId") {
        $failures += "rules must define nationalId as official identifier"
    }

    if (-not $rules.matchRules -or $rules.matchRules.Count -lt 2) {
        $failures += "rules must define at least 2 match rules"
    }

    if (-not $rules.deduplication -or $rules.deduplication.autoMergeForbidden -ne $true) {
        $failures += "rules must forbid auto-merge for duplicates"
    }

    if (-not $rules.outagePolicy -or $rules.outagePolicy.forbidPermanentIdentityWithoutReview -ne $true) {
        $failures += "rules must forbid permanent identity without review during outage"
    }

    if (-not $rules.audit -or $rules.audit.required -ne $true) {
        $failures += "rules must require audit logging"
    }

    if (-not $rules.hrBoundaryRule) {
        $failures += "rules must define HR boundary (not substitute civil registry)"
    }
}

$docFile = Resolve-RepoPath "docs/SYR_009_CIVIL_REGISTRY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-009") { $failures += "doc missing SYR-009 reference" }
    if ($doc -notmatch "EXT-002") { $failures += "doc must reference EXT-002 relationship" }
}

$outDir = Resolve-RepoPath "testdata/syr009/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-009"
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

Write-Host "PASS: SYR-009 civil registry identity validate"
exit 0
