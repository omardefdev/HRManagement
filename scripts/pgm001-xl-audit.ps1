# PGM-001: fail if any XL card lacks an approved story split in critical-path.json.
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$path = Join-Path $RepoRoot "docs/pgm/critical-path.json"
$doc = Get-Content -Raw $path | ConvertFrom-Json

if ($doc.xlDecomposition.openXlWithoutSplit -ne 0) {
    throw "openXlWithoutSplit must be 0"
}

$splits = @($doc.xlDecomposition.splits)
if ($splits.Count -lt 60) {
    throw "Expected >= 60 XL splits, found $($splits.Count)"
}

$missing = @()
foreach ($s in $splits) {
    if (-not $s.cardId) { $missing += "(blank cardId)" }
    $storyCount = 0
    if ($s.stories) { $storyCount = [int]$s.stories }
    if ($s.storyIds) { $storyCount = [Math]::Max($storyCount, @($s.storyIds).Count) }
    if ($storyCount -lt 3) { $missing += "$($s.cardId) has < 3 stories" }
}

$outDir = Join-Path $RepoRoot "testdata/pgm001/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card = "PGM-001"
    splitCount = $splits.Count
    openXlWithoutSplit = $doc.xlDecomposition.openXlWithoutSplit
    failures = $missing
    ok = ($missing.Count -eq 0)
}
($report | ConvertTo-Json -Depth 5) | Set-Content (Join-Path $outDir "xl-audit-report.json") -Encoding utf8

if ($missing.Count -gt 0) {
    Write-Host "FAIL:"
    $missing | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PGM-001 XL audit PASS ($($splits.Count) cards)"
exit 0
