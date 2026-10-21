# MIG-003 COD sequence bootstrap — hybrid counter seed (no code rewrite).
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

$lineageDoc = Join-Path $RepoRoot "docs/MIG_003_COD_CODE_LINEAGE.md"
if (-not (Test-Path $lineageDoc)) {
    $failures.Add("missing docs/MIG_003_COD_CODE_LINEAGE.md")
}

$bootstrapService = Join-Path $RepoRoot "src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodSequenceBootstrapService.cs"
if (-not (Test-Path $bootstrapService)) {
    $failures.Add("missing CodSequenceBootstrapService.cs")
}

$testProject = Join-Path $RepoRoot "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj"
if (-not (Test-Path $testProject)) {
    $failures.Add("missing HRManagement.UnitTests.csproj")
} else {
    Write-Host "Running CodSequenceBootstrap idempotency tests..." -ForegroundColor Cyan
    dotnet test $testProject --filter "FullyQualifiedName~CodSequenceBootstrapTests" --no-restore 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) {
        $failures.Add("CodSequenceBootstrapTests failed")
    }
}

if ($failures.Count -gt 0) {
    Write-Host "MIG-003 COD bootstrap validation failed:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "MIG-003 COD sequence bootstrap validation passed." -ForegroundColor Green
Write-Host "Post-load: call CodSequenceBootstrapService.BootstrapHRCoreAsync() and BootstrapFromObservedCodesAsync() per service DB." -ForegroundColor DarkGray
exit 0
