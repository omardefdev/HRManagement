# DOC-001: validate official documents deliverables.
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

Need "docs/DOC_001_OFFICIAL_DOCUMENTS.md" | Out-Null
Need "scripts/doc001-validate.ps1" | Out-Null
Need "src/Tests/HRManagement.UnitTests/Doc001DocumentsTests.cs" | Out-Null

$templatePath = Need "src/Shared/HRManagement.SharedKernel/Documents/OfficialDocumentTemplate.cs"
if (Test-Path $templatePath) {
    $template = Get-Content -Raw $templatePath
    foreach ($prop in @("TenantId", "TemplateCode", "Version", "EffectiveFromUtc", "OutputFormat", "DataBindingSchemaJson", "QrVerificationEnabled", "SignatureRequired")) {
        if ($template -notmatch $prop) { $failures.Add("OfficialDocumentTemplate missing property $prop") }
    }
    if ($template -notmatch "CorrectionCreatesSuperseding") { $failures.Add("CorrectionCreatesSuperseding required") }
    if ($template -notmatch "AccessibilityRequired") { $failures.Add("AccessibilityRequired required") }
    if ($template -notmatch "OfficialDocumentFormat") { $failures.Add("OfficialDocumentFormat enum required") }
    if ($template -notmatch "TemplateStatus") { $failures.Add("TemplateStatus enum required") }
}

if ($failures.Count -gt 0) {
    Write-Host "DOC-001 validation FAILED:"
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}

Write-Host "DOC-001 validation OK."
exit 0
