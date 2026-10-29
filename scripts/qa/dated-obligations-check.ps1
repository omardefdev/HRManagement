# QA-034: dated-obligations horizon check.
# Every date in the repository that can stop CI or a service on a calendar day (exception expiry, review due date,
# quarantine review, CI exclusion review) is registered in docs/qa/dated-obligations.json. This script resolves each
# registered item, warns when it is due within -WarnDays and fails when it is due within -FailDays, is already
# past due, or carries a date that is not yyyy-MM-dd.
# Usage:
#   pwsh -NoProfile -File scripts/qa/dated-obligations-check.ps1
#   pwsh -NoProfile -File scripts/qa/dated-obligations-check.ps1 -AsOfDate 2026-11-01 -Registry testdata/qa/dated-obligations/fail-7.json
# Exit codes: 0 = no open obligation at or inside the fail horizon; 1 = at least one failure.
# Runs on Windows PowerShell 5.1 and PowerShell 7 (no ?? / ternary).

param(
    [string]$Registry = "docs/qa/dated-obligations.json",
    [string]$AsOfDate = "",
    [int]$WarnDays = -1,
    [int]$FailDays = -1,
    [string]$RepoRoot = "",
    [string]$OutFile = "artifacts/dated-obligations.json"
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
}
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$utcStyles = [System.Globalization.DateTimeStyles]::AssumeUniversal -bor [System.Globalization.DateTimeStyles]::AdjustToUniversal

