# EVD-003: validate evidence repository artifacts and implementation.
# Usage:
#   powershell -File scripts/evd003-validate.ps1

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
    "docs/EVD_003_REPOSITORY.md",
    "docs/evd/evidence-store-model.json",
    "scripts/evd003-validate.ps1",
    "src/Tests/HRManagement.UnitTests/Evd003RepositoryTests.cs",
    "src/Tests/HRManagement.UnitTests/Evd003EvidenceRepositoryTests.cs",
    "src/Shared/HRManagement.SharedKernel/Evidence/EvidenceArtifact.cs",
    "src/Shared/HRManagement.SharedKernel/Evidence/EvidencePackExport.cs",
    "src/Shared/HRManagement.ServiceDefaults/Evidence/EvidenceRepositoryService.cs",
    "src/Shared/HRManagement.ServiceDefaults/Evidence/EvidenceImmutabilityInterceptor.cs",
    "src/Gateway/HRManagement.Gateway/Controllers/EvidenceArtifactsController.cs"
)

foreach ($rel in $requiredFiles) {
    if (-not (Test-Path (Resolve-RepoPath $rel))) {
        $failures += "missing $rel"
    }
}

$modelFile = Resolve-RepoPath "docs/evd/evidence-store-model.json"
if (Test-Path $modelFile) {
    $json = Get-Content -Raw -Path $modelFile | ConvertFrom-Json
    if ($json.card -ne "EVD-003") { $failures += "evidence-store-model.json card must be EVD-003" }
    if (-not $json.immutability.appendOnly) { $failures += "model must enforce append-only" }
    if (-not $json.immutability.adminCannotModify) { $failures += "model must block admin modification" }
    if (-not $json.immutability.wormStorage) { $failures += "model must use WORM storage" }
    if (-not $json.automatedCollection.ciCdPublish) { $failures += "model must support CI/CD publish" }
    if (-not $json.accessControl.auditLogForAllAccess) { $failures += "model must audit all access" }
    if (-not $json.freshness.expiredCreatesVisibleGap) { $failures += "model must create visible gap on expiry" }
    if (-not $json.piiProtection.piiBlocked) { $failures += "model must block PII" }
    if (-not $json.sovereignBoundary.storedWithinSovereignTerritory) { $failures += "model must store within sovereign territory" }
}

$docFile = Resolve-RepoPath "docs/EVD_003_REPOSITORY.md"
if (Test-Path $docFile) {
    $doc = Get-Content -Raw -Path $docFile
    if ($doc -notmatch "EVD-003") { $failures += "doc missing EVD-003 reference" }
    if ($doc -notmatch "immutab|Immutab") { $failures += "doc must address immutability" }
    if ($doc -notmatch "WORM|worm") { $failures += "doc must address WORM storage" }
    if ($doc -notmatch "assessor|Assessor") { $failures += "doc must address assessor access" }
    if ($doc -notmatch "expir|Expir") { $failures += "doc must address expiration" }
}

$artifactFile = Resolve-RepoPath "src/Shared/HRManagement.SharedKernel/Evidence/EvidenceArtifact.cs"
if (Test-Path $artifactFile) {
    $src = Get-Content -Raw -Path $artifactFile
    if ($src -notmatch "ContentHash") { $failures += "EvidenceArtifact must have ContentHash" }
    if ($src -notmatch "StorageUri") { $failures += "EvidenceArtifact must have StorageUri" }
    if ($src -notmatch "IsExpired") { $failures += "EvidenceArtifact must expose IsExpired" }
    if ($src -match "(?m)^\s*public\s+.*\bUpdatedAt\b") {
        $failures += "EvidenceArtifact must not have UpdatedAt property (append-only)"
    }
}

$svcFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Evidence/EvidenceRepositoryService.cs"
if (Test-Path $svcFile) {
    $src = Get-Content -Raw -Path $svcFile
    if ($src -notmatch "IngestAsync") { $failures += "EvidenceRepositoryService must implement IngestAsync" }
    if ($src -notmatch "ExportPackAsync") { $failures += "EvidenceRepositoryService must implement ExportPackAsync" }
    if ($src -notmatch "ListExpiredAsync") { $failures += "EvidenceRepositoryService must implement ListExpiredAsync" }
    if ($src -match "Task<.*>\s+Delete") { $failures += "EvidenceRepositoryService must not expose Delete" }
}

$interceptorFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Evidence/EvidenceImmutabilityInterceptor.cs"
if (Test-Path $interceptorFile) {
    $src = Get-Content -Raw -Path $interceptorFile
    if ($src -notmatch "EvidenceArtifact") { $failures += "interceptor must reject EvidenceArtifact mutations" }
}

$controllerFile = Resolve-RepoPath "src/Gateway/HRManagement.Gateway/Controllers/EvidenceArtifactsController.cs"
if (Test-Path $controllerFile) {
    $src = Get-Content -Raw -Path $controllerFile
    if ($src -match "HttpDelete") { $failures += "EvidenceArtifactsController must not expose DELETE" }
    if ($src -notmatch "EvidenceIngest|evidence.ingest|ServicePolicies.EvidenceIngest") {
        $failures += "controller ingest must require service evidence policy"
    }
}

$outDir = Resolve-RepoPath "testdata/evd003/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "EVD-003"
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

Write-Host "PASS: EVD-003 evidence repository validate"
exit 0
