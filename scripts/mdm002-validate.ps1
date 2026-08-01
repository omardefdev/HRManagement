# MDM-002: validate golden person record artifacts.
# Usage:
#   powershell -File scripts/mdm002-validate.ps1

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
    "src/Shared/HRManagement.SharedKernel/MasterData/GoldenPersonRecord.cs",
    "src/Shared/HRManagement.ServiceDefaults/MasterData/PersonMergeService.cs",
    "docs/MDM_002_GOLDEN_RECORD.md",
    "scripts/mdm002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Mdm002GoldenRecordTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$entityFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/MasterData/GoldenPersonRecord.cs"
if (Test-Path $entityFile) {
    $content = Get-Content -Raw -Path $entityFile
    if ($content -notmatch "PersonId") { $failures += "GoldenPersonRecord missing PersonId" }
    if ($content -notmatch "NationalIdHash") { $failures += "GoldenPersonRecord missing NationalIdHash" }
    if ($content -notmatch "MergeHistory") { $failures += "GoldenPersonRecord missing MergeHistory" }
    if ($content -notmatch "ProvenanceJson") { $failures += "GoldenPersonRecord missing ProvenanceJson" }
    if ($content -notmatch "GoldenPersonStatus") { $failures += "GoldenPersonRecord missing Status enum" }
}

$serviceFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/MasterData/PersonMergeService.cs"
if (Test-Path $serviceFile) {
    $content = Get-Content -Raw -Path $serviceFile
    if ($content -notmatch "IPersonMergeService") { $failures += "PersonMergeService missing interface" }
    if ($content -notmatch "Merge") { $failures += "PersonMergeService missing Merge method" }
    if ($content -notmatch "Unmerge") { $failures += "PersonMergeService missing Unmerge method" }
}

$docFile = Resolve-RepoPath "docs/MDM_002_GOLDEN_RECORD.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "MDM-002") { $failures += "doc missing MDM-002 reference" }
    if ($doc -notmatch "reversib") { $failures += "doc must mention reversibility" }
}

$outDir = Resolve-RepoPath "testdata/mdm002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "MDM-002"
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

Write-Host "PASS: MDM-002 golden record validate"
exit 0
