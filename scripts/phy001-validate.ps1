# PHY-001: validate physical site autonomy artifacts.
# Usage:
#   powershell -File scripts/phy001-validate.ps1

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
    "docs/PHY_001_PHYSICAL.md",
    "docs/phy/site-autonomy.json",
    "scripts/phy001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Phy001PhysicalTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$siteFile = Resolve-RepoPath "docs/phy/site-autonomy.json"
if (Test-Path $siteFile) {
    $json = Get-Content -Raw -Path $siteFile | ConvertFrom-Json
    if ($json.card -ne "PHY-001") { $failures += "site-autonomy.json card must be PHY-001" }
    if ($json.sites.Count -lt 2) { $failures += "sites must include primary and DR" }
    if ($json.branchMinimum.governorateCount -ne 14) { $failures += "branchMinimum must cover 14 governorates" }
    if (-not $json.verification.measuredNotPaperOnly) { $failures += "verification must require measured autonomy" }
    foreach ($site in $json.sites) {
        if (-not $site.power.dualFeed) { $failures += "site $($site.id) must have dual power feed" }
        if (-not $site.connectivity.dualProvider) { $failures += "site $($site.id) must have dual connectivity provider" }
        if ($site.connectivity.singlePathAllowed) { $failures += "site $($site.id) must not allow single connectivity path" }
        if ($site.measuredAutonomyHours -lt 24) { $failures += "site $($site.id) measuredAutonomyHours too low" }
    }
}

$docFile = Resolve-RepoPath "docs/PHY_001_PHYSICAL.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "PHY-001") { $failures += "doc missing PHY-001 reference" }
    if ($doc -notmatch "generator|fuel") { $failures += "doc must address generator/fuel" }
    if ($doc -notmatch "cooling") { $failures += "doc must address cooling" }
    if ($doc -notmatch "branch") { $failures += "doc must address branch minimum" }
}

$outDir = Resolve-RepoPath "testdata/phy001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "PHY-001"
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

Write-Host "PASS: PHY-001 physical validate"
exit 0
