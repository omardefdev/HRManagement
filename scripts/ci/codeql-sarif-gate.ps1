# PLAN-35 (G-QA-07, D-QA-18): CodeQL SARIF gate. CodeQL runs with upload only when the repository variable
# CODE_SCANNING_UPLOAD is true (code scanning needs GitHub Code Security on a private repository), so the job itself
# decides on the SARIF it wrote. It fails on any result of level "error" or with security-severity >= the threshold
# (7.0 = High) that has no baseline entry, or whose baseline entry is past its reviewBy date or not Open. It also fails
# when no SARIF run is found, so a broken analysis cannot pass as clean.
#
#   pwsh -NoProfile -File scripts/ci/codeql-sarif-gate.ps1 -Sarif sarif-results
#   pwsh -NoProfile -File scripts/ci/codeql-sarif-gate.ps1 -Sarif x.sarif -Baseline b.json -AsOfDate 2026-11-01
#
# Baseline (docs/qa/codeql-baseline.json, registered in docs/qa/dated-obligations.json as QA-CODEQL-BASELINE):
#   entries[]: id, ruleId, path, fingerprint (optional: partialFingerprints.primaryLocationLineHash), owner,
#   reviewBy (yyyy-MM-dd), status (Open | Fixed), reason.
# Exit 0 = pass, 1 = gate failed. Windows PowerShell 5.1 and PowerShell 7. ASCII only.

param(
    [Parameter(Mandatory = $true)][string]$Sarif,
    [string]$Baseline = "docs/qa/codeql-baseline.json",
    [string]$AsOfDate = "",
    [double]$SeverityThreshold = 7.0,
    [string]$RepoRoot = "",
    [string]$OutFile = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path }
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$jsonArgs = @{}
if ((Get-Command ConvertFrom-Json).Parameters.ContainsKey('DateKind')) { $jsonArgs['DateKind'] = 'String' }

function Resolve-RepoPath([string]$p) {
    if ([IO.Path]::IsPathRooted($p)) { return $p }
    return (Join-Path $RepoRoot ($p -replace '/', [IO.Path]::DirectorySeparatorChar))
}

function ConvertTo-Day([string]$text) {
    $d = [datetime]::MinValue
    if ([datetime]::TryParseExact($text, "yyyy-MM-dd", $invariant, [System.Globalization.DateTimeStyles]::None, [ref]$d)) { return $d }
    return $null
}

if ($AsOfDate) { $asOf = ConvertTo-Day $AsOfDate; if ($null -eq $asOf) { throw "AsOfDate must be yyyy-MM-dd" } }
else { $asOf = [datetime]::UtcNow.Date }

$problems = [System.Collections.Generic.List[string]]::new()

# ---------------------------------------------------------------- baseline
$baselinePath = Resolve-RepoPath $Baseline
$entries = @()
if (-not (Test-Path $baselinePath)) { $problems.Add("baseline file not found: $Baseline") }
else {
    $b = Get-Content -Raw $baselinePath | ConvertFrom-Json @jsonArgs
    $entries = @($b.entries | Where-Object { $_ })
    foreach ($e in $entries) {
        foreach ($f in @("id", "ruleId", "path", "owner", "reviewBy", "status", "reason")) {
            if (-not "$($e.$f)") { $problems.Add("baseline entry '$($e.id)' has no $f") }
        }
        if ($null -eq (ConvertTo-Day "$($e.reviewBy)")) { $problems.Add("baseline entry '$($e.id)': reviewBy '$($e.reviewBy)' is not yyyy-MM-dd") }
    }
}

# ---------------------------------------------------------------- SARIF
$sarifPath = Resolve-RepoPath $Sarif
$files = @()
if (Test-Path $sarifPath -PathType Container) { $files = @(Get-ChildItem $sarifPath -Recurse -Filter *.sarif | ForEach-Object { $_.FullName }) }
elseif (Test-Path $sarifPath) { $files = @($sarifPath) }

