# REL-002: compare 3 synthetic parallel payroll cycles.
# Exit 1 if cycle count != 3, files missing, or unexplained field diffs.
# Usage:
#   powershell -File scripts/rel002-payroll-parallel-compare.ps1
#   pwsh -File scripts/rel002-payroll-parallel-compare.ps1

param(
    [string]$CyclesPath = "testdata/rel002/payroll-parallel/cycles.json",
    [string]$RepoRoot = "",
    [string]$OutDir = "testdata/rel002/payroll-parallel/out"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

$cyclesFull = Join-Path $RepoRoot $CyclesPath
if (-not (Test-Path $cyclesFull)) {
    Write-Error "cycles.json not found: $cyclesFull"
}

$baseDir = Split-Path -Parent $cyclesFull
$manifest = Get-Content -Raw -Path $cyclesFull | ConvertFrom-Json
$failures = @()
$report = @()

if ($manifest.cycles.Count -ne 3) {
    $failures += "cycleCount=$($manifest.cycles.Count) expected 3"
}

if ($manifest.policy.forbidLiveAgencyPayroll -ne $true) {
    $failures += "forbidLiveAgencyPayroll must be true"
}

function Get-LineMap($doc) {
    $map = @{}
    foreach ($line in $doc.lines) {
        $map[$line.employeeKey] = $line
    }
    return $map
}

function Find-Explained($diffs, $employeeKey, $field) {
    foreach ($d in $diffs) {
        if ($d.employeeKey -eq $employeeKey -and $d.field -eq $field -and $d.reasonCode) {
            return $true
        }
    }
    return $false
}

foreach ($c in $manifest.cycles) {
    $srcPath = Join-Path $baseDir $c.sourcePath
    $shPath = Join-Path $baseDir $c.shadowPath
    $exPath = Join-Path $baseDir $c.explainedDiffsPath
    $ok = $true
    $detail = "ok"
    $unexplained = @()

    if (-not (Test-Path $srcPath)) { $ok = $false; $detail = "missing source $($c.sourcePath)" }
    elseif (-not (Test-Path $shPath)) { $ok = $false; $detail = "missing shadow $($c.shadowPath)" }
    elseif (-not (Test-Path $exPath)) { $ok = $false; $detail = "missing explained $($c.explainedDiffsPath)" }
    else {
        $src = Get-Content -Raw -Path $srcPath | ConvertFrom-Json
        $sh = Get-Content -Raw -Path $shPath | ConvertFrom-Json
        $ex = Get-Content -Raw -Path $exPath | ConvertFrom-Json
        $srcMap = Get-LineMap $src
        $shMap = Get-LineMap $sh
        $keys = @($srcMap.Keys + $shMap.Keys | Select-Object -Unique)
        foreach ($key in $keys) {
            if (-not $srcMap.ContainsKey($key) -or -not $shMap.ContainsKey($key)) {
                if (-not (Find-Explained $ex.diffs $key "presence")) {
                    $unexplained += "$key:presence"
                }
                continue
            }
            $s = $srcMap[$key]
            $h = $shMap[$key]
            foreach ($field in @("netPay", "grossPay")) {
                $sv = [decimal]$s.$field
                $hv = [decimal]$h.$field
                if ($sv -ne $hv) {
                    if (-not (Find-Explained $ex.diffs $key $field)) {
                        $unexplained += "${key}:${field}"
                    }
                }
            }
        }
        if ($unexplained.Count -gt 0) {
            $ok = $false
            $detail = "unexplained diffs: " + ($unexplained -join ", ")
        }
    }

    $entry = [ordered]@{
        id     = $c.id
        period = $c.period
        ok     = $ok
        detail = $detail
    }
    $report += [pscustomobject]$entry
    if (-not $ok) { $failures += "$($c.id): $detail" }
}

$outFull = Join-Path $RepoRoot $OutDir
New-Item -ItemType Directory -Force -Path $outFull | Out-Null
$reportPath = Join-Path $outFull "compare-report.json"
$report | ConvertTo-Json -Depth 5 | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: 3-cycle parallel payroll compare"
exit 0
