# SYR-008: validate national hosting artifacts.
# Usage:
#   powershell -File scripts/syr008-validate.ps1

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
    "docs/SYR_008_NATIONAL_HOSTING.md",
    "docs/syr/hosting-topology.json",
    "docs/syr/certificate-lifecycle.md",
    "scripts/syr008-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr008HostingTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$topoFile = Resolve-RepoPath "docs/syr/hosting-topology.json"
if (Test-Path $topoFile) {
    $topo = Get-Content -Raw -Path $topoFile | ConvertFrom-Json
    if ($topo.card -ne "SYR-008") { $failures += "topology card must be SYR-008" }
    if (-not $topo.sites -or $topo.sites.Count -lt 2) {
        $failures += "topology must define at least primary and DR sites"
    }
    foreach ($site in $topo.sites) {
        if ($site.sovereignCustody -ne $true) {
            $failures += "site $($site.id) must have sovereignCustody=true"
        }
        if ($site.networkEgress -ne "deny-by-default") {
            $failures += "site $($site.id) must have deny-by-default egress"
        }
    }
    if (-not $topo.dns -or $topo.dns.primaryDomain -notmatch "\.gov\.sy") {
        $failures += "DNS must use .gov.sy domain"
    }
}

$certFile = Resolve-RepoPath "docs/syr/certificate-lifecycle.md"
if (Test-Path $certFile) {
    $cert = Get-Content -Raw -Path $certFile
    if ($cert -notmatch "HSM") { $failures += "certificate lifecycle must mention HSM" }
    if ($cert -notmatch "revocation") { $failures += "certificate lifecycle must mention revocation" }
    if ($cert -notmatch "OCSP") { $failures += "certificate lifecycle must mention OCSP" }
}

$docFile = Resolve-RepoPath "docs/SYR_008_NATIONAL_HOSTING.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-008") { $failures += "doc missing SYR-008 reference" }
    if ($doc -notmatch "\.gov\.sy") { $failures += "doc must mention .gov.sy" }
}

$outDir = Resolve-RepoPath "testdata/syr008/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-008"
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

Write-Host "PASS: SYR-008 national hosting validate"
exit 0
