#Requires -Version 5.1
# PLAN-66: assembles the results of the stage jobs into the layout a sequential run of acceptance-job.ps1 writes:
#   <RunDir>/acceptance-evidence/  <RunDir>/TestResults/  <RunDir>/logs/
# The job script then runs the summary, the validator and the guard on it, unchanged. Stages join in the order of the scope, so
# the last writer of a file is the one a sequential run would have left.
#
# Returns an object: failures (each a sentence; none means every expected stage came, for this code, and ended clean) and stages
# (what each stage reported, with its timing). A stage is expected unless the run was narrowed and it holds none of the classes.
# ASCII only; Windows PowerShell 5.1 and PowerShell 7.
param(
    [string]$RepoRoot = "",
    [string]$Scope = "tests/acceptance/ci-scope.json",
    [Parameter(Mandatory)][string]$StagesDir,
    [Parameter(Mandatory)][string]$RunDir,
    [Parameter(Mandatory)][string]$SourceSha,
    [Parameter(Mandatory)][string]$CodeTree,
    # The classes of a narrowed run, comma-separated.
    [string]$Limit = "",
    [ValidateSet("lab", "prodLike", "both")][string]$Ran = "both",
    # Tests: the returned object as JSON.
    [string]$ReportFile = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
. (Join-Path $PSScriptRoot "acceptance-stage-lib.ps1")

$scopePath = if ([System.IO.Path]::IsPathRooted($Scope)) { $Scope } else { Join-Path $RepoRoot $Scope }
$scopeFile = Read-AcceptanceJson -Path $scopePath
if ($null -eq $scopeFile) { throw "Cannot read the CI scope: $scopePath" }
$limitNames = @($Limit -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

$evidenceDir = Join-Path $RunDir "acceptance-evidence"
$trxDir = Join-Path $RunDir "TestResults"
$logDir = Join-Path $RunDir "logs"
foreach ($dir in @($evidenceDir, $trxDir, $logDir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$failures = New-Object System.Collections.Generic.List[string]
$stages = New-Object System.Collections.Generic.List[object]
$ruleSkipped = [string]$env:ACCEPTANCE_META_RULE -eq "skip"
$ruleStage = [string](Get-AcceptanceProperty (Get-AcceptanceProperty $scopeFile "metaRule") "stage")

foreach ($job in @(Get-AcceptanceStageJobs -ScopeFile $scopeFile)) {
    $name = [string]$job.name
    $selection = @(Get-AcceptanceStageSelection -ScopeFile $scopeFile -Stage (@($job.stages) -join ","))
    $inPart = @($selection | Where-Object { $Ran -eq "both" -or [string]$_.part -eq $Ran })
    $classes = @(Get-AcceptanceStageClasses -ScopeFile $scopeFile -Selection $inPart -Limit $limitNames)
    $byRule = $ruleSkipped -and @($job.stages).Count -eq 1 -and [string]@($job.stages)[0] -eq $ruleStage
    $expected = $classes.Count -gt 0 -and -not $byRule

    $folder = Join-Path $StagesDir $name
    $report = Read-AcceptanceJson -Path (Join-Path $folder "stage.json")
    if ($null -eq $report) {
        if ($expected) { [void]$failures.Add("Stage ${name} left no result.") }
        continue
    }

    if ([string]$report.sourceSha -ne $SourceSha) { [void]$failures.Add("Stage ${name} ran on $($report.sourceSha), not on $SourceSha.") }
    if ([string]$report.codeTree -ne $CodeTree) { [void]$failures.Add("Stage ${name} ran on another code tree.") }
    $wanted = (@($job.stages) | Sort-Object) -join ","
    $got = (@($report.stages) | Sort-Object) -join ","
    if ($wanted -ne $got) { [void]$failures.Add("Stage ${name} ran $got, the scope gives it $wanted.") }
    foreach ($failure in @($report.failures)) { if ($failure) { [void]$failures.Add("Stage ${name}: $failure") } }

    Merge-AcceptanceEvidenceFolder -Source (Join-Path $folder "acceptance-evidence") -Target $evidenceDir
    foreach ($kind in @(@("TestResults", $trxDir), @("logs", $logDir))) {
        $source = Join-Path $folder $kind[0]
        if (Test-Path -LiteralPath $source) {
            foreach ($file in @(Get-ChildItem -LiteralPath $source -File)) {
                $target = Join-Path $kind[1] $file.Name
                # Each stage job has its own runner: the stack reports (stats-lab.txt and the like) keep the stage in their name.
                if ((Test-Path -LiteralPath $target) -and $kind[0] -eq "logs") { $target = Join-Path $kind[1] "$name-$($file.Name)" }
                Copy-Item -LiteralPath $file.FullName -Destination $target -Force
            }
        }
    }
    $timing = Read-AcceptanceJson -Path (Join-Path $folder "job-timing.json")
    [void]$stages.Add([ordered]@{ name = $name; stages = @($report.stages); steps = @(Get-AcceptanceProperty $timing "steps") })
}

$result = New-Object psobject -Property @{ failures = $failures.ToArray(); stages = $stages.ToArray() }
if (-not [string]::IsNullOrWhiteSpace($ReportFile)) { Write-AcceptanceJsonFile -Path $ReportFile -Value $result }
return $result
