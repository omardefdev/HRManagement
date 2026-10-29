<#
.SYNOPSIS
  Shows how long each acceptance plan took to execute, from the git history.

.DESCRIPTION
  A commit belongs to plan NN when its message is "type(scopeNNN): ...", for example
  feat(hcm025), fix(qa034) or test(wf025). Merges are matched by the branch name
  "...plan-NN-...".

  Two durations are reported for each plan:
  - Span: from the plan's first commit to its merge (or to its last commit if it was not merged).
    This measure is available for every past plan, so plans can be compared like for like.
  - WallClock: from the "Started (UTC):" line in reports/planNN-progress.md to the merge.
    This includes the baseline work done before the first commit. Plans from PLAN-34 on record that line.

  Times are shown in the chosen time zone (Riyadh by default). Works in Windows PowerShell 5.1 and PowerShell 7.

.EXAMPLE
  pwsh scripts/plan-timing.ps1
.EXAMPLE
  pwsh scripts/plan-timing.ps1 -From 25 -To 34 -Csv artifacts/plan-timing.csv
#>
param(
    [int]$From = 25,
    [int]$To = 99,
    [string]$Since = '2026-10-20',
    [string]$TimeZone = 'Arab Standard Time',
    [string]$Csv
)

$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture

try { $tz = [TimeZoneInfo]::FindSystemTimeZoneById($TimeZone) }
catch { $tz = [TimeZoneInfo]::FindSystemTimeZoneById('Asia/Riyadh') }

function ToLocalText([datetime]$utc) {
    $local = [TimeZoneInfo]::ConvertTimeFromUtc([datetime]::SpecifyKind($utc, [DateTimeKind]::Utc), $tz)
    return $local.ToString('yyyy-MM-dd HH:mm', $inv)
}

function FormatSpan([timespan]$span) {
    $hours = [int][math]::Floor($span.TotalHours)
    return ('{0}:{1:00}' -f $hours, $span.Minutes)
}

$root = (git rev-parse --show-toplevel).Trim()
$log = git -C $root log --all --since=$Since --date=iso-strict --pretty='%ad|%h|%s'
$plans = @{}

foreach ($line in $log) {
    $parts = $line.Split('|', 3)
    if ($parts.Count -lt 3) { continue }

    $when = [DateTimeOffset]::Parse($parts[0], $inv).UtcDateTime
    $subject = $parts[2]
    $plan = $null
    $isMerge = $false

    if ($subject -match "^Merge branch '[^']*plan-(\d+)") { $plan = [int]$Matches[1]; $isMerge = $true }
    elseif ($subject -match '^\w+\([a-z]+0(\d\d)\)') { $plan = [int]$Matches[1] }

    if ($null -eq $plan -or $plan -lt $From -or $plan -gt $To) { continue }
    if (-not $plans.ContainsKey($plan)) {
        $plans[$plan] = @{ First = $null; Last = $null; Merge = $null; Commits = 0 }
    }
    $p = $plans[$plan]

    if ($isMerge) {
        if ($null -eq $p['Merge'] -or $when -gt $p['Merge']) { $p['Merge'] = $when }
    } else {
        $p['Commits'] = $p['Commits'] + 1
        if ($null -eq $p['First'] -or $when -lt $p['First']) { $p['First'] = $when }
        if ($null -eq $p['Last'] -or $when -gt $p['Last']) { $p['Last'] = $when }
    }
}

$rows = @()
foreach ($n in ($plans.Keys | Sort-Object)) {
    $p = $plans[$n]
    if ($null -eq $p['First']) { continue }

    $end = $p['Last']
    $merged = $false
    if ($null -ne $p['Merge']) { $end = $p['Merge']; $merged = $true }

    $startedText = ''
    $wallClock = ''
    $progress = Join-Path $root ('reports/plan{0}-progress.md' -f $n)
    if (Test-Path $progress) {
        $m = Select-String -Path $progress -Pattern 'Started \(UTC\):\s*(\S+)' | Select-Object -First 1
        if ($m) {
            $started = [DateTimeOffset]::Parse($m.Matches[0].Groups[1].Value, $inv).UtcDateTime
            $startedText = ToLocalText $started
            $wallClock = FormatSpan ($end - $started)
        }
    }

    $rows += [pscustomobject]@{
        Plan        = ('PLAN-{0}' -f $n)
        Commits     = $p['Commits']
        Started     = $startedText
        FirstCommit = (ToLocalText $p['First'])
        End         = (ToLocalText $end)
        Merged      = $merged
        Span        = (FormatSpan ($end - $p['First']))
        WallClock   = $wallClock
    }
}

$rows | Format-Table -AutoSize

if ($Csv) {
    $dir = Split-Path $Csv -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    $rows | Export-Csv -Path $Csv -NoTypeInformation -Encoding UTF8
    Write-Host "Written: $Csv"
}

Write-Host 'Span = first commit -> merge (or last commit if not merged). WallClock = "Started (UTC)" in the progress file -> merge.'
Write-Host 'Note: without a merge commit, a later commit with the same plan scope (for example a live-run fix) extends that plan''s End.'
