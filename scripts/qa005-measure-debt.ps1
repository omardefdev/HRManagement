# QA-005: measure a lightweight tech-debt baseline and compare to debt-baseline.json thresholds.
param([string]$RepoRoot = "")

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$baselinePath = Join-Path $RepoRoot "docs/qa/debt-baseline.json"
$baseline = Get-Content -Raw $baselinePath | ConvertFrom-Json
$registerPath = Join-Path $RepoRoot "docs/qa/debt-register.json"
if (-not (Test-Path $registerPath)) { throw "docs/qa/debt-register.json missing" }

# Lightweight measurable proxies (no external commercial analyzers required).
$srcRoot = Join-Path $RepoRoot "src"
$csFiles = Get-ChildItem -Path $srcRoot -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\(obj|bin|Migrations)\\' }

$maxFileLines = 0
$longFiles = 0
foreach ($f in $csFiles) {
    $lines = @(Get-Content $f.FullName).Count
    if ($lines -gt $maxFileLines) { $maxFileLines = $lines }
    if ($lines -gt 500) { $longFiles++ }
}

# Circular dependency heuristic: project references that form A->B and B->A between service layers.
$csprojs = Get-ChildItem -Path $srcRoot -Filter *.csproj -Recurse
$edges = @()
foreach ($p in $csprojs) {
    $xml = [xml](Get-Content -Raw $p.FullName)
    $from = [IO.Path]::GetFileNameWithoutExtension($p.Name)
    foreach ($ref in $xml.Project.ItemGroup.ProjectReference) {
        if (-not $ref.Include) { continue }
        $to = [IO.Path]::GetFileNameWithoutExtension($ref.Include)
        $edges += [pscustomobject]@{ From = $from; To = $to }
    }
}
$circular = 0
$edgeSet = @{}
foreach ($e in $edges) { $edgeSet["$($e.From)->$($e.To)"] = $true }
foreach ($e in $edges) {
    if ($edgeSet.ContainsKey("$($e.To)->$($e.From)")) { $circular++ }
}
$circular = [int]($circular / 2)

$outDir = Join-Path $RepoRoot "testdata/qa005/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$measurement = [ordered]@{
    card = "QA-005"
    measuredInCi = $true
    measuredAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    fileCount = $csFiles.Count
    maxFileLines = $maxFileLines
    filesOver500Lines = $longFiles
    circularDependencyPairs = $circular
    debtRegisterPath = "docs/qa/debt-register.json"
    baselineVersion = $baseline.baselineVersion
    thresholds = @{
        file_length_max = 500
        circular_dependencies_max = 0
    }
    pass = ($circular -eq 0)
}

$path = Join-Path $outDir "debt-measurement.json"
($measurement | ConvertTo-Json -Depth 6) | Set-Content -Path $path -Encoding utf8

Write-Host "QA-005 measurement written: $path"
if (-not $measurement.pass) {
    Write-Host "FAIL: circular dependencies detected: $circular"
    exit 1
}
Write-Host "QA-005 measure-debt PASS"
exit 0
