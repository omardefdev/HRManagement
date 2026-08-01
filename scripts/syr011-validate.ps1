# SYR-011: validate payment orders/banks artifacts.
# Usage:
#   powershell -File scripts/syr011-validate.ps1

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
    "docs/SYR_011_PAYMENT_ORDERS.md",
    "docs/syr/payment-channel-profile.json",
    "scripts/syr011-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Syr011PaymentTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

# Verify channel profile structure
$profileFile = Resolve-RepoPath "docs/syr/payment-channel-profile.json"
if (Test-Path $profileFile) {
    $json = Get-Content -Raw -Path $profileFile | ConvertFrom-Json
    if ($json.card -ne "SYR-011") { $failures += "payment-channel-profile.json card must be SYR-011" }
    if ($json.channels.Count -lt 3) { $failures += "payment-channel-profile.json must have at least 3 channels" }
    if ($json.paymentOrderStates.Count -lt 5) { $failures += "payment-channel-profile.json must have at least 5 payment order states" }
    if (-not $json.validationRules.ibanFormat) { $failures += "payment-channel-profile.json must define IBAN format" }
    if (-not $json.reconciliation.invariant) { $failures += "payment-channel-profile.json must define reconciliation invariant" }
    if (-not $json.security.fileSigningRequired) { $failures += "payment-channel-profile.json must require file signing" }
}

# Verify doc references
$docFile = Resolve-RepoPath "docs/SYR_011_PAYMENT_ORDERS.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "SYR-011") { $failures += "doc missing SYR-011 reference" }
    if ($doc -notmatch "IBAN") { $failures += "doc must address IBAN validation" }
    if ($doc -notmatch "reconcil|Reconcil") { $failures += "doc must address reconciliation" }
    if ($doc -notmatch "channel|Channel") { $failures += "doc must address payment channels" }
    if ($doc -notmatch "four.eyes|Four.eyes") { $failures += "doc must address four-eyes approval" }
}

$outDir = Resolve-RepoPath "testdata/syr011/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "SYR-011"
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

Write-Host "PASS: SYR-011 payment orders validate"
exit 0
