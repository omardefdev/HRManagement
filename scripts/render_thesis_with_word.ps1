param(
    [string]$InputPath = 'C:\univrsity\HRManagement\docs\output\HRManagement_Graduation_Thesis_Final.docx',
    [string]$OutputPath = 'C:\univrsity\HRManagement\docs\output\HRManagement_Graduation_Thesis_Final.pdf'
)

$ErrorActionPreference = 'Stop'

$root = 'C:\univrsity\HRManagement'
$outputDir = Split-Path -Parent $OutputPath
$pdfPath = $OutputPath

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$word = $null
$document = $null

try {
    $word = New-Object -ComObject Word.Application
    $word.Visible = $false
    $word.DisplayAlerts = 0
    $document = $word.Documents.Open($inputPath, $false, $true)
    $document.ExportAsFixedFormat($pdfPath, 17)
}
finally {
    if ($document -ne $null) {
        $document.Close($false)
    }
    if ($word -ne $null) {
        $word.Quit()
    }
}

Get-Item $pdfPath | Select-Object FullName, Length
