# SYR-015: validate Syrian records retention matrix artifacts.
# Usage:
#   powershell -File scripts/syr015-validate.ps1

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
    "docs/SYR_015_RECORDS_MATRIX.md",
    "docs/syr/records-retention-matrix.json",
    "scripts/syr015-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr015RecordsTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$matrixFile = Resolve-RepoPath "docs/syr/records-retention-matrix.json"
if (Test-Path $matrixFile) {
    $json = Get-Content -Raw -Path $matrixFile | ConvertFrom-Json
    if ($json.card -ne "SYR-015") { $failures += "records-retention-matrix.json card must be SYR-015" }
    if ($json.recordClasses.Count -lt 10) { $failures += "records-retention-matrix.json must have at least 10 record classes" }
    if (-not $json.legalHoldPolicy.blocksDisposition) { $failures += "legalHoldPolicy must block disposition" }
    if (-not $json.disclosurePolicy.noForeignStandardImport) { $failures += "disclosurePolicy must reject foreign standard import" }
    if (-not $json.disposalPolicy.requiresCertificate) { $failures += "disposalPolicy must require certificate" }
    foreach ($cls in $json.recordClasses) {
        if (-not $cls.code) { $failures += "record class missing code" }
        if (-not $cls.owner) { $failures += "record class $($cls.code) missing owner" }
        if (-not $cls.legalBasis) { $failures += "record class $($cls.code) missing legalBasis" }
    }
}

$docFile = Resolve-RepoPath "docs/SYR_015_RECORDS_MATRIX.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-015") { $failures += "doc missing SYR-015 reference" }
    if ($doc -notmatch "legal hold") { $failures += "doc must mention legal hold" }
    if ($doc -notmatch "Law 12") { $failures += "doc must reference Law 12/2024 disclosure" }
    if ($doc -notmatch "disposal|destruction") { $failures += "doc must address disposal/destruction" }
}

$outDir = Resolve-RepoPath "testdata/syr015/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-015"
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

Write-Host "PASS: SYR-015 records retention validate"
exit 0