function Resolve-RepoPath([string]$rel) {
    if ([IO.Path]::IsPathRooted($rel)) { return $rel }
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

# Parses a strict yyyy-MM-dd date (invariant culture, UTC). PowerShell 7 ConvertFrom-Json turns ISO date strings
# into [datetime]; those were already validated by the JSON reader, so only their date part is kept.
function ConvertTo-UtcDate($value) {
    if ($null -eq $value) { return $null }
    if ($value -is [datetime]) {
        return [datetime]::SpecifyKind($value.Date, [DateTimeKind]::Utc)
    }
    $text = ([string]$value).Trim()
    $parsed = [datetime]::MinValue
    if ([datetime]::TryParseExact($text, "yyyy-MM-dd", $invariant, $utcStyles, [ref]$parsed)) {
        return $parsed.Date
    }
    throw "unparseable date '$text' (expected yyyy-MM-dd)"
}

function Format-Date([datetime]$d) { return $d.ToString("yyyy-MM-dd", $invariant) }

# Raw text of a field for reporting (keeps the original spelling of an unparseable value).
function Get-FieldText($value) {
    if ($null -eq $value) { return "" }
    if ($value -is [datetime]) { return (Format-Date $value) }
    return [string]$value
}

# Dotted path inside an object: "eol.eolDate".
function Get-PathValue($obj, [string]$path) {
    if (-not $path) { return $null }
    $cur = $obj
    foreach ($seg in $path.Split('.')) {
        if ($null -eq $cur) { return $null }
        $prop = $cur.PSObject.Properties[$seg]
        if ($null -eq $prop) { return $null }
        $cur = $prop.Value
    }
    return $cur
}

# Selector: "$" (the root object), "a.b" (nested object), "items[*]" (each array element), "a[*].b[*]".
function Select-Items($root, [string]$selector) {
    if (-not $selector -or $selector -eq '$') { return ,@($root) }
    $current = @($root)
    foreach ($seg in $selector.Split('.')) {
        $next = @()
        $isArray = $seg.EndsWith("[*]")
        $name = $seg
        if ($isArray) { $name = $seg.Substring(0, $seg.Length - 3) }
        foreach ($node in $current) {
            if ($null -eq $node) { continue }
            $prop = $node.PSObject.Properties[$name]
            if ($null -eq $prop -or $null -eq $prop.Value) { continue }
            if ($isArray) { foreach ($el in @($prop.Value)) { $next += ,$el } }
            else { $next += ,$prop.Value }
        }
        $current = $next
    }
    return ,$current
}

$registryFull = Resolve-RepoPath $Registry
if (-not (Test-Path $registryFull)) { throw "registry not found: $registryFull" }
$reg = Get-Content -Raw -Encoding utf8 -Path $registryFull | ConvertFrom-Json

$defaultWarn = 30
$defaultFail = 7
if ($null -ne $reg.defaults) {
    if ($null -ne $reg.defaults.warnDays) { $defaultWarn = [int]$reg.defaults.warnDays }
    if ($null -ne $reg.defaults.failDays) { $defaultFail = [int]$reg.defaults.failDays }
}
if ($WarnDays -ge 0) { $defaultWarn = $WarnDays }
if ($FailDays -ge 0) { $defaultFail = $FailDays }

if ($AsOfDate) {
    $asOf = ConvertTo-UtcDate $AsOfDate
}
else {
    $asOf = [datetime]::SpecifyKind([datetime]::UtcNow.Date, [DateTimeKind]::Utc)
}

$rows = @()
$errors = @()
$warnings = @()

foreach ($ob in @($reg.obligations)) {
    $obId = [string]$ob.id
    $file = [string]$ob.file
    $warn = $defaultWarn
    $fail = $defaultFail
    if ($null -ne $ob.warnDays) { $warn = [int]$ob.warnDays }
    if ($null -ne $ob.failDays) { $fail = [int]$ob.failDays }
    $closed = @()
    if ($null -ne $ob.closedStatuses) { $closed = @($ob.closedStatuses | ForEach-Object { [string]$_ }) }

    $full = Resolve-RepoPath $file
    if (-not (Test-Path $full)) {
        $errors += "${obId}: registered file not found: $file"
        continue
    }
    $doc = Get-Content -Raw -Encoding utf8 -Path $full | ConvertFrom-Json
    $items = Select-Items $doc ([string]$ob.selector)
    if (@($items).Count -eq 0) {
        if ($ob.allowEmpty -ne $true) {
            $errors += "${obId}: selector '$($ob.selector)' matches nothing in $file (stale registration)"
        }
        continue
    }

    $index = 0
    foreach ($item in @($items)) {
        $itemId = Get-FieldText (Get-PathValue $item ([string]$ob.idField))
        if (-not $itemId) { $itemId = "$obId[$index]" }
        $index++
        $status = ""
        if ($ob.statusField) { $status = Get-FieldText (Get-PathValue $item ([string]$ob.statusField)) }
        $rawDate = Get-PathValue $item ([string]$ob.dateField)
        $row = [ordered]@{
            obligation = $obId
            item = $itemId
            file = $file
            field = [string]$ob.dateField
            date = (Get-FieldText $rawDate)
            status = $status
            owner = [string]$ob.owner
            enforcedBy = @($ob.enforcedBy)
            daysLeft = $null
            result = ""
        }

        if ($status -and ($closed -contains $status)) {
            $row.result = "closed"
            $rows += New-Object psobject -Property $row
            continue
        }
        if ($null -eq $rawDate -or ([string]$rawDate).Trim() -eq "") {
            if ($ob.allowMissingDate -eq $true) {
                $row.result = "no-date"
            }
            else {
                $row.result = "fail"
                $errors += "${obId}/${itemId}: $($ob.dateField) missing in $file"
            }
            $rows += New-Object psobject -Property $row
            continue
        }

        try {
            $due = ConvertTo-UtcDate $rawDate
        }
        catch {
            $row.result = "fail"
            $errors += "${obId}/${itemId}: $($_.Exception.Message) in $file ($($ob.dateField))"
            $rows += New-Object psobject -Property $row
            continue
        }

        $days = [int][Math]::Floor(($due - $asOf).TotalDays)
        $row.daysLeft = $days
        if ($days -lt 0) {
            $row.result = "fail"
            $errors += "${obId}/${itemId}: $($ob.dateField)=$(Format-Date $due) is past due by $(-$days) day(s) ($file, owner $($ob.owner))"
        }
        elseif ($days -le $fail) {
            $row.result = "fail"
            $errors += "${obId}/${itemId}: $($ob.dateField)=$(Format-Date $due) is due in $days day(s) (fail horizon $fail; $file, owner $($ob.owner))"
        }
        elseif ($days -le $warn) {
            $row.result = "warn"
            $warnings += "${obId}/${itemId}: $($ob.dateField)=$(Format-Date $due) is due in $days day(s) (warn horizon $warn; $file, owner $($ob.owner))"
        }
        else {
            $row.result = "ok"
        }
        $rows += New-Object psobject -Property $row
    }
}

$open = @($rows | Where-Object { $null -ne $_.daysLeft } | Sort-Object daysLeft)
$sorted = @($open) + @($rows | Where-Object { $null -eq $_.daysLeft })

Write-Host ("Dated obligations as of {0} (warn <= {1} days, fail <= {2} days) - registry {3}" -f (Format-Date $asOf), $defaultWarn, $defaultFail, $Registry)
Write-Host ("{0,-6} {1,8}  {2,-10}  {3,-40} {4}" -f "RESULT", "DAYS", "DATE", "ITEM", "FILE")
foreach ($r in $sorted) {
    $daysText = ""
    if ($null -ne $r.daysLeft) { $daysText = [string]$r.daysLeft }
    Write-Host ("{0,-6} {1,8}  {2,-10}  {3,-40} {4}" -f $r.result.ToUpperInvariant(), $daysText, $r.date, "$($r.obligation)/$($r.item)", $r.file)
}
if ($open.Count -gt 0) {
    $n = $open[0]
    Write-Host ("Nearest open obligation: {0}/{1} {2}={3} in {4} day(s) ({5})" -f $n.obligation, $n.item, $n.field, $n.date, $n.daysLeft, $n.file)
}

foreach ($w in $warnings) { Write-Host "::warning::dated obligation $w" }
foreach ($e in $errors) { Write-Host "::error::dated obligation $e" }

$report = [ordered]@{
    card = "QA-034"
    registry = $Registry
    asOfDate = (Format-Date $asOf)
    warnDays = $defaultWarn
    failDays = $defaultFail
    ok = ($errors.Count -eq 0)
    warningCount = $warnings.Count
    errorCount = $errors.Count
    nearest = $(if ($open.Count -gt 0) { "$($open[0].obligation)/$($open[0].item) $($open[0].date)" } else { $null })
    warnings = $warnings
    errors = $errors
    items = $sorted
}
if ($OutFile) {
    $outFull = Resolve-RepoPath $OutFile
    $outDir = Split-Path -Parent $outFull
    if ($outDir) { New-Item -ItemType Directory -Force -Path $outDir | Out-Null }
    ($report | ConvertTo-Json -Depth 6) | Set-Content -Path $outFull -Encoding utf8
    Write-Host "Wrote $outFull"
}

if ($errors.Count -gt 0) {
    Write-Host "FAIL: $($errors.Count) dated obligation(s) at or inside the fail horizon"
    exit 1
}
Write-Host "PASS: dated obligations ($($warnings.Count) warning(s))"
exit 0
