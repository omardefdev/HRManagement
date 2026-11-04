#Requires -Version 5.1
# PLAN-41: the one definition of the acceptance evidence states (G-QA-08). Dot-sourced by
# scripts/acceptance-evidence-summarize.ps1 and scripts/acceptance-baseline-validate.ps1; EvidenceRecorder takes the code
# tree from scripts/ci/acceptance-code-tree.ps1, which calls Get-AcceptanceCodeTree here. Runs under Windows PowerShell 5.1
# and PowerShell 7. ASCII only.

$script:AcceptanceContractFiles = @(
    "metadata.json", "steps.md", "before.json", "after.json",
    "audit.json", "db-checks.json", "timing.json", "result.json"
)
$script:AcceptanceFolderPattern = '^(AC|HCM)-'

# P-2: freshness is by code tree, not by commit. Committing evidence or a report changes the commit, never this hash.
function Get-AcceptanceCodeTree {
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [string]$Commit = "HEAD"
    )
    # core.quotepath keeps every path ASCII, so both PowerShells hash the same bytes; a quoted path starts with ".
    $lines = & git -C $RepoRoot -c core.quotepath=true ls-tree -r $Commit
    if ($LASTEXITCODE -ne 0) { throw "git ls-tree failed for '$Commit'." }
    $kept = @($lines | Where-Object { $_ -notmatch "`t`"?(acceptance-evidence|reports|docs)/" })
    if ($kept.Count -eq 0) { throw "git ls-tree returned no file for '$Commit'." }
    $bytes = [System.Text.Encoding]::UTF8.GetBytes((($kept -join "`n") + "`n"))
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (([System.BitConverter]::ToString($sha.ComputeHash($bytes))) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Read-AcceptanceJson {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Get-AcceptanceProperty {
    param($Object, [Parameter(Mandatory)][string]$Name)
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

# PowerShell 7 turns ISO strings into DateTime in ConvertFrom-Json, 5.1 keeps them as strings; the machine's culture may
# use another calendar (ar-SA), so everything here is parsed and printed with the invariant culture.
function ConvertTo-AcceptanceUtc {
    param($Value)
    if ($null -eq $Value) { return $null }
    if ($Value -is [datetime]) { return $Value.ToUniversalTime() }
    if ($Value -is [datetimeoffset]) { return $Value.UtcDateTime }
    $text = [string]$Value
    if ([string]::IsNullOrWhiteSpace($text)) { return $null }
    $parsed = [datetimeoffset]::MinValue
    $styles = [System.Globalization.DateTimeStyles]::AssumeUniversal
    if ([datetimeoffset]::TryParse($text, [System.Globalization.CultureInfo]::InvariantCulture, $styles, [ref]$parsed)) {
        return $parsed.UtcDateTime
    }
    return $null
}

function Format-AcceptanceDate {
    param($Utc, [string]$Format = "yyyy-MM-dd")
    if ($null -eq $Utc) { return "-" }
    return ([datetime]$Utc).ToString($Format, [System.Globalization.CultureInfo]::InvariantCulture)
}

function Get-AcceptanceShort {
    param([string]$Sha)
    if ([string]::IsNullOrWhiteSpace($Sha)) { return "-" }
    if ($Sha.Length -le 12) { return $Sha }
    return $Sha.Substring(0, 12)
}

function Get-AcceptanceOwnership {
    param([Parameter(Mandatory)][string]$Path)
    $file = Read-AcceptanceJson -Path $Path
    if ($null -eq $file) { throw "Cannot read the AC ownership file: $Path" }
    $map = @{}
    foreach ($property in $file.acs.PSObject.Properties) { $map[$property.Name] = $property.Value }
    return $map
}

# Every reason the folder cannot count as evidence of the code under test (D-QA-21, P-2). Empty means fresh.
function Get-AcceptanceStaleReasons {
    param($Metadata, $Result, [string]$CodeTree, [datetime]$NowUtc, [int]$MaxAgeDays)
    $reasons = New-Object System.Collections.Generic.List[string]
    $folderTree = [string](Get-AcceptanceProperty $Metadata "codeTree")
    $sourceSha = [string](Get-AcceptanceProperty $Metadata "sourceSha")
    if ([string]::IsNullOrWhiteSpace($folderTree)) {
        [void]$reasons.Add("no code tree recorded")
    }
    elseif ($folderTree -ne $CodeTree) {
        [void]$reasons.Add("code tree $(Get-AcceptanceShort $folderTree) differs from $(Get-AcceptanceShort $CodeTree)")
    }
    if ([string]::IsNullOrWhiteSpace($sourceSha)) { [void]$reasons.Add("no source commit recorded") }

    $images = Get-AcceptanceProperty $Metadata "imageRevisions"
    $imageProperties = @()
    if ($null -ne $images) { $imageProperties = @($images.PSObject.Properties) }
    if ($imageProperties.Count -eq 0) { [void]$reasons.Add("no image revisions recorded") }
    foreach ($image in $imageProperties) {
        $revision = [string]$image.Value
        if ([string]::IsNullOrWhiteSpace($revision) -or $revision -eq "unknown") {
            [void]$reasons.Add("image $($image.Name) has no source revision")
        }
        elseif ($revision -like "*-dirty") {
            [void]$reasons.Add("image $($image.Name) was built from a changed tree")
        }
        elseif ($revision -ne $sourceSha) {
            [void]$reasons.Add("image $($image.Name) was built from $(Get-AcceptanceShort $revision)")
        }
    }

    $recorded = @()
    $scenarios = Get-AcceptanceProperty $Result "scenarios"
    if ($null -ne $scenarios) {
        foreach ($scenario in $scenarios.PSObject.Properties) {
            $scenarioTree = [string](Get-AcceptanceProperty $scenario.Value "codeTree")
            if ([string]::IsNullOrWhiteSpace($scenarioTree) -or $scenarioTree -ne $CodeTree) {
                [void]$reasons.Add("scenario $($scenario.Name) comes from another code tree")
            }
            $at = ConvertTo-AcceptanceUtc (Get-AcceptanceProperty $scenario.Value "recordedAtUtc")
            if ($null -ne $at) { $recorded += $at }
        }
    }
    if ($recorded.Count -eq 0) {
        $at = ConvertTo-AcceptanceUtc (Get-AcceptanceProperty $Metadata "capturedAtUtc")
        if ($null -ne $at) { $recorded += $at }
    }
    if ($recorded.Count -eq 0) {
        [void]$reasons.Add("no recording date")
    }
    else {
        $oldest = ($recorded | Sort-Object | Select-Object -First 1)
        if (($NowUtc - $oldest).TotalDays -gt $MaxAgeDays) { [void]$reasons.Add("older than $MaxAgeDays days") }
    }
    return @($reasons)
}

# The state of one AC, by the rules of PLAN-41 section 2, in their order: NotRun, Incomplete, Stale, Fail/Blocked, Pass.
function Get-AcceptanceState {
    param(
        [Parameter(Mandatory)][string]$Ac,
        [string]$Folder,
        $Owner,
        [Parameter(Mandatory)][string]$CodeTree,
        [Parameter(Mandatory)][datetime]$NowUtc,
        [int]$MaxAgeDays = 30
    )

    $row = [ordered]@{
        ac = $Ac
        status = "NotRun"
        reason = $null
        pass = 0
        fail = 0
        blocked = 0
        contract = "-"
        missing = @()
        profile = "-"
        sourceSha = $null
        codeTree = $null
        images = "-"
        recorded = "-"
        source = "-"
        ciRunId = $null
        freshness = "-"
        staleReasons = @()
        provesClaim = [bool](Get-AcceptanceProperty $Owner "provesClaim")
        ownerPlan = [string](Get-AcceptanceProperty $Owner "ownerPlan")
        claimNote = [string](Get-AcceptanceProperty $Owner "reason")
    }

    $hasFolder = -not [string]::IsNullOrWhiteSpace($Folder) -and (Test-Path -LiteralPath $Folder)
    if (-not $hasFolder) { return [pscustomobject]$row }
    if ([bool](Get-AcceptanceProperty $Owner "legacyContractOnly")) {
        $row.reason = "Legacy contract text only: the evidence stays for history and does not count."
        return [pscustomobject]$row
    }

    $missing = @($script:AcceptanceContractFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Folder $_)) })
    $row.missing = $missing
    $row.contract = if ($missing.Count -eq 0) { "complete" } else { "incomplete" }

    $metadata = Read-AcceptanceJson -Path (Join-Path $Folder "metadata.json")
    $result = Read-AcceptanceJson -Path (Join-Path $Folder "result.json")
    $row.profile = if (Get-AcceptanceProperty $metadata "profile") { [string](Get-AcceptanceProperty $metadata "profile") } else { "-" }
    $row.sourceSha = [string](Get-AcceptanceProperty $metadata "sourceSha")
    if ([string]::IsNullOrWhiteSpace($row.sourceSha)) { $row.sourceSha = [string](Get-AcceptanceProperty $metadata "gitSha") }
    $row.codeTree = [string](Get-AcceptanceProperty $metadata "codeTree")
    $row.source = if (Get-AcceptanceProperty $metadata "source") { [string](Get-AcceptanceProperty $metadata "source") } else { "-" }
    $row.ciRunId = [string](Get-AcceptanceProperty $metadata "ciRunId")
    $at = ConvertTo-AcceptanceUtc (Get-AcceptanceProperty $result "recordedAtUtc")
    if ($null -eq $at) { $at = ConvertTo-AcceptanceUtc (Get-AcceptanceProperty $metadata "capturedAtUtc") }
    $row.recorded = Format-AcceptanceDate $at

    $revisions = @()
    $images = Get-AcceptanceProperty $metadata "imageRevisions"
    if ($null -ne $images) { $revisions = @($images.PSObject.Properties | ForEach-Object { [string]$_.Value }) }
    if ($revisions.Count -gt 0) {
        $distinct = @($revisions | Sort-Object -Unique)
        $row.images = if ($distinct.Count -eq 1) { "$($revisions.Count) x $(Get-AcceptanceShort $distinct[0])" } else { "$($revisions.Count), $($distinct.Count) sources" }
    }

    $statuses = @()
    $reasonText = $null
    $scenarios = Get-AcceptanceProperty $result "scenarios"
    if ($null -ne $scenarios) {
        foreach ($scenario in $scenarios.PSObject.Properties) { $statuses += [string](Get-AcceptanceProperty $scenario.Value "status") }
    }
    if ($statuses.Count -eq 0 -and $null -ne $result) {
        $top = [string](Get-AcceptanceProperty $result "status")
        if (-not [string]::IsNullOrWhiteSpace($top)) { $statuses += $top }
    }
    $reasonText = [string](Get-AcceptanceProperty $result "reason")
    $row.pass = @($statuses | Where-Object { $_ -eq "Pass" }).Count
    $row.fail = @($statuses | Where-Object { $_ -eq "Fail" }).Count
    $row.blocked = @($statuses | Where-Object { $_ -in @("Blocked", "Deferred") }).Count
    $other = $statuses.Count - $row.pass - $row.fail - $row.blocked
    if (-not [string]::IsNullOrWhiteSpace($reasonText)) { $row.reason = $reasonText }

    if ($missing.Count -gt 0 -or $null -eq $result -or $statuses.Count -eq 0 -or $other -gt 0) {
        $row.status = "Incomplete"
        $row.freshness = "-"
        return [pscustomobject]$row
    }

    $stale = @(Get-AcceptanceStaleReasons -Metadata $metadata -Result $result -CodeTree $CodeTree -NowUtc $NowUtc -MaxAgeDays $MaxAgeDays)
    $row.staleReasons = $stale
    if ($stale.Count -gt 0) {
        $row.status = "Stale"
        $row.freshness = "stale"
        return [pscustomobject]$row
    }

    $row.freshness = "fresh"
    if ($row.fail -gt 0) { $row.status = "Fail" }
    elseif ($row.blocked -gt 0) { $row.status = "Blocked" }
    else { $row.status = "Pass" }
    return [pscustomobject]$row
}

# Every AC with an owner or a folder, one row each.
function Get-AcceptanceRows {
    param(
        [Parameter(Mandatory)][string]$EvidenceDir,
        [Parameter(Mandatory)][hashtable]$Ownership,
        [Parameter(Mandatory)][string]$CodeTree,
        [Parameter(Mandatory)][datetime]$NowUtc,
        [int]$MaxAgeDays = 30
    )
    $folders = @{}
    if (Test-Path -LiteralPath $EvidenceDir) {
        Get-ChildItem -LiteralPath $EvidenceDir -Directory | Where-Object { $_.Name -match $script:AcceptanceFolderPattern } | ForEach-Object {
            $folders[$_.Name] = $_.FullName
            # A folder inside a folder is a run of its own: AC-HCM-33/prodlike, AC-OPS-36/vps. It takes the owner of its parent.
            $parent = $_.Name
            Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "result.json") } |
                ForEach-Object { $folders["$parent/$($_.Name)"] = $_.FullName }
        }
    }
    $names = @(@($Ownership.Keys) + @($folders.Keys) | Sort-Object -Unique)
    foreach ($name in $names) {
        $owner = if ($Ownership.ContainsKey($name)) { $Ownership[$name] } else { $Ownership[($name -split '/')[0]] }
        Get-AcceptanceState -Ac $name -Folder ([string]$folders[$name]) -Owner $owner -CodeTree $CodeTree -NowUtc $NowUtc -MaxAgeDays $MaxAgeDays
    }
}

