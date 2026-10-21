# UX-003: fail if native confirm() remains in views or shared-ui
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $root 'src'))) {
    $root = Split-Path -Parent $PSScriptRoot
}

$patterns = @(
    @{ Path = Join-Path $root 'src'; Filter = '*.cshtml'; Regex = '(?:window\.confirm|return\s+confirm|onclick\s*=\s*"return\s+confirm)\s*\(' }
)

$violations = @()
foreach ($p in $patterns) {
    if (-not (Test-Path $p.Path)) { continue }
    Get-ChildItem -Path $p.Path -Filter $p.Filter -Recurse -File | ForEach-Object {
        $matches = Select-String -Path $_.FullName -Pattern $p.Regex -AllMatches
        if ($matches) {
            foreach ($m in $matches) {
                $violations += "$($_.FullName):$($m.LineNumber): $($m.Line.Trim())"
            }
        }
    }
}

if ($violations.Count -gt 0) {
    Write-Host "UX-003: native confirm() found (use data-hr-confirm / hrSharedUi.confirm):" -ForegroundColor Red
    $violations | ForEach-Object { Write-Host "  $_" }
    exit 1
}

Write-Host "UX-003: no native confirm() in views or shared-ui.js" -ForegroundColor Green
exit 0
