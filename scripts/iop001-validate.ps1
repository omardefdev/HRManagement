# IOP-001: validate semantic model artifacts.
# Usage:
#   powershell -File scripts/iop001-validate.ps1

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
    "docs/IOP_001_SEMANTIC_MODEL.md",
    "docs/iop/canonical-identifiers.json",
    "docs/iop/schema-registry-profile.json",
    "scripts/iop001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Iop001SemanticTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$idsFile = Resolve-RepoPath "docs/iop/canonical-identifiers.json"
if (Test-Path $idsFile) {
    $ids = Get-Content -Raw -Path $idsFile | ConvertFrom-Json
    if ($ids.card -ne "IOP-001") { $failures += "canonical identifiers card must be IOP-001" }
    if (-not $ids.identifiers -or $ids.identifiers.Count -lt 3) {
        $failures += "must define at least 3 canonical identifiers"
    }
    foreach ($id in $ids.identifiers) {
        if (-not $id.name) { $failures += "identifier missing name" }
        if (-not $id.owner) { $failures += "identifier $($id.name) missing owner" }
    }
}

$profileFile = Resolve-RepoPath "docs/iop/schema-registry-profile.json"
if (Test-Path $profileFile) {
    $profile = Get-Content -Raw -Path $profileFile | ConvertFrom-Json
    if ($profile.card -ne "IOP-001") { $failures += "schema profile card must be IOP-001" }
    if (-not $profile.profiles -or $profile.profiles.Count -lt 2) {
        $failures += "must define at least 2 integration profiles"
    }
}

$docFile = Resolve-RepoPath "docs/IOP_001_SEMANTIC_MODEL.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "IOP-001") { $failures += "doc missing IOP-001 reference" }
    if ($doc -notmatch "canonical") { $failures += "doc must mention canonical identifiers" }
}

$outDir = Resolve-RepoPath "testdata/iop001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "IOP-001"
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

Write-Host "PASS: IOP-001 semantic model validate"
exit 0
