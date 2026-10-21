# PDF Arabic/RTL: ensure QuestPDF generators use shared Arabic font + RTL helpers.
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

Need "docs/PDF_ARABIC_RTL.md" | Out-Null
Need "src/Shared/HRManagement.Documents.QuestPdf/HrQuestPdfBootstrap.cs" | Out-Null
Need "src/Shared/HRManagement.Documents.QuestPdf/HrArabicDocumentExtensions.cs" | Out-Null
Need "src/Shared/HRManagement.Documents.QuestPdf/HrOfficialDocumentLayout.cs" | Out-Null
Need "src/Shared/HRManagement.Documents.QuestPdf/fonts/NotoSansArabic-Regular.ttf" | Out-Null
Need "src/Shared/HRManagement.Documents.QuestPdf/fonts/NotoSansArabic-Bold.ttf" | Out-Null
Need "src/Tests/HRManagement.UnitTests/ArabicPdfGenerationTests.cs" | Out-Null

foreach ($program in @(
    "src/Services/AdminLogistics/Admin.Logistics.Web/Program.cs",
    "src/Services/HRCore/HR.Core.Web/Program.cs",
    "src/Services/Payroll/Payroll.Web/Program.cs"
)) {
    $path = Need $program
    if (Test-Path $path) {
        $text = Get-Content -Raw $path
        if ($text -notmatch "AddHrQuestPdfDocuments") {
            $failures.Add("$program missing AddHrQuestPdfDocuments()")
        }
    }
}

$srcRoot = Join-Path $RepoRoot "src"
$pdfPattern = 'Document\.Create|\.GeneratePdf\s*\('
$compliantPattern = 'ConfigureArabicPage|ConfigureOfficialPage|HrQuestPdfBootstrap|HrPayslipPdfGenerator|AssetHandoverPdfService'
$questPdfShared = "HRManagement.Documents.QuestPdf"

Get-ChildItem -Path $srcRoot -Recurse -Filter "*.cs" |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    ForEach-Object {
        $text = Get-Content -Raw $_.FullName
        if ($text -match $pdfPattern) {
            $rel = $_.FullName.Substring($RepoRoot.Length).TrimStart('\', '/')
            if ($rel -match [regex]::Escape($questPdfShared)) {
                return
            }
            if ($text -notmatch $compliantPattern) {
                $failures.Add("$rel uses QuestPDF without Arabic layout helpers")
            }
        }
    }

if ($failures.Count -gt 0) {
    Write-Host "PDF Arabic validation failed:" -ForegroundColor Red
    foreach ($f in $failures) { Write-Host "  - $f" -ForegroundColor Red }
    exit 1
}

Write-Host "PDF Arabic validation passed." -ForegroundColor Green
exit 0
