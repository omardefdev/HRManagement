# PLAN-47: the policy rule lifecycle, checked.
#  1. Static: no in-place seed writer and no same-actor publish flag in the product code; the validator's messages are
#     Arabic; the immutability trigger is in the migration; the two roles are in the realm manifest; the change sets exist.
#  2. Inventory (optional, -Container or -LogFile): every published rule the validator refuses, as HR.Core logged it at
#     start-up ("PLAN-47 validator inventory"). A text inventory: nothing is fixed in place (a fix is a new change set).
# Usage (PowerShell 5.1):
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-validate.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol047-validate.ps1 -Container hrm-acceptance-hr-core-web-1 -Out artifacts/plan47/validator-inventory.txt
# Server (bash): docker logs hrm-hr-core-web 2>&1 | grep 'PLAN-47 validator inventory'
param(
    [string]$Container = "",
    [string]$LogFile = "",
    [string]$Out = ""
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
foreach ($id in @("POL-2026-10-30-BASELINE", "POL-2026-10-30-RETIRE-R-REC-HIRE")) {
    if (-not $sets.Contains($id)) { $failures.Add("change set $id is missing") }
}

# 2. The inventory of published rules the validator refuses, from the start-up log.
$inventory = @()
if (-not [string]::IsNullOrWhiteSpace($Container)) {
    $log = & docker logs $Container 2>&1 | Out-String
    $inventory = @($log -split "`r?`n" | Where-Object { $_ -match 'PLAN-47 validator inventory' })
} elseif (-not [string]::IsNullOrWhiteSpace($LogFile)) {
    $inventory = @([System.IO.File]::ReadAllLines((Resolve-Path $LogFile)) | Where-Object { $_ -match 'PLAN-47 validator inventory' })
}
if ($inventory.Count -gt 0) {
    Write-Output "Published rules the validator refuses (start-up log):"
    $inventory | ForEach-Object { Write-Output ("  " + $_.Trim()) }
    if (-not [string]::IsNullOrWhiteSpace($Out)) {
        $dir = Split-Path -Parent $Out
        if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        [System.IO.File]::WriteAllLines($Out, [string[]]$inventory, (New-Object System.Text.UTF8Encoding($false)))
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