$runCount = 0
$results = [System.Collections.Generic.List[object]]::new()
foreach ($file in $files) {
    $doc = Get-Content -Raw $file | ConvertFrom-Json @jsonArgs
    foreach ($run in @($doc.runs | Where-Object { $_ })) {
        $runCount++
        # Rules live in tool.driver.rules and, for CodeQL query packs, in tool.extensions[].rules.
        $rules = @{}
        $components = @($run.tool.driver) + @($run.tool.extensions | Where-Object { $_ })
        foreach ($c in $components) {
            foreach ($r in @($c.rules | Where-Object { $_ })) { if (-not $rules.ContainsKey("$($r.id)")) { $rules["$($r.id)"] = $r } }
        }
        foreach ($res in @($run.results | Where-Object { $_ })) {
            $ruleId = "$($res.ruleId)"
            if (-not $ruleId -and $res.rule) { $ruleId = "$($res.rule.id)" }
            $rule = $null
            if ($rules.ContainsKey($ruleId)) { $rule = $rules[$ruleId] }
            $level = "$($res.level)"
            if (-not $level -and $rule -and $rule.defaultConfiguration) { $level = "$($rule.defaultConfiguration.level)" }
            if (-not $level) { $level = "warning" }
            $sev = $null
            foreach ($props in @($res.properties, $(if ($rule) { $rule.properties } else { $null }))) {
                if ($null -ne $props -and $null -ne $props.'security-severity' -and $null -eq $sev) {
                    $sev = [double]::Parse("$($props.'security-severity')", $invariant)
                }
            }
            $loc = @($res.locations)[0]
            $uri = ""; $line = 0
            if ($loc -and $loc.physicalLocation) {
                $uri = "$($loc.physicalLocation.artifactLocation.uri)"
                if ($loc.physicalLocation.region) { $line = [int]$loc.physicalLocation.region.startLine }
            }
            $fp = ""
            if ($res.partialFingerprints) { $fp = "$($res.partialFingerprints.primaryLocationLineHash)" }
            $gated = ($level -eq "error") -or ($null -ne $sev -and $sev -ge $SeverityThreshold)
            $results.Add([pscustomobject]@{ ruleId = $ruleId; level = $level; severity = $sev; path = $uri; line = $line; fingerprint = $fp; gated = $gated; baseline = $null })
        }
    }
}

if ($files.Count -eq 0) { $problems.Add("no SARIF file under $Sarif (the analysis did not write results)") }
elseif ($runCount -eq 0) { $problems.Add("SARIF under $Sarif contains no run") }

# ---------------------------------------------------------------- match
$used = @{}
foreach ($r in $results) {
    if (-not $r.gated) { continue }
    $match = $entries | Where-Object {
        $_.ruleId -eq $r.ruleId -and $_.path -eq $r.path -and (-not "$($_.fingerprint)" -or "$($_.fingerprint)" -eq $r.fingerprint)
    } | Select-Object -First 1
    $where = "$($r.ruleId) at $($r.path):$($r.line) (level $($r.level), security-severity $($r.severity))"
    if (-not $match) { $problems.Add("new finding not in the baseline: $where"); continue }
    $r.baseline = $match.id
    $used["$($match.id)"] = $true
    $due = ConvertTo-Day "$($match.reviewBy)"
    if ("$($match.status)" -ne "Open") { $problems.Add("baseline entry '$($match.id)' is $($match.status) but the finding is still reported: $where") }
    elseif ($null -ne $due -and $due -lt $asOf) { $problems.Add("baseline entry '$($match.id)' expired on $($match.reviewBy) (owner $($match.owner)): $where") }
}

$gatedCount = @($results | Where-Object { $_.gated }).Count
$stale = @($entries | Where-Object { "$($_.status)" -eq "Open" -and -not $used.ContainsKey("$($_.id)") })

Write-Host ("CodeQL SARIF gate: {0} file(s), {1} run(s), {2} result(s), {3} at level error or security-severity >= {4}; baseline {5} entr(y/ies); as of {6:yyyy-MM-dd}" -f `
        $files.Count, $runCount, $results.Count, $gatedCount, $SeverityThreshold.ToString($invariant), $entries.Count, $asOf)
foreach ($s in $stale) { Write-Host "::warning::baseline entry '$($s.id)' ($($s.ruleId) $($s.path)) matched no finding - mark it Fixed" }
foreach ($p in $problems) { Write-Host "::error::$p" }

if ($env:GITHUB_STEP_SUMMARY) {
    $sum = @("### CodeQL SARIF gate (PLAN-35)", "", "- Results: $($results.Count); gated (error or security-severity >= $SeverityThreshold): $gatedCount; baseline entries: $($entries.Count)")
    if ($problems.Count -eq 0) { $sum += "- Verdict: **pass**" } else { $sum += "- Verdict: **fail**"; $sum += ($problems | ForEach-Object { "  - $_" }) }
    Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $sum
}

if ($OutFile) {
    $report = [ordered]@{
        card = "PLAN-35"; asOfDate = $asOf.ToString("yyyy-MM-dd"); ok = ($problems.Count -eq 0)
        files = $files.Count; runs = $runCount; results = $results.Count; gated = $gatedCount
        problems = @($problems); staleBaselineEntries = @($stale | ForEach-Object { $_.id })
    }
    $outPath = Resolve-RepoPath $OutFile
    New-Item -ItemType Directory -Force (Split-Path $outPath -Parent) | Out-Null
    [IO.File]::WriteAllText($outPath, ($report | ConvertTo-Json -Depth 5))
}

if ($problems.Count -gt 0) { Write-Host "CodeQL SARIF gate FAILED"; exit 1 }
Write-Host "CodeQL SARIF gate OK"
exit 0
