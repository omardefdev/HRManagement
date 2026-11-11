#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-49 (AC-POL-49a, G-POL-02): a service refuses to start when a policy domain that is out of the engine's scope is given
  a mode other than Off.

.DESCRIPTION
  Starts one more container of a service of the running acceptance stack (docker compose run --no-deps) with
  PolicyEngine__<Domain>=<Mode> added to its environment, waits for it to exit, reads its exit code and its output, and
  removes it. The start-up check runs before the migrations and the seeds, so that container never touches the database;
  the running service of the stack is not stopped, recreated or changed.

  PASS (exit 0): the container exited with a code other than 0, and its output names the key and says that the domain is out
  of the policy engine's scope.
  FAIL (exit 1): the container started (it was still running after -TimeoutSeconds), or it exited for another reason.

  ASCII only. Runs under Windows PowerShell 5.1 and under PowerShell 7.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol049-outofscope-mode-boot-check.ps1
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/pol049-outofscope-mode-boot-check.ps1 -Service idcard-web -Domain Payroll.Period -Mode Enforce
#>
param(
    [string]$Service = "hr-core-web",
    [string]$Domain = "IT.Tickets",
    [ValidateSet("Shadow", "Enforce")][string]$Mode = "Shadow",
    [string]$Project = "hrm-acceptance",
    # The compose chain of the running stack; the default is the prod-like acceptance chain.
    [string[]]$ComposeFile = @(
        "docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml",
        "docker-compose.full-stack-gateway.yml", "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml"),
    [int]$TimeoutSeconds = 180,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent $PSScriptRoot }

# The compose files read the acceptance secrets from the environment. A caller that started the stack has them already
# (scripts/ci/acceptance-job.ps1, the acceptance tests); a person who runs this alone gets them from the git-ignored files.
if ([string]::IsNullOrWhiteSpace($env:ACCEPTANCE_SVC_CLIENT_SECRET) -and (Test-Path -LiteralPath (Join-Path $RepoRoot ".acceptance/credentials.json"))) {
    . (Join-Path $PSScriptRoot "acceptance-local-env.ps1")
    Import-AcceptanceLocalEnvironment -RepoRoot $RepoRoot
}

# docker through System.Diagnostics.Process: Windows PowerShell 5.1 turns the stderr of a native command into an error
# record, and the output of a container that refuses to start is on both streams.
function Invoke-Docker {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $quoted = $Arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $info = New-Object System.Diagnostics.ProcessStartInfo
    $info.FileName = "docker"
    $info.Arguments = ($quoted -join " ")
    $info.WorkingDirectory = $RepoRoot
    $info.UseShellExecute = $false
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    # The services log UTF-8 (their start-up refusal begins in Arabic); the default here would be the console's code page.
    $info.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [System.Text.Encoding]::UTF8
    $process = [System.Diagnostics.Process]::Start($info)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = ($stdout.Result + $stderr.Result) }
}

function ConvertTo-AsciiLine {
    param([string]$Text)
    return ([regex]::Replace([string]$Text, '[^ -~]', '?')).TrimEnd()
}

$key = "PolicyEngine:$Domain"
$name = "pol049-boot-" + [guid]::NewGuid().ToString("N").Substring(0, 12)
$arguments = @("compose", "-p", $Project)
foreach ($file in $ComposeFile) { $arguments += @("-f", $file) }
$arguments += @("--profile", "full-stack", "run", "-d", "--no-deps", "--name", $name, "-e", "PolicyEngine__$Domain=$Mode", $Service)

Write-Host "pol049-outofscope-mode-boot-check: one-off $Service with $key=$Mode (project $Project)"
$failure = $null
$exitCode = $null
$logs = ""
try {
    $started = Invoke-Docker -Arguments $arguments
    if ($started.ExitCode -ne 0) { throw "docker compose run did not start the container: $($started.Output)" }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $state = (Invoke-Docker -Arguments @("inspect", "-f", "{{.State.Status}} {{.State.ExitCode}}", $name)).Output.Trim() -split '\s+'
        if ($state.Count -ge 2 -and $state[0] -eq "exited") {
            $exitCode = [int]$state[1]
            break
        }
        Start-Sleep -Seconds 2
    }

    $logs = (Invoke-Docker -Arguments @("logs", $name)).Output
    if ($null -eq $exitCode) {
        $failure = "the container was still running after $TimeoutSeconds s: $Service started with $key=$Mode"
    }
    elseif ($exitCode -eq 0) {
        $failure = "the container exited with code 0"
    }
    elseif ($logs.IndexOf("$key must be Off", [System.StringComparison]::Ordinal) -lt 0) {
        $failure = "the container exited with code $exitCode, but its output does not say that $key must be Off"
    }
    elseif ($logs.IndexOf("out of the policy engine's scope", [System.StringComparison]::Ordinal) -lt 0) {
        $failure = "the container exited with code $exitCode, but its output does not say that the domain is out of the policy engine's scope"
    }
}
catch {
    $failure = $_.Exception.Message
}
finally {
    [void](Invoke-Docker -Arguments @("rm", "-f", $name))
}

if ($failure) {
    Write-Host "pol049-outofscope-mode-boot-check: FAIL: $failure"
    if ($logs) {
        $tail = @($logs -split "`n" | Where-Object { $_.Trim() }) | Select-Object -Last 15
        foreach ($line in $tail) { Write-Host "  | $(ConvertTo-AsciiLine $line)" }
    }
    exit 1
}

# The line that refused, from its English half on (this script and what reads its output stay ASCII).
$line = [string](@($logs -split "`n" | Where-Object { $_.IndexOf("$key must be Off", [System.StringComparison]::Ordinal) -ge 0 }) | Select-Object -First 1)
$marker = "PolicyEngine configuration is invalid"
$at = $line.IndexOf($marker, [System.StringComparison]::Ordinal)
if ($at -ge 0) { $line = $line.Substring($at) }
Write-Host "pol049-outofscope-mode-boot-check: PASS (exit code $exitCode)"
Write-Host "  | $(ConvertTo-AsciiLine $line)"
exit 0
