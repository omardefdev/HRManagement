# QA-002: Risk-scoped coverage gate + per-assembly floors (delegates to check-coverage.py).
param(
    [string]$CoverageFile,
    [double]$CoverageThreshold = -1
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$python = $null
foreach ($cand in @("python", "python3", "py")) {
    if (Get-Command $cand -ErrorAction SilentlyContinue) {
        $python = $cand
        break
    }
}
if (-not $python) {
    Write-Error "python is required for scripts/check-coverage.py"
}

if ($CoverageThreshold -ge 0) {
    $env:COVERAGE_THRESHOLD = "$CoverageThreshold"
}

$argsList = @("$PSScriptRoot\check-coverage.py")
if (-not [string]::IsNullOrWhiteSpace($CoverageFile)) {
    $argsList += $CoverageFile
}

& $python @argsList
exit $LASTEXITCODE
