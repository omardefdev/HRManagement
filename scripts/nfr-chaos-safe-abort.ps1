# NFR-002: safe abort under dependency degradation (checklist + optional API calls).
param(
    [string]$BaseUrl = $(if ($env:BASE_URL) { $env:BASE_URL } else { 'http://localhost:5000' }),
    [string]$JobId = $env:PAYROLL_JOB_ID,
    [string]$ResultsRoot = 'chaos/results'
)

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$runDir = Join-Path $ResultsRoot "$stamp-safe-abort"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$notes = @"
# Safe abort drill (PAY-003 under chaos)

1. Inject latency/down on pg or rabbit: nfr-chaos-inject.ps1
2. Pause job: POST $BaseUrl/api/payroll/jobs/{jobId}/pause
3. Cancel or Resume after restore: .../cancel or .../resume
4. Assert run state consistent; no duplicate salary lines

**AC-22 (PLAN-06):** full kill/resume proof → scripts/acceptance-payroll-resume-chaos.ps1 (RUN_CHAOS=true)

JobId=$JobId
ACCESS_TOKEN required for live calls.
"@
$notes | Set-Content (Join-Path $runDir 'notes.md') -Encoding UTF8

$status = 'checklist'
if ($JobId -and $env:ACCESS_TOKEN) {
    try {
        $headers = @{ Authorization = "Bearer $($env:ACCESS_TOKEN)" }
        Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/payroll/jobs/$JobId/pause" -Headers $headers | Out-Null
        $status = 'pause-called'
    }
    catch {
        $status = "pause-failed:$($_.Exception.Message)"
    }
}

@{ status = $status; jobId = $JobId; baseUrl = $BaseUrl } |
    ConvertTo-Json | Set-Content (Join-Path $runDir 'summary.json') -Encoding UTF8
Write-Host "Safe abort → $runDir ($status)"
