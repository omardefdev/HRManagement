# TRN-001: validate training/change management artifacts.
# Usage:
#   powershell -File scripts/trn001-validate.ps1

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
    "docs/TRN_001_TRAINING.md",
    "docs/trn/persona-curricula.json",
    "scripts/trn001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Trn001TrainingTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$modelFile = Resolve-RepoPath "docs/trn/persona-curricula.json"
if (Test-Path $modelFile) {
    $json = Get-Content -Raw -Path $modelFile | ConvertFrom-Json
    if ($json.card -ne "TRN-001") { $failures += "persona-curricula.json card must be TRN-001" }
    if ($json.personas.Count -lt 5) { $failures += "persona-curricula.json must have at least 5 personas" }
    if ($json.curriculumStructure.Count -lt 4) { $failures += "persona-curricula.json must have at least 4 curriculum modules" }
    if ($json.readinessThresholds.trainingCompletionRate -lt 0.80) { $failures += "training completion threshold must be at least 80%" }
    if (-not $json.changeManagement.resistanceRegister) { $failures += "persona-curricula.json must include resistance register" }
}

$docFile = Resolve-RepoPath "docs/TRN_001_TRAINING.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "TRN-001") { $failures += "doc missing TRN-001 reference" }
    if ($doc -notmatch "persona|Persona") { $failures += "doc must address personas" }
    if ($doc -notmatch "curricul|Curricul") { $failures += "doc must address curricula" }
    if ($doc -notmatch "readiness|Readiness") { $failures += "doc must address readiness" }
    if ($doc -notmatch "resistance|Resistance") { $failures += "doc must address resistance management" }
}

$outDir = Resolve-RepoPath "testdata/trn001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "TRN-001"
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

Write-Host "PASS: TRN-001 training/change validate"
exit 0
