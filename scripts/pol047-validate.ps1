# PLAN-47: the policy rule lifecycle, checked.
#  1. Static: no in-place seed writer and no same-actor publish flag in the product code; the validator's messages are
#     Arabic; the immutability trigger is in the migration; the two roles are in the realm manifest; the change sets exist.
#  2. Inventory (optional, -Container or -LogFile): the published rules the validator refuses, as HR.Core logged them at
#     start-up ("PLAN-47 validator inventory"). PLAN-49: a rule of an effective version is a defect, and the last start-up in
#     the log must list none (its summary line says "0 rule(s) of effective versions"). Rules of superseded versions are
#     history and are only counted. Nothing is fixed in place (a fix is a new change set).
#     The start-up session is the national tenant's, so those lines are about the national templates. The agencies' overrides
#     are read apart, each tenant in its own session ("PLAN-49 agency override inventory"); the last start-up must have taken
#     that part too and must list none in force, unless -AllowAgencyOverrideFindings (the acceptance stack seeds two agencies
#     with a layer written before the ceiling, on purpose: AC-TEN-48e).
#     The national summary also says how many templates the session read: none means the session was not the national
#     tenant's, and "0 rule(s)" would then say nothing, so it fails.
# Usage (PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-validate.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-validate.ps1 -Container hrm-acceptance-hr-core-web-1 -AllowAgencyOverrideFindings -Out artifacts/plan47/validator-inventory.txt
# Server (bash), both parts, for -LogFile:
#   docker logs hrm-hr-core-web 2>&1 | grep -E 'PLAN-47 validator inventory|PLAN-49 agency override inventory'
param(
    [string]$Container = "",
    [string]$LogFile = "",
    [string]$Out = "",
    [switch]$AllowAgencyOverrideFindings
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$failures = New-Object System.Collections.Generic.List[string]

function Read-Text([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { $script:failures.Add("missing $relative"); return "" }
    return [System.IO.File]::ReadAllText($path)
}

# 1. Static checks over the product code (tests are excluded: the acceptance test sends the old flag to prove it is ignored).
$forbidden = @("AddRuleIfMissingAsync", "DeactivateRuleIfPresentAsync", "SeedIncrementalRulesAsync", "SeedSelectCodePolicyRuleIfMissingAsync", "ForceSameActorForLab", "forceSameActorForLab")
$sources = Get-ChildItem -Path (Join-Path $root "src") -Recurse -Include *.cs, *.cshtml -File |
    Where-Object { $_.FullName -notmatch '[\\/](Tests|bin|obj)[\\/]' }
foreach ($file in $sources) {
    $text = [System.IO.File]::ReadAllText($file.FullName)
    foreach ($word in $forbidden) {
        if ($text.Contains($word)) { $failures.Add(("{0} still names {1}" -f $file.FullName.Substring($root.Length + 1), $word)) }
    }
}

$validator = Read-Text "src/Services/HRCore/HR.Core.Domain/Services/Policy/PolicyRuleValidator.cs"
$parser = Read-Text "src/Shared/HRManagement.SharedKernel/Policy/PolicyRuleJson.cs"
$arabic = '[' + [char]0x0600 + '-' + [char]0x06FF + ']'
foreach ($pair in @(@("PolicyRuleValidator", $validator), @("PolicyRuleJson", $parser))) {
    $messages = [regex]::Matches($pair[1], 'new\("[A-Z_]+",\s*\$?"((?:[^"\\]|\\.)*)"')
    if ($messages.Count -eq 0) { $failures.Add("$($pair[0]): no issue messages found") }
    foreach ($m in $messages) {
        if ($m.Groups[1].Value -notmatch $arabic) { $failures.Add("$($pair[0]): a message is not Arabic: $($m.Groups[1].Value)") }
    }
}
if ($parser -notmatch 'CultureInfo\.InvariantCulture') { $failures.Add("PolicyRuleJson does not parse numbers with the invariant culture") }
if ((Read-Text "src/Shared/HRManagement.SharedKernel/Workflow/WorkflowCondition.cs") -notmatch 'CultureInfo\.InvariantCulture') { $failures.Add("WorkflowCondition does not parse numbers with the invariant culture") }

$migration = Get-ChildItem -Path (Join-Path $root "src/Services/HRCore/HR.Core.Infrastructure/Migrations") -Filter "*_POL047_PolicyRuleLifecycle.cs" -File |
    Where-Object { $_.Name -notmatch 'Designer' } | Select-Object -First 1
if ($null -eq $migration) { $failures.Add("missing the POL047_PolicyRuleLifecycle migration") }
else {
    $m = [System.IO.File]::ReadAllText($migration.FullName)
    foreach ($needle in @("policy_rules_guard", "policy_versions_guard", "IX_PolicyTemplateVersions_OneDraftPerTemplate", "PolicyVersionEdits", "PolicySeedApplications")) {
        if (-not $m.Contains($needle)) { $failures.Add("migration lacks $needle") }
    }
    $bytes = [System.IO.File]::ReadAllBytes($migration.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $failures.Add("the migration carries a byte order mark") }
}

$manifest = Read-Text "docker/keycloak/realm-manifest.json"
foreach ($role in @("policy-author", "policy-approver")) {
    if ($manifest -notmatch ('"name":\s*"' + $role + '"')) { $failures.Add("realm manifest lacks the $role role") }
}
$sets = Read-Text "src/Services/HRCore/HR.Core.Infrastructure/Data/PolicySeedChangeSets.cs"
foreach ($id in @("POL-2026-10-30-BASELINE", "POL-2026-10-30-RETIRE-R-REC-HIRE", "POL-049-SCOPE-RETIRE", "POL-049-ASSETS-ACTION", "POL-049-IDCARD-ORPHAN", "POL-049-LEAVE")) {
    if (-not $sets.Contains($id)) { $failures.Add("change set $id is missing") }
}

# 2. The inventory of published rules the validator refuses, from the start-up log.
$logLines = @()
if (-not [string]::IsNullOrWhiteSpace($Container)) {
    # docker logs writes the container's stderr to stderr: under PowerShell 5.1 that is an error record, not a failure.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $log = (& docker logs $Container 2>&1 | ForEach-Object { "$_" }) -join "`n"
    $ErrorActionPreference = $previous
    $logLines = @($log -split "`r?`n")
} elseif (-not [string]::IsNullOrWhiteSpace($LogFile)) {
    $logLines = @([System.IO.File]::ReadAllLines((Resolve-Path $LogFile)))
}
$inventory = @($logLines | Where-Object { $_ -match 'PLAN-47 validator inventory' })
$overrides = @($logLines | Where-Object { $_ -match 'PLAN-49 agency override inventory' })
if ($inventory.Count -gt 0) {
    Write-Output "The validator inventory (start-up log):"
    $inventory | ForEach-Object { Write-Output ("  " + $_.Trim()) }
    if (-not [string]::IsNullOrWhiteSpace($Out)) {
        $dir = Split-Path -Parent $Out
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        # Both parts, in the order of the log: a file written here can be given back with -LogFile.
        $both = @($logLines | Where-Object { $_ -match 'PLAN-47 validator inventory' -or $_ -match 'PLAN-49 agency override inventory' })
        [System.IO.File]::WriteAllLines($Out, [string[]]$both, (New-Object System.Text.UTF8Encoding($false)))
    }

    # PLAN-49: the last start-up of the log, both parts by their place in it. One start-up writes the national part (a warning
    # for each refused rule of an effective version, then one summary) and then the agencies' part (its warnings, then its own
    # summary). A national line that follows an agencies' line, or a national line that ended a part, begins another start-up.
    $nationalEnd = 'validator inventory: (\d+ rule\(s\)|the inventory could not be taken)'
    $entries = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $logLines.Count; $i++) {
        if ($logLines[$i] -match 'PLAN-47 validator inventory') { $entries.Add(@{ Part = 'national'; Text = $logLines[$i] }) }
        elseif ($logLines[$i] -match 'PLAN-49 agency override inventory') { $entries.Add(@{ Part = 'override'; Text = $logLines[$i] }) }
    }
    $begin = 0
    for ($i = 1; $i -lt $entries.Count; $i++) {
        $before = $entries[$i - 1]
        if ($entries[$i].Part -eq 'national' -and ($before.Part -eq 'override' -or $before.Text -match $nationalEnd)) { $begin = $i }
    }
    $lastNational = @()
    $lastOverride = @()
    for ($i = $begin; $i -lt $entries.Count; $i++) {
        if ($entries[$i].Part -eq 'national') { $lastNational += $entries[$i].Text } else { $lastOverride += $entries[$i].Text }
    }

    $summaryPattern = 'validator inventory: (\d+) rule\(s\) of effective versions the validator refuses; (\d+) more in superseded versions \(history, unchanged\); (\d+) national template\(s\) read'
    $nationalSummary = @($lastNational | Where-Object { $_ -match 'validator inventory: \d+ ' })
    if (@($lastNational | Where-Object { $_ -match 'could not be taken' }).Count -gt 0) {
        $failures.Add("the inventory could not be taken at the last start-up")
    } elseif ($nationalSummary.Count -eq 0) {
        $failures.Add("the last start-up wrote inventory lines and no summary line (HR.Core writes one at every start-up)")
    } elseif ($nationalSummary[$nationalSummary.Count - 1] -notmatch $summaryPattern) {
        $failures.Add("the last summary line is not PLAN-49's (the container runs a release that does not tell effective versions from history, or does not say how many templates it read)")
    } else {
        $effective = [int]$Matches[1]
        $historic = [int]$Matches[2]
        $templatesRead = [int]$Matches[3]
        if ($effective -ne 0) { $failures.Add("the last start-up lists $effective rule(s) of effective versions the validator refuses (PLAN-49: none may remain)") }
        if ($templatesRead -lt 1) { $failures.Add("the last start-up read no national template: its session was not the national tenant's, so its count of refused rules says nothing") }
        foreach ($line in $lastNational) {
            if ($line -match ' rule \S+: ') { $failures.Add("a rule of an effective version is refused: " + $line.Trim()) }
        }
        Write-Output "Last start-up: $effective rule(s) of effective versions refused, $historic in superseded versions (history), $templatesRead national template(s) read."

        # The agencies' overrides, read apart at the same start-up: its lines, then its own summary.
        $overridePattern = 'agency override inventory: (\d+) rule\(s\) of overrides in force the validator refuses, in (\d+) of (\d+) agency tenant\(s\) read; (\d+) more in superseded versions'
        if ($overrides.Count -gt 0) {
            Write-Output "The agency override inventory (start-up log):"
            $overrides | ForEach-Object { Write-Output ("  " + $_.Trim()) }
        }
        $overrideSummary = @($lastOverride | Where-Object { $_ -match 'agency override inventory: \d+ ' })
        if (@($lastOverride | Where-Object { $_ -match 'could not be taken' }).Count -gt 0) {
            $failures.Add("the agency override inventory could not be taken at the last start-up")
        } elseif (@($lastOverride | Where-Object { $_ -match 'not taken' }).Count -gt 0) {
            $failures.Add("the agencies' overrides were not read at the last start-up (each tenant could not be read in its own session)")
        } elseif ($overrideSummary.Count -eq 0) {
            $failures.Add("no 'PLAN-49 agency override inventory' summary at the last start-up: the agencies' overrides were not read (an older release, or a start-up that did not finish)")
        } elseif ($overrideSummary[$overrideSummary.Count - 1] -notmatch $overridePattern) {
            $failures.Add("the last agency override summary line is not understood")
        } else {
            $overrideCount = [int]$Matches[1]
            $overrideTenants = [int]$Matches[2]
            $tenantsRead = [int]$Matches[3]
            Write-Output "Last start-up: $overrideCount rule(s) of agency overrides in force refused, in $overrideTenants of $tenantsRead agency tenant(s) read."
            if ($overrideCount -ne 0 -and -not $AllowAgencyOverrideFindings) {
                $failures.Add("the last start-up lists $overrideCount rule(s) of agency overrides in force the validator refuses (pass -AllowAgencyOverrideFindings where they are seeded on purpose)")
            }
        }
    }
} elseif ($Container -or $LogFile) {
    $failures.Add("no 'PLAN-47 validator inventory' line in the log (HR.Core writes a summary line at every start-up)")
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output "FAIL: $_" }
    Write-Output "pol047-validate: FAIL ($($failures.Count))"
    exit 1
}
Write-Output "pol047-validate: PASS"
exit 0
