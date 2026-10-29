# HCM-032 (PLAN-32, P5): recruitment analytics scale run on PostgreSQL (Testcontainers) — 50,000 applications,
# 250,000 events, 2,000 requisitions, 20,000 offers, 5,000 workflow instances. The Index set of metric calls must stay
# under 3 s (unrestricted, 12 months) and 1.5 s (one branch). Timings and EXPLAIN plans: artifacts/hcm032-perf/.
# Needs Docker. Usage: powershell -File perf/hcm032-recruitment-analytics-scale.ps1 [-NoBuild]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "artifacts/hcm032-perf"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$env:HCM032_PERF = "1"
$env:HCM032_PERF_OUT = $outDir
try {
    $project = Join-Path $root "src/Tests/HRManagement.IntegrationTests/HRManagement.IntegrationTests.csproj"
    $testArgs = @("test", $project, "--filter", "Category=Hcm032Scale", "--logger", "console;verbosity=normal")
    if ($NoBuild) { $testArgs += "--no-build" }
    & dotnet @testArgs
    $exit = $LASTEXITCODE
}
finally {
    Remove-Item Env:HCM032_PERF -ErrorAction SilentlyContinue
    Remove-Item Env:HCM032_PERF_OUT -ErrorAction SilentlyContinue
}

$latest = Join-Path $outDir "latest.json"
if (Test-Path $latest) {
    $t = Get-Content $latest -Raw | ConvertFrom-Json
    Write-Host ("HCM-032 scale: unrestricted {0:0} ms (budget {1} ms), branch {2:0} ms (budget {3} ms) -> {4}" -f `
        $t.unrestrictedMs, $t.budgetUnrestrictedMs, $t.branchMs, $t.budgetBranchMs, $(if ($t.passed) { "PASS" } else { "FAIL" }))
}
else {
    Write-Host "HCM-032 scale: no timings written (Docker unavailable or the test was skipped)."
}
exit $exit
