#Requires -Version 5.1
# PLAN-41 (G-QA-08): checks an acceptance summary against the rules. Exit 1 lists every failure.
#   default     the run's own summary, inside the Acceptance job: every AC has an owner, nothing is counted as proven against
#               the rules, no Pass with an incomplete contract, and summary.json agrees with the evidence folders.
#   -Committed  the committed summary (P-4): also generated in CI, by a run of this repository, and fresh against -Head.
param(
    [string]$RepoRoot = "",
    [string]$EvidenceDir = "acceptance-evidence",
    [string]$Ownership = "tests/acceptance/ac-ownership.json",
    # PLAN-66 (S3): the scope whose rule may leave classes out of a run.
    [string]$Scope = "tests/acceptance/ci-scope.json",
    [string]$Head = "HEAD",
    # A fixed code tree (tests); by default the tree of -Head.
    [string]$CodeTree = "",
    [int]$MaxAgeDays = 30,
    [switch]$Committed,
    # -Committed: the repository the run must belong to. The default comes from GITHUB_REPOSITORY, then the origin remote.
    [string]$Repository = "",
    # Tests only: skip the GitHub call that proves the run exists (the fixtures carry a made-up run id).
    [switch]$SkipRunLookup
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
. (Join-Path (Join-Path (Join-Path $RepoRoot "scripts") "ci") "acceptance-stage-lib.ps1")

$evidencePath = if ([System.IO.Path]::IsPathRooted($EvidenceDir)) { $EvidenceDir } else { Join-Path $RepoRoot $EvidenceDir }
$ownershipPath = if ([System.IO.Path]::IsPathRooted($Ownership)) { $Ownership } else { Join-Path $RepoRoot $Ownership }
if ([string]::IsNullOrWhiteSpace($CodeTree)) { $CodeTree = Get-AcceptanceCodeTree -RepoRoot $RepoRoot -Commit $Head }

$failures = New-Object System.Collections.Generic.List[string]
$owners = Get-AcceptanceOwnership -Path $ownershipPath
$nowUtc = (Get-Date).ToUniversalTime()

# Ownership: every entry is well formed, and AC-01..37 are all there.
foreach ($name in @($owners.Keys | Sort-Object)) {
    $entry = $owners[$name]
    if ([string]::IsNullOrWhiteSpace([string](Get-AcceptanceProperty $entry "ownerPlan"))) { [void]$failures.Add("$name has no owner plan.") }
    if ((Get-AcceptanceProperty $entry "provesClaim") -isnot [bool]) { [void]$failures.Add("$name has no boolean provesClaim.") }
    if ([string]::IsNullOrWhiteSpace([string](Get-AcceptanceProperty $entry "reason"))) { [void]$failures.Add("$name has no reason for its provesClaim.") }
}
foreach ($number in 1..37) {
    $name = "AC-{0:D2}" -f $number
    if (-not $owners.ContainsKey($name)) { [void]$failures.Add("$name has no entry in the ownership file.") }
}

$summary = Read-AcceptanceJson -Path (Join-Path $evidencePath "summary.json")
if ($null -eq $summary) {
    [void]$failures.Add("summary.json is missing or unreadable in $evidencePath.")
}
else {
    $rows = @(Get-AcceptanceRows -EvidenceDir $evidencePath -Ownership $owners -CodeTree $CodeTree -NowUtc $nowUtc -MaxAgeDays $MaxAgeDays)
    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.ownerPlan)) { [void]$failures.Add("$($row.ac) has evidence but no owner.") }
        if ($row.status -eq "Pass" -and $row.contract -ne "complete") { [void]$failures.Add("$($row.ac) is a Pass with an incomplete contract.") }
    }

    # The summary must say what the folders say; a hand-edited row or total does not survive this.
    $recorded = @{}
    foreach ($row in @($summary.rows)) { $recorded[[string]$row.ac] = $row }
    foreach ($row in $rows) {
        if (-not $recorded.ContainsKey($row.ac)) {
            [void]$failures.Add("$($row.ac) is missing from summary.json.")
        }
        elseif ([string]$recorded[$row.ac].status -ne $row.status) {
            [void]$failures.Add("$($row.ac): the summary says $($recorded[$row.ac].status), the evidence says $($row.status).")
        }
    }
    foreach ($name in @($recorded.Keys)) {
        if (@($rows | Where-Object { $_.ac -eq $name }).Count -eq 0) { [void]$failures.Add("$name is in summary.json but has neither a folder nor an owner.") }
    }

    $expected = Get-AcceptanceTotals -Rows $rows
    foreach ($key in @($expected.Keys)) {
        $value = Get-AcceptanceProperty $summary.totals $key
        if ([int]$value -ne [int]$expected[$key]) { [void]$failures.Add("Total '$key' is $value in summary.json; the evidence gives $($expected[$key]).") }
    }

    # PLAN-66 (S3): "not run by rule" holds only for the classes the rule of the scope names, and the summary says what the file says.
    $scopePath = if ([System.IO.Path]::IsPathRooted($Scope)) { $Scope } else { Join-Path $RepoRoot $Scope }
    $scopeFile = Read-AcceptanceJson -Path $scopePath
    $ruleClasses = @()
    if ($null -ne $scopeFile) { $ruleClasses = @(Get-AcceptanceRuleClasses -ScopeFile $scopeFile) }
    $declared = @(Get-AcceptanceProperty $summary "notRunByRule" | Where-Object { $_ })
    foreach ($class in $declared) {
        if ([string]$class -notin $ruleClasses) { [void]$failures.Add("$class is recorded as not run by rule, and the rule of the scope does not name it.") }
    }
    $file = Read-AcceptanceJson -Path (Join-Path $evidencePath "not-run-by-rule.json")
    $inFile = @()
    if ($null -ne $file) { $inFile = @($file.classes | Where-Object { $_ }) }
    if ((@($declared | Sort-Object) -join ",") -ne (@($inFile | Sort-Object) -join ",")) {
        [void]$failures.Add("summary.json and not-run-by-rule.json do not name the same classes.")
    }

    # summary.md is what people read.
    $markdownPath = Join-Path $evidencePath "summary.md"
    $totalsLine = Format-AcceptanceTotalsLine -Totals $expected
    if (-not (Test-Path -LiteralPath $markdownPath) -or -not [System.IO.File]::ReadAllText($markdownPath).Contains($totalsLine)) {
        [void]$failures.Add("summary.md does not carry the totals of the evidence: $totalsLine")
    }

    if ($Committed) {
        if ([string]$summary.source -ne "ci") { [void]$failures.Add("The committed summary has source '$($summary.source)'; only a CI run may produce it (D-QA-20).") }
        $runId = [string]$summary.ciRunId
        if ($runId -notmatch '^[0-9]+$') {
            [void]$failures.Add("The committed summary has no CI run id.")
        }
        elseif (-not $SkipRunLookup) {
            if ([string]::IsNullOrWhiteSpace($Repository)) { $Repository = [string]$env:GITHUB_REPOSITORY }
            if ([string]::IsNullOrWhiteSpace($Repository)) {
                $origin = & git -C $RepoRoot remote get-url origin
                if ($origin -match 'github\.com[:/](.+?)(\.git)?$') { $Repository = $Matches[1] }
            }
            $found = @(& gh api "repos/$Repository/actions/runs/$runId" --jq '.repository.full_name, .conclusion, .event' 2>$null)
            if ($LASTEXITCODE -ne 0 -or $found.Count -lt 3 -or [string]$found[0] -ne $Repository) { [void]$failures.Add("Run $runId does not belong to $Repository.") }
            else {
                # P-3: the evidence of record comes from a green run of a pull request or a push, never from a narrowed dispatch.
                if ([string]$found[1] -ne "success") { [void]$failures.Add("Run $runId concluded '$($found[1])'; the evidence of record comes from a green run.") }
                if ([string]$found[2] -notin @("pull_request", "push")) { [void]$failures.Add("Run $runId is a '$($found[2])' run; the evidence of record comes from a pull request or a push.") }
            }
        }
        if ([string]$summary.codeTree -ne $CodeTree) {
            [void]$failures.Add("The committed summary is for code tree $(Get-AcceptanceShort ([string]$summary.codeTree)), the code under test is $(Get-AcceptanceShort $CodeTree).")
        }
        $ofRun = @($rows | Where-Object { $_.ciRunId -eq $runId })
        if ($ofRun.Count -eq 0) { [void]$failures.Add("No evidence folder comes from run $runId.") }
        foreach ($row in @($ofRun | Where-Object { $_.status -eq "Stale" })) {
            [void]$failures.Add("$($row.ac) comes from run $runId and is stale: $($row.staleReasons -join '; ').")
        }
        foreach ($row in @($rows | Where-Object { $_.status -eq "Pass" -and $_.source -ne "ci" })) {
            [void]$failures.Add("$($row.ac) is a Pass from source '$($row.source)' in the committed summary.")
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "acceptance-baseline-validate: FAIL ($($failures.Count))"
    foreach ($failure in $failures) { Write-Host "  - $failure" }
    exit 1
}
$mode = if ($Committed) { "committed" } else { "run" }
Write-Host "acceptance-baseline-validate: PASS ($mode summary)"
exit 0
