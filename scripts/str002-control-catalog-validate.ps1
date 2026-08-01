# STR-002: validate control-catalog.json domains, fields, LEGAL_TBD, confidence D rules.
# Usage:
#   powershell -File scripts/str002-control-catalog-validate.ps1

param(
    [string]$CatalogPath = "docs/str-legal/control-catalog.json",
    [string]$RequiredPath = "testdata/str002/required-domains.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/str002/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$catalogFull = Join-Path $RepoRoot $CatalogPath
$reqFull = Join-Path $RepoRoot $RequiredPath
if (-not (Test-Path $catalogFull)) { Write-Error "catalog not found: $catalogFull" }
if (-not (Test-Path $reqFull)) { Write-Error "required not found: $reqFull" }

$cat = Get-Content -Raw -Path $catalogFull | ConvertFrom-Json
$req = Get-Content -Raw -Path $reqFull | ConvertFrom-Json
$failures = @()

if ($cat.card -ne "STR-002") { $failures += "card must be STR-002" }

$fields = @($cat.requiredControlFields)
$domainsSeen = @{}
$exceptionIds = @{}
foreach ($ex in $cat.exceptions) { $exceptionIds[$ex.exceptionId] = $true }

foreach ($c in $cat.controls) {
    foreach ($f in $fields) {
        $val = $c.$f
        if ($null -eq $val) { $failures += "$($c.requirementId) missing $f"; continue }
        if ($f -eq "cardIds") {
            if (@($val).Count -lt 1) { $failures += "$($c.requirementId) empty cardIds" }
        }
        elseif ($val -is [string] -and [string]::IsNullOrWhiteSpace($val)) {
            $failures += "$($c.requirementId) empty $f"
        }
    }
    $domainsSeen[$c.domain] = $true
    if ($cat.allowedDomains -notcontains $c.domain) {
        $failures += "$($c.requirementId) unknown domain $($c.domain)"
    }
    if ($cat.allowedConfidence -notcontains $c.confidence) {
        $failures += "$($c.requirementId) bad confidence $($c.confidence)"
    }
    if ($cat.allowedStatuses -notcontains $c.status) {
        $failures += "$($c.requirementId) bad status $($c.status)"
    }
    if ($c.status -eq "Implemented") {
        $blob = ("{0} {1}" -f $c.evidencePath, $c.testRef)
        if ($blob -match "(?i)LEGAL_TBD") {
            $failures += "$($c.requirementId) Implemented must not reference LEGAL_TBD"
        }
    }
    if ($c.confidence -eq "D") {
        $okD = ($c.status -eq "Blocked-Legal") -or ($c.exceptionId -and $exceptionIds.ContainsKey([string]$c.exceptionId))
        if (-not $okD) {
            $failures += "$($c.requirementId) confidence D requires Blocked-Legal or exceptionId"
        }
    }
    if ($c.exceptionId -and -not $exceptionIds.ContainsKey([string]$c.exceptionId)) {
        $failures += "$($c.requirementId) unknown exceptionId $($c.exceptionId)"
    }
}

foreach ($d in $req.requiredDomains) {
    if (-not $domainsSeen.ContainsKey($d)) { $failures += "missing domain $d" }
}

$tracePath = Join-Path $RepoRoot $req.traceDocRelative
if (-not (Test-Path $tracePath)) {
    $failures += "trace doc missing: $($req.traceDocRelative)"
}
else {
    $traceText = Get-Content -Raw -Path $tracePath
    $matches = [regex]::Matches($traceText, "(?m)^## Trace \d+")
    if ($matches.Count -lt [int]$req.minTraceHeadings) {
        $failures += "need >= $($req.minTraceHeadings) Trace headings, found $($matches.Count)"
    }
}

$journeyIds = @($cat.journeys | ForEach-Object { $_.id })
foreach ($need in @("DomainCoverage", "ControlLinkage", "ConflictExceptions", "LegislativeWatch", "TraceWalkthrough")) {
    if ($journeyIds -notcontains $need) { $failures += "missing journey $need" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$report = [ordered]@{
    card = "STR-002"
    ok = ($failures.Count -eq 0)
    controlCount = @($cat.controls).Count
    domainCount = $domainsSeen.Count
    failureCount = $failures.Count
    failures = $failures
}
$reportPath = Join-Path $outFull "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: STR-002 control catalog validate"
exit 0
