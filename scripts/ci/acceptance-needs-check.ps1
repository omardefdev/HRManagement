#Requires -Version 5.1
# PLAN-66: the Acceptance check is green only when every job it needs succeeded. A failed, cancelled or skipped job is red: a
# stage that did not run proves nothing, and the run could otherwise pass on what the other stages left.
# Reads the needs context of the workflow, as JSON, from NEEDS_JSON (or -Needs). Windows PowerShell 5.1 and PowerShell 7. ASCII only.
param(
    [string]$Needs = $env:NEEDS_JSON,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($Needs)) {
    Write-Host "acceptance-needs-check: FAIL (no needs context)"
    exit 1
}
$jobs = @(($Needs | ConvertFrom-Json).PSObject.Properties)
if ($jobs.Count -eq 0) {
    Write-Host "acceptance-needs-check: FAIL (the needs context is empty)"
    exit 1
}
$bad = @($jobs | Where-Object { [string]$_.Value.result -ne "success" })
if ($bad.Count -gt 0) {
    Write-Host "acceptance-needs-check: FAIL ($($bad.Count))"
    foreach ($job in $bad) { Write-Host "  - $($job.Name): $($job.Value.result)" }
    exit 1
}
Write-Host "acceptance-needs-check: PASS ($($jobs.Count) jobs)"
exit 0
