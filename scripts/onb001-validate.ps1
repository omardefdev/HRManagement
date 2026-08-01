# ONB-001: validate agency provisioning manifest artifacts.
# Usage:
#   powershell -File scripts/onb001-validate.ps1

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
    "docs/ONB_001_PROVISIONING.md",
    "docs/onb/agency-manifest-schema.json",
    "scripts/onb001-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Onb001ProvisioningTests.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$schemaFile = Resolve-RepoPath "docs/onb/agency-manifest-schema.json"
if (Test-Path $schemaFile) {
    $json = Get-Content -Raw -Path $schemaFile | ConvertFrom-Json
    if ($json.card -ne "ONB-001") { $failures += "agency-manifest-schema.json card must be ONB-001" }
    if ($json.required.Count -lt 10) { $failures += "schema must require at least 10 top-level fields" }
    if (-not $json.provisioningRules.noSecretInPack) { $failures += "provisioningRules must forbid secrets in pack" }
    if (-not $json.provisioningRules.noManualDbEdits) { $failures += "provisioningRules must forbid manual DB edits" }
    if (-not $json.provisioningRules.rollbackSupported) { $failures += "provisioningRules must support rollback" }
    if ($json.exampleManifest.reviewerRoles.Count -lt 2) { $failures += "exampleManifest must have >= 2 reviewer roles" }
    if (-not $json.exampleManifest.policies.preserveNationalMandatory) { $failures += "example must preserve national mandatory controls" }
}

$docFile = Resolve-RepoPath "docs/ONB_001_PROVISIONING.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "ONB-001") { $failures += "doc missing ONB-001 reference" }
    if ($doc -notmatch "manifest") { $failures += "doc must address manifest" }
    if ($doc -notmatch "rollback") { $failures += "doc must address rollback" }
    if ($doc -notmatch "secret") { $failures += "doc must address no-secret rule" }
}

$outDir = Resolve-RepoPath "testdata/onb001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "ONB-001"
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

Write-Host "PASS: ONB-001 provisioning validate"
exit 0
