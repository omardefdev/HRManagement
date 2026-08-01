# REL-001: compare staging↔prod against docs/rel-staging/parity-matrix.json
# Exit 1 if any comparison has status not in (match, approved) OR required files missing.
# Usage:
#   pwsh scripts/rel001-parity-diff.ps1
#   pwsh scripts/rel001-parity-diff.ps1 -MatrixPath docs/rel-staging/parity-matrix.json

param(
    [string]$MatrixPath = "docs/rel-staging/parity-matrix.json",
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}
$matrixFull = Join-Path $RepoRoot $MatrixPath
if (-not (Test-Path $matrixFull)) {
    Write-Error "Parity matrix not found: $matrixFull"
}

$matrix = Get-Content -Raw -Path $matrixFull | ConvertFrom-Json
$failures = @()
$report = @()

function Test-StagingHasTls([string]$path) {
    $text = Get-Content -Raw -Path $path
    return ($text -match '(?m)^\s*tls\s*:' ) -and ($text -match 'secretName:\s*hr-gateway-tls')
}

foreach ($c in $matrix.comparisons) {
    $stg = Join-Path $RepoRoot $c.stagingPath
    $prd = Join-Path $RepoRoot $c.productionPath
    $ok = $true
    $detail = $c.note

    switch ($c.check) {
        "stagingHasTlsBlock" {
            if (-not (Test-Path $stg)) { $ok = $false; $detail = "missing staging ingress" }
            elseif (-not (Test-StagingHasTls $stg)) { $ok = $false; $detail = "staging ingress missing tls block" }
            elseif (-not (Test-Path $prd)) { $ok = $false; $detail = "missing production ingress" }
        }
        "hostDiffersByDesign" {
            if (-not (Test-Path $stg) -or -not (Test-Path $prd)) { $ok = $false; $detail = "missing ingress file" }
        }
        "stagingLowerReplicas" {
            if (-not (Test-Path $stg) -or -not (Test-Path $prd)) { $ok = $false; $detail = "missing replicas patch" }
        }
        "fileExistsBoth" {
            if (-not (Test-Path $stg)) { $ok = $false; $detail = "missing staging: $($c.stagingPath)" }
            if (-not (Test-Path $prd)) { $ok = $false; $detail = "missing production: $($c.productionPath)" }
        }
        "fileExistsStaging" {
            if (-not (Test-Path $stg)) { $ok = $false; $detail = "missing staging artifact: $($c.stagingPath)" }
        }
        "composeIsLabOnly" {
            if (-not (Test-Path $stg)) { $ok = $false; $detail = "missing compose lab root" }
        }
        "obsStackDocumented" {
            if (-not (Test-Path $stg) -and -not (Test-Path $prd)) {
                $ok = $false
                $detail = "observability paths missing"
            }
        }
        default {
            $ok = $false
            $detail = "unknown check: $($c.check)"
        }
    }

    $statusOk = $c.status -in @("match", "approved")
    if (-not $statusOk) {
        $ok = $false
        $detail = "status=$($c.status) not allowed (must be match|approved). $detail"
    }

    $entry = [ordered]@{
        id     = $c.id
        status = $c.status
        ok     = $ok
        detail = $detail
    }
    $report += $entry
    if (-not $ok) { $failures += "$($c.id): $detail" }
    $mark = if ($ok) { "OK" } else { "FAIL" }
    Write-Host ("{0}  {1} [{2}] - {3}" -f $mark, $c.id, $c.status, $detail)
}

$outDir = Join-Path $RepoRoot "docs/rel-staging"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$reportPath = Join-Path $outDir "parity-diff-last.json"
@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    matrixVersion  = $matrix.version
    results        = $report
    failed         = $failures.Count
} | ConvertTo-Json -Depth 6 | Set-Content -Path $reportPath -Encoding UTF8

if ($failures.Count -gt 0) {
    Write-Error ("REL-001 parity-diff failed:`n" + ($failures -join "`n"))
}
Write-Host ("REL-001 parity-diff passed ({0} checks)." -f $report.Count)
