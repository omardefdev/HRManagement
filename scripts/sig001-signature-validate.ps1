# SIG-001: validate legal digital signature artifacts and configuration.
# Usage:
#   powershell -File scripts/sig001-signature-validate.ps1

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

# --- 1. Required source files ---
$requiredFiles = @(
    "src/Shared/HRManagement.SharedKernel/Signatures/SignatureRecord.cs",
    "src/Shared/HRManagement.SharedKernel/Signatures/SignaturePolicy.cs",
    "src/Shared/HRManagement.SharedKernel/Signatures/CanonicalPayloadBuilder.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/ISignatureService.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/SoftwareSignatureService.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/SignatureVerifier.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/SignatureImmutabilityInterceptor.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/SignatureEntityConfiguration.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/LegalSignatureExtensions.cs",
    "src/Shared/HRManagement.ServiceDefaults/Signatures/SignatureRequiredAttribute.cs",
    "src/Services/HRCore/HR.Core.Web/Controllers/SignaturesController.cs"
)

foreach ($f in $requiredFiles) {
    $full = Resolve-RepoPath $f
    if (-not (Test-Path $full)) {
        $failures += "MISSING: $f"
    }
}

# --- 2. Required documentation ---
$requiredDocs = @(
    "docs/SIG_001_SIGNATURE_POLICY.md",
    "docs/SIG_001_VERIFICATION.md"
)

foreach ($d in $requiredDocs) {
    $full = Resolve-RepoPath $d
    if (-not (Test-Path $full)) {
        $failures += "MISSING DOC: $d"
    }
}

# --- 3. Migration file ---
$migrationPattern = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Migrations/*SIG001*"
$migrations = Get-ChildItem -Path $migrationPattern -ErrorAction SilentlyContinue
if ($migrations.Count -eq 0) {
    $failures += "MISSING: SIG-001 migration file"
}

# --- 4. Test file ---
$testFile = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/Sig001SignatureTests.cs"
if (-not (Test-Path $testFile)) {
    $failures += "MISSING: Sig001SignatureTests.cs"
}

# --- 5. Key patterns in source ---
$interceptorFile = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Signatures/SignatureImmutabilityInterceptor.cs"
if (Test-Path $interceptorFile) {
    $content = Get-Content -Raw $interceptorFile
    if ($content -notmatch "append-only") {
        $failures += "SignatureImmutabilityInterceptor missing append-only enforcement"
    }
}

$dbContextFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Infrastructure/Data/HRCoreDbContext.cs"
if (Test-Path $dbContextFile) {
    $content = Get-Content -Raw $dbContextFile
    if ($content -notmatch "SignatureRecords") {
        $failures += "HRCoreDbContext missing SignatureRecords DbSet"
    }
    if ($content -notmatch "ConfigureSignatureRecords") {
        $failures += "HRCoreDbContext missing ConfigureSignatureRecords call"
    }
}

$programFile = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Program.cs"
if (Test-Path $programFile) {
    $content = Get-Content -Raw $programFile
    if ($content -notmatch "SignatureImmutabilityInterceptor") {
        $failures += "Program.cs missing SignatureImmutabilityInterceptor wiring"
    }
    if ($content -notmatch "AddLegalSignatures") {
        $failures += "Program.cs missing AddLegalSignatures DI call"
    }
}

# --- 6. Report ---
$outDir = Resolve-RepoPath "testdata/sig001"
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir -Force | Out-Null }

$report = @{
    card      = "SIG-001"
    timestamp = (Get-Date -Format "o")
    checks    = $requiredFiles.Count + $requiredDocs.Count + 4
    failures  = $failures
    pass      = ($failures.Count -eq 0)
}
$report | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $outDir "validate-report.json")

if ($failures.Count -gt 0) {
    Write-Host "FAIL: SIG-001 signature validate - $($failures.Count) failure(s):"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "PASS: SIG-001 signature validate"
exit 0