# Separate totals: a Pass whose test does not prove its claim is never counted as proven.
function Format-AcceptanceTotalsLine {
    param([Parameter(Mandatory)]$Totals)
    return ("**Totals:** {0} ACs: {1} proven, {2} passed but not proving, {3} stale, {4} incomplete, {5} not run, {6} fail, {7} blocked." -f `
        $Totals.total, $Totals.proven, $Totals.passedNotProving, $Totals.stale, $Totals.incomplete, $Totals.notRun, $Totals.fail, $Totals.blocked)
}

function Get-AcceptanceTotals {
    param([object[]]$Rows)
    $rows = @($Rows)
    $pass = @($rows | Where-Object { $_.status -eq "Pass" })
    return [ordered]@{
        total = $rows.Count
        proven = @($pass | Where-Object { $_.provesClaim }).Count
        passedNotProving = @($pass | Where-Object { -not $_.provesClaim }).Count
        stale = @($rows | Where-Object { $_.status -eq "Stale" }).Count
        incomplete = @($rows | Where-Object { $_.status -eq "Incomplete" }).Count
        notRun = @($rows | Where-Object { $_.status -eq "NotRun" }).Count
        fail = @($rows | Where-Object { $_.status -eq "Fail" }).Count
        blocked = @($rows | Where-Object { $_.status -eq "Blocked" }).Count
    }
}
