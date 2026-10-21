# COD-001: validate code generation infrastructure and consumer wiring.
param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$failures = [System.Collections.Generic.List[string]]::new()

function Need([string]$rel) {
    $p = Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar)
    if (-not (Test-Path $p)) { $script:failures.Add("missing $rel") }
    return $p
}

Need "docs/COD_001_CODE_GENERATION.md" | Out-Null
Need "docs/MIG_003_COD_CODE_LINEAGE.md" | Out-Null
Need "src/Shared/HRManagement.SharedKernel/CodeGeneration/ICodeGenerator.cs" | Out-Null
Need "src/Services/HRCore/HR.Core.Infrastructure/Services/CodeGeneration/CodeGeneratorService.cs" | Out-Null
Need "src/Shared/HRManagement.ServiceDefaults/CodeGeneration/CodeGenerationClient.cs" | Out-Null
Need "src/Tests/HRManagement.UnitTests/CodeGenerationTests.cs" | Out-Null

foreach ($program in @(
    "src/Services/HRCore/HR.Core.Web/Program.cs",
    "src/Services/AdminLogistics/Admin.Logistics.Web/Program.cs",
    "src/Services/RecordsArchive/Records.Archive.Web/Program.cs",
    "src/Services/Compliance/Compliance.Web/Program.cs",
    "src/Services/IDCard/IDCard.Web/Program.cs",
    "src/Services/HRContracts/HR.Contracts.Web/Program.cs",
    "src/Services/ITInfrastructure/IT.Infra.Web/Program.cs"
)) {
    $path = Need $program
    if (Test-Path $path) {
        $text = Get-Content -Raw $path
        if ($text -notmatch "AddHrCodeGeneration") {
            $failures.Add("$program missing AddHrCodeGeneration()")
        }
    }
}

$hrcoreProgram = Need "src/Services/HRCore/HR.Core.Web/Program.cs"
if (Test-Path $hrcoreProgram) {
    $text = Get-Content -Raw $hrcoreProgram
    if ($text -notmatch "AddHrCodeGenerationEngine") {
        $failures.Add("HRCore missing AddHrCodeGenerationEngine()")
    }
}

$legacyTagPattern = 'TagNumber\s*=\s*\$"AST-'
Get-ChildItem -Path (Join-Path $RepoRoot "src") -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '\\(bin|obj|Tests)\\' } |
    ForEach-Object {
        $text = Get-Content -Raw $_.FullName
        if ($text -match $legacyTagPattern) {
            $rel = $_.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
            $failures.Add("$rel still uses hardcoded AST TagNumber pattern")
        }
    }

if ($failures.Count -gt 0) {
    Write-Host "Code generation validation failed:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "Code generation validation passed." -ForegroundColor Green
exit 0
