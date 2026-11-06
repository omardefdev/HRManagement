#Requires -Version 5.1
# PLAN-41: the guard of the Acceptance job. Reads every trx file of the run and the run's evidence folders against
# tests/acceptance/ci-scope.json. Exit 1 when:
#   - a required class has a result other than Passed, or did not run at all;
#   - a scenario of a required class is Fail or Blocked in its evidence folder;
#   - a class in a trx is not in the scope file, or is listed as excluded;
#   - a trx says the test run was aborted, or a result.json cannot be read;
#   - PLAN-66: -NotRunByRule names a class the rule of the scope does not name, or a class that ran.
# Results of classes that are not required go to the report only. Writes <OutDir>/summary.md and <OutDir>/guard.json.
param(
    [string]$RepoRoot = "",
    [string]$Scope = "tests/acceptance/ci-scope.json",
    [Parameter(Mandatory)][string]$TrxDir,
    [string]$EvidenceDir = "",
    [string]$OutDir = "",
    # The classes the run was limited to (-Classes of the job); the others are not expected.
    [string]$Classes = "",
    # lab, prodLike or both: the part of the scope that ran.
    [ValidateSet("lab", "prodLike", "both")][string]$Ran = "both",
    # PLAN-66 (S3): the classes the rule of the scope left out of this run: they are not expected, and nothing else may be.
    [string]$NotRunByRule = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
. (Join-Path $PSScriptRoot "acceptance-stage-lib.ps1")

$scopePath = if ([System.IO.Path]::IsPathRooted($Scope)) { $Scope } else { Join-Path $RepoRoot $Scope }
$scopeFile = Read-AcceptanceJson -Path $scopePath
if ($null -eq $scopeFile) { throw "Cannot read the CI scope: $scopePath" }

$listed = @{}
foreach ($part in @("lab", "prodLike")) {
    foreach ($entry in @(Get-AcceptanceProperty $scopeFile $part)) {
        if ($null -eq $entry) { continue }
        $listed[[string]$entry.class] = [pscustomobject]@{ part = $part; required = [bool]$entry.required; group = [string]$entry.group }
    }
}
$excluded = @{}
foreach ($entry in @($scopeFile.excluded)) {
    if ($null -ne $entry) { $excluded[[string]$entry.class] = [string]$entry.reason }
}
$limit = @($Classes -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$notRun = @($NotRunByRule -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$failures = New-Object System.Collections.Generic.List[string]
$byClass = @{}
$ranTests = New-Object System.Collections.Generic.HashSet[string]
function Add-ClassResult([string]$Class, [string]$Test, [string]$Outcome, [double]$Seconds, [string]$Message) {
    $parts = $Test -split '\.'
    if ($parts.Count -ge 2) { [void]$ranTests.Add("$Class.$($parts[-1])") }
    if (-not $byClass.ContainsKey($Class)) {
        $byClass[$Class] = [pscustomobject]@{ class = $Class; passed = 0; failed = 0; other = 0; seconds = 0.0; problems = (New-Object System.Collections.Generic.List[string]) }
    }
    $row = $byClass[$Class]
    $row.seconds += $Seconds
    if ($Outcome -eq "Passed") { $row.passed++ }
    else {
        if ($Outcome -eq "Failed") { $row.failed++ } else { $row.other++ }
        $firstLine = ""
        if ($Message) { $firstLine = ($Message -split "`n")[0].Trim() }
        [void]$row.problems.Add("${Test}: $Outcome $firstLine".Trim())
    }
}

$trxFiles = @()
if (Test-Path -LiteralPath $TrxDir) { $trxFiles = @(Get-ChildItem -LiteralPath $TrxDir -Filter "*.trx" -Recurse) }
foreach ($file in $trxFiles) {
    [xml]$trx = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    # An aborted host keeps the finished tests in the trx and drops the rest, so the class would look complete.
    foreach ($info in @($trx.TestRun.ResultSummary.RunInfos.RunInfo)) {
        if ($null -eq $info) { continue }
        if ([string]$info.outcome -eq "Error" -and [string]$info.Text -match 'test run was aborted|host process crashed') {
            [void]$failures.Add("$($file.Name): the test run did not finish: $(([string]$info.Text -split "`n")[0].Trim())")
        }
    }
    $classOf = @{}
    foreach ($definition in @($trx.TestRun.TestDefinitions.UnitTest)) {
        if ($null -eq $definition) { continue }
        $classOf[[string]$definition.id] = ([string]$definition.TestMethod.className -split '[.+]')[-1]
    }
    foreach ($result in @($trx.TestRun.Results.UnitTestResult)) {
        if ($null -eq $result) { continue }
        $seconds = 0.0
        if ($result.duration) { $seconds = ([timespan]::Parse([string]$result.duration, [System.Globalization.CultureInfo]::InvariantCulture)).TotalSeconds }
        $message = $null
        if ($result.Output -and $result.Output.ErrorInfo) { $message = [string]$result.Output.ErrorInfo.Message }
        Add-ClassResult -Class ([string]$classOf[[string]$result.testId]) -Test ([string]$result.testName) -Outcome ([string]$result.outcome) -Seconds $seconds -Message $message
    }
}

foreach ($method in @($excluded.Keys | Where-Object { $_ -like "*.*" })) {
    if ($ranTests.Contains($method)) { [void]$failures.Add("$method is excluded in the scope and ran.") }
}
foreach ($class in @($byClass.Keys | Sort-Object)) {
    if ($excluded.ContainsKey($class)) { [void]$failures.Add("$class is excluded in the scope and ran.") }
    elseif (-not $listed.ContainsKey($class)) { [void]$failures.Add("$class ran and is not in the scope file: add it to lab[] or prodLike[], or to excluded[] with an owner.") }
    elseif ($listed[$class].required) {
        foreach ($problem in $byClass[$class].problems) { [void]$failures.Add("Required: $problem") }
    }
}

$ruleClasses = @(Get-AcceptanceRuleClasses -ScopeFile $scopeFile)
foreach ($class in $notRun) {
    if ($class -notin $ruleClasses) { [void]$failures.Add("$class is marked not run by rule, and the rule of the scope does not name it.") }
    elseif ($byClass.ContainsKey($class)) { [void]$failures.Add("$class is marked not run by rule, and it ran.") }
}

foreach ($class in @($listed.Keys | Sort-Object)) {
    $entry = $listed[$class]
    if (-not $entry.required -or $byClass.ContainsKey($class)) { continue }
    if ($class -in $notRun -and $class -in $ruleClasses) { continue }
    if ($limit.Count -gt 0 -and $class -notin $limit) { continue }
    if ($Ran -ne "both" -and $entry.part -ne $Ran) { continue }
    [void]$failures.Add("Required: $class did not run.")
}

$open = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace($EvidenceDir) -and (Test-Path -LiteralPath $EvidenceDir)) {
    foreach ($file in @(Get-ChildItem -LiteralPath $EvidenceDir -Filter "result.json" -Recurse)) {
        $result = Read-AcceptanceJson -Path $file.FullName
        if ($null -eq $result) {
            [void]$failures.Add("Required: $($file.FullName.Substring($EvidenceDir.TrimEnd('\', '/').Length + 1)) cannot be read.")
            continue
        }
        $scenarios = Get-AcceptanceProperty $result "scenarios"
        if ($null -eq $scenarios) { continue }
        $folder = $file.Directory.FullName.Substring($EvidenceDir.TrimEnd('\', '/').Length + 1) -replace '\\', '/'
        foreach ($scenario in $scenarios.PSObject.Properties) {
            $status = [string](Get-AcceptanceProperty $scenario.Value "status")
            if ($status -eq "Pass") { continue }
            $class = [string](Get-AcceptanceProperty $scenario.Value "testClass")
            $reason = [string](Get-AcceptanceProperty $scenario.Value "reason")
            $line = "$folder/$($scenario.Name) ($class): $status $reason".Trim()
            if ($listed.ContainsKey($class) -and -not $listed[$class].required) { [void]$open.Add($line) }
            else { [void]$failures.Add("Required: $line") }
        }
    }
}

$lines = New-Object System.Collections.Generic.List[string]
$verdict = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
[void]$lines.Add("# Acceptance job: $verdict")
[void]$lines.Add("")
if ($failures.Count -gt 0) {
    [void]$lines.Add("## Why the job is red")
    [void]$lines.Add("")
    foreach ($failure in $failures) { [void]$lines.Add("- $failure") }
    [void]$lines.Add("")
}
[void]$lines.Add("| Class | Part | Group | Required | Passed | Failed | Other | Seconds |")
[void]$lines.Add("|-------|------|-------|----------|--------|--------|-------|---------|")
foreach ($class in @(@($listed.Keys) + @($byClass.Keys) | Sort-Object -Unique)) {
    $entry = $listed[$class]
    $row = $byClass[$class]
    $part = if ($entry) { $entry.part } else { "NOT LISTED" }
    $group = if ($entry) { $entry.group } else { "-" }
    $required = if ($entry -and $entry.required) { "yes" } else { "no" }
    if ($row) {
        [void]$lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f $class, $part, $group, $required, $row.passed, $row.failed, $row.other, [math]::Round($row.seconds).ToString([System.Globalization.CultureInfo]::InvariantCulture)))
    }
    else {
        [void]$lines.Add(("| {0} | {1} | {2} | {3} | - | - | - | - |" -f $class, $part, $group, $required))
    }
}
$notRequired = @($byClass.Values | Where-Object { $listed.ContainsKey($_.class) -and -not $listed[$_.class].required -and $_.problems.Count -gt 0 })
if ($notRequired.Count -gt 0 -or $open.Count -gt 0) {
    [void]$lines.Add("")
    [void]$lines.Add("## Not required, shown only")
    [void]$lines.Add("")
    foreach ($row in $notRequired) { foreach ($problem in $row.problems) { [void]$lines.Add("- $problem") } }
    foreach ($line in $open) { [void]$lines.Add("- $line") }
}
if ($notRun.Count -gt 0) {
    [void]$lines.Add("")
    [void]$lines.Add("## Not run by rule")
    [void]$lines.Add("")
    foreach ($class in $notRun) { [void]$lines.Add("- $class") }
}
[void]$lines.Add("")
[void]$lines.Add("Excluded classes: $($excluded.Count) (tests/acceptance/ci-scope.json, each with a reason, an owner and a review date).")

if (-not [string]::IsNullOrWhiteSpace($OutDir)) {
    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText((Join-Path $OutDir "summary.md"), (($lines -join "`n") + "`n"), $utf8NoBom)
    $report = [ordered]@{ verdict = $verdict; failures = @($failures); trxFiles = $trxFiles.Count; classes = $byClass.Count }
    [System.IO.File]::WriteAllText((Join-Path $OutDir "guard.json"), (($report | ConvertTo-Json -Depth 4) + "`n"), $utf8NoBom)
}

if ($failures.Count -gt 0) {
    Write-Host "acceptance-guard: FAIL ($($failures.Count))"
    foreach ($failure in $failures) { Write-Host "  - $failure" }
    exit 1
}
Write-Host "acceptance-guard: PASS ($($byClass.Count) classes in $($trxFiles.Count) trx files)"
exit 0
