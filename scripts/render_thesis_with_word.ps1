param(
    [string]$InputPath = 'C:\univrsity\HRManagement\docs\output\HRManagement_Graduation_Thesis_2026_Current.docx',
    [string]$OutputPath = 'C:\univrsity\HRManagement\docs\output\HRManagement_Graduation_Thesis_2026_Current.pdf'
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
    $document = $word.Documents.Open($inputPath, $false, $false)
    $document.Fields.Update() | Out-Null
    foreach ($toc in $document.TablesOfContents) {
        $toc.Update() | Out-Null

        # Word builds TOC styles as LTR on English installations. Persist RTL in
        # both the styles and the generated paragraphs so page numbers stay on
        # the left after the document is reopened or the TOC is refreshed.
        foreach ($styleName in @('TOC 1', 'TOC 2', 'TOC 3')) {
            $style = $document.Styles.Item($styleName)
            $style.ParagraphFormat.ReadingOrder = 0
            $style.ParagraphFormat.Alignment = 2
        }
        foreach ($paragraph in $toc.Range.Paragraphs) {
            $paragraph.Format.ReadingOrder = 0
            $paragraph.Format.Alignment = 2
        }
    }
    $document.Repaginate()
    $document.Save()
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
