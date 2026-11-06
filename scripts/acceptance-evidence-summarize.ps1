#Requires -Version 5.1
# PLAN-41: the only generator of acceptance-evidence/summary.md (and summary.json, which the validator reads). The states
# come from scripts/acceptance-evidence-lib.ps1. Runs under Windows PowerShell 5.1 and PowerShell 7. ASCII only.
param(
    [string]$EvidenceDir = $(if ($env:ACCEPTANCE_EVIDENCE_DIR) { $env:ACCEPTANCE_EVIDENCE_DIR } else { "acceptance-evidence" }),
    [string]$RepoRoot = "",
    # The commit whose code tree is the code under test (P-2).
    [string]$Head = "HEAD",
    # A fixed code tree (tests); by default the tree of -Head.
    [string]$CodeTree = "",
    [string]$Ownership = "tests/acceptance/ac-ownership.json",
    [int]$MaxAgeDays = 30
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")

$evidencePath = if ([System.IO.Path]::IsPathRooted($EvidenceDir)) { $EvidenceDir } else { Join-Path $RepoRoot $EvidenceDir }
$ownershipPath = if ([System.IO.Path]::IsPathRooted($Ownership)) { $Ownership } else { Join-Path $RepoRoot $Ownership }
New-Item -ItemType Directory -Force -Path $evidencePath | Out-Null

if ([string]::IsNullOrWhiteSpace($CodeTree)) { $CodeTree = Get-AcceptanceCodeTree -RepoRoot $RepoRoot -Commit $Head }
$owners = Get-AcceptanceOwnership -Path $ownershipPath
$nowUtc = (Get-Date).ToUniversalTime()
$rows = @(Get-AcceptanceRows -EvidenceDir $evidencePath -Ownership $owners -CodeTree $CodeTree -NowUtc $nowUtc -MaxAgeDays $MaxAgeDays)
$totals = Get-AcceptanceTotals -Rows $rows

$runId = $env:GITHUB_RUN_ID
$source = if ([string]::IsNullOrWhiteSpace($runId)) { "local" } else { "ci" }
$generated = Format-AcceptanceDate $nowUtc "yyyy-MM-dd HH:mm:ss"
function Format-Cell([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return "-" }
    return (($Text -replace '\|', '/') -replace '[\r\n]+', ' ')
}

$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add("# Acceptance evidence summary")
[void]$lines.Add("")
[void]$lines.Add("Generated: $generated UTC. Source: $source$(if ($runId) { ", run $runId" }). Code tree: $(Get-AcceptanceShort $CodeTree).")
[void]$lines.Add("")
[void]$lines.Add("| AC | Status | Scenarios (P/F/B) | Contract | Profile | gitSha | Images | Recorded | Source/Run | Freshness | Proves claim | Owner plan |")
[void]$lines.Add("|----|--------|-------------------|----------|---------|--------|--------|----------|------------|-----------|--------------|------------|")
foreach ($row in $rows) {
    $run = if ($row.ciRunId) { "$($row.source)/$($row.ciRunId)" } else { $row.source }
    $proves = if ($row.provesClaim) { "yes" } else { "no" }
    $owner = if ($row.ownerPlan) { $row.ownerPlan } else { "NONE" }
    $freshness = $row.freshness
    if ($row.staleReasons.Count -gt 0) { $freshness = "stale: " + (@($row.staleReasons | Select-Object -First 2) -join "; ") }
    [void]$lines.Add(("| {0} | {1} | {2}/{3}/{4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |" -f `
        $row.ac, $row.status, $row.pass, $row.fail, $row.blocked, $row.contract, $row.profile, (Get-AcceptanceShort $row.sourceSha),
        (Format-Cell $row.images), $row.recorded, $run, (Format-Cell $freshness), $proves, (Format-Cell $owner)))
}
[void]$lines.Add("")
[void]$lines.Add((Format-AcceptanceTotalsLine -Totals $totals))

# PLAN-66 (S3): the classes the rule of the scope left out of this run, with the reason and the last nightly run that ran them.
$notRunByRule = Read-AcceptanceJson -Path (Join-Path $evidencePath "not-run-by-rule.json")
if ($null -ne $notRunByRule) {
    $nightly = [string](Get-AcceptanceProperty $notRunByRule "lastNightly")
    if ([string]::IsNullOrWhiteSpace($nightly)) { $nightly = "none yet" }
    [void]$lines.Add("")
    [void]$lines.Add("## Not run by rule")
    [void]$lines.Add("")
    [void]$lines.Add("$(@($notRunByRule.classes) -join ', '): $($notRunByRule.reason) Last nightly run: $nightly.")
}

$open = @($rows | Where-Object { $_.status -in @("Fail", "Blocked") -and $_.reason })
if ($open.Count -gt 0) {
    [void]$lines.Add("")
    [void]$lines.Add("## Open reasons")
    foreach ($row in $open) { [void]$lines.Add("- $($row.ac) ($($row.status)): $(Format-Cell $row.reason)") }
}

$summaryPath = Join-Path $evidencePath "summary.md"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# PLAN-40 (P-5): keep the scenario log that EvidenceRecorder appends ("- **AC-..** ...: Pass (`id`)") after the totals,
# and write UTF-8 without a BOM (Set-Content -Encoding UTF8 adds one under PowerShell 5.1).
if (Test-Path -LiteralPath $summaryPath) {
    $log = @([System.IO.File]::ReadAllLines($summaryPath, $utf8NoBom) | Where-Object { $_ -match '^- \*\*(AC|HCM)-' })
    if ($log.Count -gt 0) {
        [void]$lines.Add("")
        [void]$lines.Add("## Scenario log")
        foreach ($entry in $log) { [void]$lines.Add($entry) }
    }
}
[System.IO.File]::WriteAllText($summaryPath, (($lines -join "`n") + "`n"), $utf8NoBom)

$json = [ordered]@{
    schema = "acceptance-summary-v1"
    generatedAtUtc = $nowUtc.ToString("o", [System.Globalization.CultureInfo]::InvariantCulture)
    source = $source
    ciRunId = $runId
    ciEvent = $env:GITHUB_EVENT_NAME
    repository = $env:GITHUB_REPOSITORY
    codeTree = $CodeTree
    totals = $totals
    rows = $rows
}
if ($null -ne $notRunByRule) { $json["notRunByRule"] = @($notRunByRule.classes) }
[System.IO.File]::WriteAllText((Join-Path $evidencePath "summary.json"), (($json | ConvertTo-Json -Depth 6) + "`n"), $utf8NoBom)
Write-Host "Evidence summary written to: $summaryPath"
Write-Host ("Totals: {0} proven, {1} passed but not proving, {2} stale, {3} incomplete, {4} not run, {5} fail, {6} blocked." -f `
    $totals.proven, $totals.passedNotProving, $totals.stale, $totals.incomplete, $totals.notRun, $totals.fail, $totals.blocked)
