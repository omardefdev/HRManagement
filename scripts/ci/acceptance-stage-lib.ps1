#Requires -Version 5.1
# PLAN-66: what the parallel Acceptance job shares: which entries of order[] a run selects, the stage jobs of the scope, and how
# the evidence of one stage joins another. Runs under Windows PowerShell 5.1 and PowerShell 7. ASCII only. Needs
# scripts/acceptance-evidence-lib.ps1 (Read-AcceptanceJson, Get-AcceptanceProperty).

function Get-AcceptanceStageKey {
    param([Parameter(Mandatory)]$Entry)
    return "$($Entry.part):$($Entry.group)"
}

# The entries of order[] named by -Stage, in the order of order[]: the order of the stages is the scope's, not the caller's.
# No -Stage: all of them. An unknown name ends the run, so a typo can never read as a stage that had nothing to do.
function Get-AcceptanceStageSelection {
    param([Parameter(Mandatory)]$ScopeFile, [string]$Stage = "")
    $order = @($ScopeFile.order)
    $names = @($Stage -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($names.Count -eq 0) { return $order }
    $known = @($order | ForEach-Object { Get-AcceptanceStageKey $_ })
    foreach ($name in $names) {
        if ($name -notin $known) { throw "Unknown stage '$name'. The stages of the scope: $($known -join ', ')" }
    }
    if (@($names | Sort-Object -Unique).Count -ne $names.Count) { throw "-Stage names a stage twice: $Stage" }
    return @($order | Where-Object { (Get-AcceptanceStageKey $_) -in $names })
}

# The classes of the selected stages, without the ones -Classes left out.
function Get-AcceptanceStageClasses {
    param([Parameter(Mandatory)]$ScopeFile, [object[]]$Selection, [string[]]$Limit = @())
    $classes = New-Object System.Collections.Generic.List[string]
    foreach ($entry in @($Selection)) {
        foreach ($item in @(Get-AcceptanceProperty $ScopeFile ([string]$entry.part))) {
            if ($null -eq $item -or [string]$item.group -ne [string]$entry.group) { continue }
            if (@($Limit).Count -gt 0 -and [string]$item.class -notin $Limit) { continue }
            [void]$classes.Add([string]$item.class)
        }
    }
    return @($classes)
}

# stageJobs[] of the scope in the order of their first stage, which is the order a sequential run writes its evidence in.
function Get-AcceptanceStageJobs {
    param([Parameter(Mandatory)]$ScopeFile)
    $position = @{}
    foreach ($entry in @($ScopeFile.order)) { $position[(Get-AcceptanceStageKey $entry)] = $position.Count }
    return @(@(Get-AcceptanceProperty $ScopeFile "stageJobs") | Where-Object { $null -ne $_ } |
        Sort-Object -Property { $position[[string](@($_.stages)[0])] })
}

# PLAN-66 (S3): the stage on demand and the classes it holds.
function Get-AcceptanceRuleClasses {
    param([Parameter(Mandatory)]$ScopeFile)
    $rule = Get-AcceptanceProperty $ScopeFile "metaRule"
    if ($null -eq $rule) { return @() }
    $selection = @(Get-AcceptanceStageSelection -ScopeFile $ScopeFile -Stage ([string]$rule.stage))
    return @(Get-AcceptanceStageClasses -ScopeFile $ScopeFile -Selection $selection)
}

# As EvidenceRecorder.AggregateStatus: the worst scenario decides.
function Get-AcceptanceWorstStatus {
    param([string[]]$Statuses)
    $list = @($Statuses)
    if ($list.Count -eq 0) { return "Unknown" }
    if ($list -contains "Fail") { return "Fail" }
    if ($list -contains "Blocked") { return "Blocked" }
    $other = @($list | Where-Object { $_ -ne "Pass" })
    if ($other.Count -gt 0) { return [string]$other[0] }
    return "Pass"
}

function Write-AcceptanceJsonFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)]$Value)
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 20) + "`n"), $utf8NoBom)
}

