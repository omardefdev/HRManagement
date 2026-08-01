# SUP-003: validate competency/knowledge transfer artifacts.
# Usage:
#   powershell -File scripts/sup003-validate.ps1

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
    "docs/SUP_003_COMPETENCY.md",
    "docs/sup/competency-matrix.json",
    "scripts/sup003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Sup003CompetencyTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Validate competency matrix
$matrixFile = Resolve-RepoPath "docs/sup/competency-matrix.json"
if (Test-Path $matrixFile) {
    $matrix = Get-Content -Raw -Path $matrixFile | ConvertFrom-Json
    if ($matrix.card -ne "SUP-003") { $failures += "matrix card must be SUP-003" }
    if (-not $matrix.skills -or $matrix.skills.Count -lt 5) {
        $failures += "matrix must have at least 5 Tier-0 skills"
    }
    foreach ($skill in $matrix.skills) {
        if ($skill.tier -ne 0) { $failures += "skill $($skill.id) must be Tier-0" }
        if (-not $skill.primary) { $failures += "skill $($skill.id) missing primary owner" }
        if (-not $skill.secondary) { $failures += "skill $($skill.id) missing secondary owner" }
        if ($skill.primary -eq $skill.secondary) {
            $failures += "skill $($skill.id) primary and secondary must differ"
        }
    }
    if ($matrix.coveragePolicy.minimumDepth -lt 2) {
        $failures += "coverage policy minimumDepth must be >= 2"
    }
}

# Validate doc content
$docFile = Resolve-RepoPath "docs/SUP_003_COMPETENCY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SUP-003") { $failures += "doc missing SUP-003 reference" }
    if ($doc -notmatch "two-deep|minimumDepth") { $failures += "doc must mention two-deep coverage" }
    if ($doc -notmatch "Tier-0") { $failures += "doc must mention Tier-0" }
}

$outDir = Resolve-RepoPath "testdata/sup003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SUP-003"
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

Write-Host "PASS: SUP-003 competency and knowledge transfer validate"
exit 0
