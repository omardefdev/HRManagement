# HCM-031 (PLAN-31, P7): competition scale run — 10,000 applicants, 4 lines, 20 sessions on PostgreSQL (Testcontainers).
# Seating + results + allocation must stay under 60 s. Timings: artifacts/hcm031-perf/latest.json (+ a timestamped copy).
# Needs Docker. Usage: pwsh perf/hcm031-competition-scale.ps1 [-NoBuild]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "artifacts/hcm031-perf"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$env:HCM031_PERF = "1"
$env:HCM031_PERF_OUT = $outDir
try {
    $project = Join-Path $root "src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj"
    $testArgs = @("test", $project, "--filter", "Category=Hcm031Perf", "--logger", "console;verbosity=normal")
    if ($NoBuild) { $testArgs += "--no-build" }
    & dotnet @testArgs
    $exit = $LASTEXITCODE
}
finally {
    Remove-Item Env:HCM031_PERF -ErrorAction SilentlyContinue
    Remove-Item Env:HCM031_PERF_OUT -ErrorAction SilentlyContinue
}

$latest = Join-Path $outDir "latest.json"
if (Test-Path $latest) {
    $t = Get-Content $latest -Raw | ConvertFrom-Json
    Write-Host ("HCM-031 scale: seating {0} ms, results {1} ms, allocation {2} ms, total {3} ms (budget {4} ms) -> {5}" -f `
        $t.seatingMs, $t.resultsMs, $t.allocationMs, $t.totalMs, $t.budgetMs, $(if ($t.passed) { "PASS" } else { "FAIL" }))
}
else {
    Write-Host "HCM-031 scale: no timings written (Docker unavailable or the test was skipped)."
}
exit $exit
