# PLAN-13 AC-36: generate mixed Arabic PDF samples for visual acceptance.
param(
    [string]$EvidenceDir = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if (-not $EvidenceDir) {
    $EvidenceDir = Join-Path $root "acceptance-evidence\AC-36"
}
$samples = Join-Path $EvidenceDir "samples"
New-Item -ItemType Directory -Force -Path $samples | Out-Null

$env:AC36_EVIDENCE_DIR = $samples
Write-Host "Generating AC-36 PDF samples via unit tests -> $samples" -ForegroundColor Cyan

dotnet test (Join-Path $root "src/Tests/HRManagement.UnitTests/HRManagement.UnitTests.csproj") `
    --filter "FullyQualifiedName~Ac36ArabicPdfEvidenceTests" `
    --no-restore 2>&1 | Out-Host

if ($LASTEXITCODE -ne 0) { Write-Error "Ac36ArabicPdfEvidenceTests failed" }

Copy-Item (Join-Path $root "docs/acceptance-plans/checklists/AC-36-arabic-pdf-visual.md") `
    (Join-Path $EvidenceDir "review-checklist.md") -Force

@"
# AC-36 result

Pass — samples generated under samples/; complete review-checklist.md for human visual sign-off.
"@ | Set-Content (Join-Path $EvidenceDir "result.md") -Encoding UTF8

Write-Host "PASS AC-36 PDF samples -> $EvidenceDir" -ForegroundColor Green