# Two stages wrote one result.json: both keep their scenarios, as when one process appends to the file, and the later one wins a
# scenario they share. A Pass of one stage never hides a Fail of another.
function Merge-AcceptanceResultFile {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Target)
    $later = Read-AcceptanceJson -Path $Source
    $earlier = Read-AcceptanceJson -Path $Target
    if ($null -eq $earlier -or $null -eq $later) {
        Copy-Item -LiteralPath $Source -Destination $Target -Force
        return
    }
    $merged = [ordered]@{}
    foreach ($document in @($earlier, $later)) {
        $scenarios = Get-AcceptanceProperty $document "scenarios"
        if ($null -eq $scenarios) { continue }
        foreach ($scenario in $scenarios.PSObject.Properties) { $merged[$scenario.Name] = $scenario.Value }
    }
    if ($merged.Count -eq 0) {
        Copy-Item -LiteralPath $Source -Destination $Target -Force
        return
    }
    $open = New-Object System.Collections.Generic.List[string]
    $statuses = New-Object System.Collections.Generic.List[string]
    foreach ($name in @($merged.Keys)) {
        $status = [string](Get-AcceptanceProperty $merged[$name] "status")
        [void]$statuses.Add($status)
        if ($status -ne "Pass") { [void]$open.Add("${name}: $status $([char]0x2014) $([string](Get-AcceptanceProperty $merged[$name] 'reason'))") }
    }
    $reason = $null
    if ($open.Count -gt 0) { $reason = $open -join "; " }
    Write-AcceptanceJsonFile -Path $Target -Value ([ordered]@{
            scenarios = $merged
            status = (Get-AcceptanceWorstStatus -Statuses @($statuses))
            reason = $reason
            correlationId = (Get-AcceptanceProperty $later "correlationId")
            recordedAtUtc = (Get-AcceptanceProperty $later "recordedAtUtc")
        })
}

# The evidence of one stage on top of what is already in $Target. Files keep the rule of a sequential run, the last writer
# wins, except result.json (above) and the scenario log of summary.md (appended once). summary.json belongs to the summarizer.
function Merge-AcceptanceEvidenceFolder {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Target)
    if (-not (Test-Path -LiteralPath $Source)) { return }
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $rootLength = (Get-Item -LiteralPath $Source).FullName.TrimEnd('\', '/').Length + 1
    foreach ($file in @(Get-ChildItem -LiteralPath $Source -Recurse -File -Force)) {
        $relative = $file.FullName.Substring($rootLength)
        $destination = Join-Path $Target $relative
        if ($relative -eq "summary.json") { continue }
        if ($relative -eq "summary.md") {
            $known = @()
            if (Test-Path -LiteralPath $destination) { $known = @([System.IO.File]::ReadAllLines($destination, $utf8NoBom)) }
            $added = @([System.IO.File]::ReadAllLines($file.FullName, $utf8NoBom) | Where-Object { $_ -match '^- \*\*(AC|HCM)-' -and $_ -notin $known })
            if ($added.Count -gt 0) {
                [System.IO.File]::WriteAllLines($destination, [string[]](@($known) + @($added)), $utf8NoBom)
            }
            continue
        }
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        if ($file.Name -eq "result.json" -and (Test-Path -LiteralPath $destination)) {
            Merge-AcceptanceResultFile -Source $file.FullName -Target $destination
        }
        else {
            Copy-Item -LiteralPath $file.FullName -Destination $destination -Force
        }
    }
}

# PLAN-66 (S3): the trigger paths of the rule are globs over repository paths: ** crosses folders, * stays inside one.
function Convert-AcceptanceGlobToRegex {
    param([Parameter(Mandatory)][string]$Glob)
    $pattern = [regex]::Escape($Glob.Replace('\', '/'))
    $pattern = $pattern.Replace('\*\*/', '(.*/)?').Replace('\*\*', '.*').Replace('\*', '[^/]*').Replace('\?', '[^/]')
    return "^$pattern$"
}

function Test-AcceptanceTriggerPath {
    param([Parameter(Mandatory)]$ScopeFile, [Parameter(Mandatory)][string]$Path)
    $rule = Get-AcceptanceProperty $ScopeFile "metaRule"
    if ($null -eq $rule) { return $false }
    $normal = $Path.Trim().Trim('"').Replace('\', '/')
    foreach ($glob in @($rule.triggerPaths)) {
        if ($normal -cmatch (Convert-AcceptanceGlobToRegex -Glob ([string]$glob))) { return $true }
    }
    return $false
}

# PLAN-66 (S2): a hash of what the governance checks read at this commit. docs/qa/** and the like are outside the code tree, so
# without this a commit that changes them would reuse a run that never saw them.
function Get-AcceptanceInputsHash {
    param([Parameter(Mandatory)][string]$RepoRoot, [Parameter(Mandatory)]$ScopeFile, [string]$Commit = "HEAD")
    $lines = & git -C $RepoRoot -c core.quotepath=false ls-tree -r $Commit
    if ($LASTEXITCODE -ne 0) { throw "git ls-tree failed for '$Commit'." }
    $kept = @($lines | Where-Object { Test-AcceptanceTriggerPath -ScopeFile $ScopeFile -Path (($_ -split "`t", 2)[1]) })
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((($kept -join "`n") + "`n"))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (([System.BitConverter]::ToString($sha.ComputeHash($bytes))) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}
