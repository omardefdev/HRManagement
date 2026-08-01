# IOP-002: validate data portability artifacts.
# Usage:
#   powershell -File scripts/iop002-validate.ps1

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
    "docs/IOP_002_PORTABILITY.md",
    "docs/iop/export-manifest-schema.json",
    "scripts/iop002-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Iop002PortabilityTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$schemaFile = Resolve-RepoPath "docs/iop/export-manifest-schema.json"
if (Test-Path $schemaFile) {
    $schema = Get-Content -Raw -Path $schemaFile | ConvertFrom-Json
    if ($schema.card -ne "IOP-002") { $failures += "schema card must be IOP-002" }
    if (-not $schema.required -or $schema.required.Count -lt 5) {
        $failures += "manifest schema must have at least 5 required fields"
    }
    if (-not $schema.properties.entities) {
        $failures += "manifest schema must define entities property"
    }
    if (-not $schema.properties.controlTotals) {
        $failures += "manifest schema must define controlTotals for reconciliation"
    }
    if (-not $schema.properties.checksums) {
        $failures += "manifest schema must define checksums for integrity"
    }
}

$docFile = Resolve-RepoPath "docs/IOP_002_PORTABILITY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "IOP-002") { $failures += "doc missing IOP-002 reference" }
    if ($doc -notmatch "manifest") { $failures += "doc must describe export manifest" }
    if ($doc -notmatch "reconciliation|Reconciliation") { $failures += "doc must describe reconciliation" }
}

$outDir = Resolve-RepoPath "testdata/iop002/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "IOP-002"
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

Write-Host "PASS: IOP-002 data portability validate"
exit 0
