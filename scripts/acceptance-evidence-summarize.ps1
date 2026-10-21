# Rebuild acceptance-evidence/summary.md from AC-XX/result.json folders.
param(
    [string]$EvidenceDir = $(if ($env:ACCEPTANCE_EVIDENCE_DIR) { $env:ACCEPTANCE_EVIDENCE_DIR } else { "acceptance-evidence" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$evidencePath = if ([System.IO.Path]::IsPathRooted($EvidenceDir)) { $EvidenceDir } else { Join-Path $root $EvidenceDir }
New-Item -ItemType Directory -Force -Path $evidencePath | Out-Null

$required = @(
    "metadata.json", "steps.md", "before.json", "after.json",
    "audit.json", "db-checks.json", "timing.json", "result.json"
)

$entries = @()
Get-ChildItem -Path $evidencePath -Directory -Filter "AC-*" | Sort-Object Name | ForEach-Object {
    $scenarioDirectory = $_
    $missing = @($required | Where-Object {
        $requiredFile = $_
        -not (Test-Path (Join-Path $scenarioDirectory.FullName $requiredFile))
    })
    $status = "NoResult"
    $reason = $null
    $resultPath = Join-Path $scenarioDirectory.FullName "result.json"
    if (Test-Path $resultPath) {
        $result = Get-Content $resultPath -Raw | ConvertFrom-Json
        $status = [string]$result.status
        $reason = [string]$result.reason
    }
    $entries += [pscustomobject]@{
        AcCode = $scenarioDirectory.Name
        Status = $status
        Reason = $reason
        ContractComplete = ($missing.Count -eq 0)
        Missing = ($missing -join ", ")
    }
}

$expected = 1..37 | ForEach-Object { "AC-{0:D2}" -f $_ }
$found = $entries | ForEach-Object { $_.AcCode }
$notRun = @($expected | Where-Object { $_ -notin $found })

$pass = @($entries | Where-Object { $_.Status -eq "Pass" }).Count
$fail = @($entries | Where-Object { $_.Status -eq "Fail" }).Count
$deferred = @($entries | Where-Object { $_.Status -in @("Deferred", "Blocked") }).Count

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add("# Acceptance evidence summary (AC-01..AC-37)")
[void]$lines.Add("")
[void]$lines.Add("Generated: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss')) UTC")
[void]$lines.Add("")
[void]$lines.Add("| AC | Status | Contract | Missing |")
[void]$lines.Add("|----|--------|----------|---------|")

foreach ($e in $entries) {
    $contract = if ($e.ContractComplete) { "complete" } else { "incomplete" }
    $missingCol = if ([string]::IsNullOrWhiteSpace($e.Missing)) { "-" } else { $e.Missing }
    $statusCol = [string]$e.Status
    if (-not [string]::IsNullOrWhiteSpace($e.Reason)) {
        $statusCol = $statusCol + " (" + $e.Reason + ")"
    }
    $row = "| {0} | {1} | {2} | {3} |" -f $e.AcCode, $statusCol, $contract, $missingCol
    [void]$lines.Add($row)
}

if ($notRun.Count -gt 0) {
    [void]$lines.Add("")
    [void]$lines.Add("## Not yet executed")
    foreach ($ac in $notRun) { [void]$lines.Add("- $ac") }
}

[void]$lines.Add("")
[void]$lines.Add("**Totals:** $pass Pass, $deferred Deferred/Blocked, $fail Fail, $($notRun.Count) not run.")

$summaryPath = Join-Path $evidencePath "summary.md"
$lines -join [Environment]::NewLine | Set-Content -Path $summaryPath -Encoding UTF8
Write-Host "Evidence summary written to: $summaryPath"
