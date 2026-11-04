#Requires -Version 5.1
<#
.SYNOPSIS
  PLAN-41: the Acceptance job. One source for CI and for the owner: it runs under Windows PowerShell 5.1 on the owner's
  machine and under PowerShell 7 on the Linux runner. ASCII only.

.DESCRIPTION
  Runs tests/acceptance/ci-scope.json stage by stage, in its "order": each stage names a part (lab or prodLike), a group and
  the stack it needs. The job defines no stack of its own: lab and prodlike are scripts/acceptance-up.ps1.
  Every run writes under artifacts/acceptance/run/ (acceptance-evidence/, TestResults/, logs/, summary.md, job-timing.json) and
  never into the committed acceptance-evidence/ (D-QA-20): the evidence folders not rewritten by the run are copied in
  unchanged at the end, so the artifact is a whole folder to replace the committed one with.

  Exit 1 when the guard (scripts/ci/acceptance-guard.ps1) or the baseline validator fails, or a stack does not start.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile Both
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ci/acceptance-job.ps1 -Profile Lab -Classes AcHcm29HireContractTests
#>
param(
    [ValidateSet("Lab", "ProdLike", "Both")][string]$Profile = "Both",
    [string]$Scope = "tests/acceptance/ci-scope.json",
    # The commit under test; the default is HEAD.
    [string]$SourceSha = "",
    # Comma-separated class names: only these run (the owner's retry, or the negative proof of AC-QA-41a).
    [string]$Classes = "",
    # Reuse the images of the last build (a local retry of the tests only).
    [switch]$NoBuild,
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
Set-Location $RepoRoot
$scripts = Join-Path $RepoRoot "scripts"
. (Join-Path $scripts "acceptance-evidence-lib.ps1")
. (Join-Path $scripts "acceptance-local-env.ps1")

$onWindows = $env:OS -eq "Windows_NT"
$inCi = $env:GITHUB_ACTIONS -eq "true"
$hasPwsh7 = $PSVersionTable.PSVersion.Major -ge 7
$acceptanceProject = "src/Tests/HRManagement.AcceptanceTests/HRManagement.AcceptanceTests.csproj"
$leastPrivFiles = @("docker-compose.yml", "docker-compose.services.yml", "docker-compose.dev-profiles.yml",
    "docker-compose.full-stack-gateway.yml", "docker-compose.acceptance.yml", "docker-compose.acceptance-prodlike.yml",
    "docker-compose.acceptance-leastpriv.yml")

# The negative proof of AC-QA-41a: the lab profile only, on the class of the simulated scenario unless -Classes names others.
$simulated = [string]$env:ACCEPTANCE_SIMULATE_MISSING
if (-not [string]::IsNullOrWhiteSpace($simulated)) {
    $Profile = "Lab"
    if ([string]::IsNullOrWhiteSpace($Classes)) { $Classes = ($simulated.Trim() -split '\.')[0] }
}
if ([string]::IsNullOrWhiteSpace($Classes) -and -not [string]::IsNullOrWhiteSpace($env:ACCEPTANCE_CLASSES)) { $Classes = $env:ACCEPTANCE_CLASSES }
$limit = @($Classes -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })

if ([string]::IsNullOrWhiteSpace($SourceSha)) { $SourceSha = (git rev-parse HEAD).Trim() }
$codeTree = Get-AcceptanceCodeTree -RepoRoot $RepoRoot -Commit $SourceSha
# As acceptance-up.ps1: a changed tracked file marks the images, so they never count as built from the commit.
git diff --quiet HEAD --
$imageRevision = if ($LASTEXITCODE -eq 0) { $SourceSha } else { "$SourceSha-dirty" }

$runDir = Join-Path (Join-Path (Join-Path $RepoRoot "artifacts") "acceptance") "run"
if (Test-Path -LiteralPath $runDir) { Remove-Item -LiteralPath $runDir -Recurse -Force }
$evidenceDir = Join-Path $runDir "acceptance-evidence"
$trxDir = Join-Path $runDir "TestResults"
$logDir = Join-Path $runDir "logs"
foreach ($dir in @($evidenceDir, $trxDir, $logDir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }

$env:RUN_ACCEPTANCE = "true"
$env:ACCEPTANCE_REQUIRE_ALL = "true"
$env:RUN_PLAYWRIGHT_E2E = "true"
$env:ACCEPTANCE_EVIDENCE_DIR = $evidenceDir
$env:ACCEPTANCE_SOURCE_SHA = $SourceSha
$env:ACCEPTANCE_CODE_TREE = $codeTree
$env:GATEWAY_BASE_URL = "http://localhost:6200"
$env:KEYCLOAK_BASE_URL = "http://localhost:6388"
$env:COMPOSE_PROJECT_NAME = "hrm-acceptance"
$env:POSTGRES_HOST = "localhost"
$env:POSTGRES_PORT = "6433"

$timing = New-Object System.Collections.Generic.List[object]
function Measure-Step {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Body)
    $start = Get-Date
    Write-Host "== $Name =="
    if ($inCi) { Write-Host "::group::$Name" }
    try {
        & $Body
    }
    finally {
        if ($inCi) { Write-Host "::endgroup::" }
        $seconds = [math]::Round(((Get-Date) - $start).TotalSeconds)
        [void]$timing.Add([ordered]@{ step = $Name; seconds = $seconds })
        Write-Host "== $Name took $seconds s =="
    }
}

# Runs a script in its own process, so one that sets $ErrorActionPreference or exits cannot end this one. It throws on a
# failure; a stack that does not start ends the job.
function Invoke-RepoScript {
    param([Parameter(Mandatory)][string]$Script, [string[]]$Arguments = @())
    $shell = if ($hasPwsh7) { "pwsh" } else { "powershell" }
    $path = Join-Path $RepoRoot $Script
    & $shell -NoProfile -ExecutionPolicy Bypass -File $path @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Script failed with exit code $LASTEXITCODE" }
}

function Import-StackEnvironment {
    Import-AcceptanceLocalEnvironment -RepoRoot $RepoRoot
    foreach ($name in @("KEYCLOAK_HR_GATEWAY_CLIENT_SECRET", "POSTGRES_USER", "POSTGRES_PASSWORD", "KEYCLOAK_ADMIN", "KEYCLOAK_ADMIN_PASSWORD")) {
        $value = Get-HrmDotEnvValue -RepoRoot $RepoRoot -Name $name
        if ($value) { Set-Item -Path "Env:$name" -Value $value }
    }
}

function Write-StackReport {
    param([Parameter(Mandatory)][string]$Name)
    # P-8: what the stack takes on the runner.
    $ErrorActionPreference = "Continue"
    docker stats --no-stream --format "{{.Name}} {{.MemUsage}} {{.CPUPerc}}" > (Join-Path $logDir "stats-$Name.txt") 2>&1
    docker system df > (Join-Path $logDir "docker-df-$Name.txt") 2>&1
    if (-not $onWindows) {
        df -h / > (Join-Path $logDir "disk-$Name.txt") 2>&1
        free -m > (Join-Path $logDir "memory-$Name.txt") 2>&1
    }
}

function Wait-Gateway {
    param([string]$Url = "http://localhost:6200/health/live", [int]$Attempts = 60)
    for ($i = 1; $i -le $Attempts; $i++) {
        try {
            $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 5
            if ([int]$response.StatusCode -eq 200) { return }
        }
        catch {
            Start-Sleep -Seconds 3
        }
    }
    throw "The gateway did not answer $Url."
}

# The compose child must not see the secret-looking variables of this process: they would win over --env-file.
function Invoke-LeastPrivCompose {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $saved = @{}
    foreach ($item in @(Get-ChildItem Env: | Where-Object { $_.Name -match '(SECRET|PASSWORD|_PASS|TOKEN)$' })) {
        $saved[$item.Name] = $item.Value
        Remove-Item -Path "Env:$($item.Name)"
    }
    try {
        $fileArgs = $leastPrivFiles | ForEach-Object { "-f", $_ }
        docker compose -p hrm-leastpriv --env-file .env.leastpriv @fileArgs --profile full-stack @Arguments
        if ($LASTEXITCODE -ne 0) { throw "docker compose (hrm-leastpriv) $($Arguments -join ' ') failed with exit code $LASTEXITCODE" }
    }
    finally {
        foreach ($name in $saved.Keys) { Set-Item -Path "Env:$name" -Value $saved[$name] }
    }
}

$script:currentStack = "none"
$script:imagesBuilt = [bool]$NoBuild

function Stop-CurrentStack {
    switch ($script:currentStack) {
        { $_ -in @("lab", "prodlike") } { Invoke-RepoScript -Script "scripts/acceptance-reset.ps1" }
        "leastpriv" { Invoke-LeastPrivCompose -Arguments @("down", "--volumes", "--remove-orphans") }
        "base" {
            docker compose -f docker-compose.yml -f docker-compose.services.yml --profile full-stack down -v --remove-orphans
        }
    }
    $script:currentStack = "none"
}

function Start-AcceptanceStack {
    param([Parameter(Mandatory)][ValidateSet("Lab", "ProdLike")][string]$Name)
    $arguments = @("-Profile", $Name, "-SourceSha", $SourceSha)
    if ($script:imagesBuilt) { $arguments += "-NoBuild" }
    # Every stack of this job starts from empty volumes; the verification that says so needs PowerShell 7.
    if ($hasPwsh7) { $arguments += "-RequireCleanBaseline" }
    # acceptance-verify.ps1 needs PowerShell 7, which the owner's machine does not have.
    if (-not $hasPwsh7) { $arguments += "-SkipLiveVerification" }
    Invoke-RepoScript -Script "scripts/acceptance-up.ps1" -Arguments $arguments
    $verification = Join-Path (Join-Path (Join-Path $RepoRoot "artifacts") "acceptance") "verification"
    if ($hasPwsh7 -and (Test-Path -LiteralPath $verification)) {
        Get-ChildItem -LiteralPath $verification -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $logDir "$Name-$($_.Name)") }
    }
    $script:imagesBuilt = $true
    $env:COMPOSE_PROJECT_NAME = "hrm-acceptance"
    $env:GATEWAY_BASE_URL = "http://localhost:6200"
    Import-StackEnvironment
    Wait-Gateway
}

function Use-Stack {
    param([Parameter(Mandatory)][string]$Stack)
    if ($Stack -eq $script:currentStack) { return }
    Measure-Step "Stack $Stack" {
        Stop-CurrentStack
        Remove-Item -Path Env:ACCEPTANCE_COMPOSE_PROJECT -ErrorAction SilentlyContinue
        switch ($Stack) {
            "lab" {
                Start-AcceptanceStack -Name Lab
                # Plan step 3: the per-service roles are applied by acceptance-up.ps1; this proves each role stays in its
                # own database and leaves the files AC-20 reads. AC-20 is not required, so a failure shows and goes on.
                try { Invoke-RepoScript -Script "scripts/acceptance-verify-db-isolation.ps1" }
                catch { Write-Host "::warning::DB isolation script: $($_.Exception.Message)" }
            }
            "prodlike" { Start-AcceptanceStack -Name ProdLike }
            "leastpriv" {
                # As PLAN-38: the developer .env, the caller-proof secret, then every role and broker password generated here.
                Import-StackEnvironment
                $lines = @(Get-Content -LiteralPath (Join-Path $RepoRoot ".env")) + "ACCEPTANCE_GATEWAY_PROOF_PRIVATE_KEY_B64=$env:ACCEPTANCE_GATEWAY_PROOF_PRIVATE_KEY_B64" + "ACCEPTANCE_GATEWAY_PROOF_PUBLIC_KEY_B64=$env:ACCEPTANCE_GATEWAY_PROOF_PUBLIC_KEY_B64" + "ACCEPTANCE_SVC_CLIENT_SECRET=$env:ACCEPTANCE_SVC_CLIENT_SECRET"
                [System.IO.File]::WriteAllText((Join-Path $RepoRoot ".env.leastpriv"), (($lines -join "`n") + "`n"), (New-Object System.Text.UTF8Encoding($false)))
                $global:LASTEXITCODE = 0
                & (Join-Path (Join-Path $scripts "vps") "init-secrets.ps1") -EnvFile ".env.leastpriv" -Example "" -ComposeFile $leastPrivFiles
                if ($LASTEXITCODE -ne 0) { throw "init-secrets.ps1 failed with exit code $LASTEXITCODE" }
                # COPY --exclude needs Dockerfile frontend 1.19; the runner's Docker has an older one built in (run 41899641329).
                # The argument picks the frontend here, so the Dockerfile the demo server builds stays as it is.
                docker build -f docker/Migrator.Dockerfile -t hr-migrate:local --build-arg BUILDKIT_SYNTAX=docker/dockerfile:1.19 --build-arg "SOURCE_SHA=$imageRevision" .
                if ($LASTEXITCODE -ne 0) { throw "hr-migrate build failed with exit code $LASTEXITCODE" }
                # The images the other stages tested, under this project's names: a second build would only repeat them.
                $env:SOURCE_SHA = $imageRevision
                $tested = @(docker images --format "{{.Repository}}" --filter "reference=hrm-acceptance-*" | Sort-Object -Unique)
                if ($tested.Count -eq 0) { throw "No hrm-acceptance image to reuse for hrm-leastpriv." }
                foreach ($image in $tested) {
                    docker tag "${image}:latest" ($image -replace '^hrm-acceptance-', 'hrm-leastpriv-')
                    if ($LASTEXITCODE -ne 0) { throw "docker tag $image failed with exit code $LASTEXITCODE" }
                }
                Invoke-LeastPrivCompose -Arguments @("up", "-d", "--wait", "--no-build")
                $env:ACCEPTANCE_COMPOSE_PROJECT = "hrm-leastpriv"
                Wait-Gateway
            }
            "base" {
                # AC-QA-34g: the stack of the E2E job. BuildKit reuses the layers of the acceptance images.
                Remove-Item -Path Env:COMPOSE_PROJECT_NAME -ErrorAction SilentlyContinue
                $env:SOURCE_SHA = $imageRevision
                bash scripts/prepare-ci-compose-env.sh
                if ($LASTEXITCODE -ne 0) { throw "prepare-ci-compose-env.sh failed with exit code $LASTEXITCODE" }
                bash scripts/ci/compose-up-retry.sh -f docker-compose.yml -f docker-compose.services.yml --profile full-stack
                if ($LASTEXITCODE -ne 0) { throw "the base stack did not start" }
                $env:ACQA34_BASE_STACK_URL = "http://localhost:5000"
                Wait-Gateway -Url "http://localhost:5000/health/live"
            }
        }
        $script:currentStack = $Stack
        if ($Stack -ne "none") { Write-StackReport -Name $Stack }
    }
}

# AC-QA-34 reads the results of the CI job of the same commit, which runs beside this job.
function Get-CiJobResults {
    if (-not $inCi) { return }
    Measure-Step "Wait for the results of the CI job" {
        $repo = $env:GITHUB_REPOSITORY
        $found = $false
        for ($i = 1; $i -le 120; $i++) {
            $names = gh api "repos/$repo/actions/runs/$env:GITHUB_RUN_ID/artifacts" --jq '.artifacts[].name'
            if (@($names) -contains "test-results") { $found = $true; break }
            Start-Sleep -Seconds 30
        }
        if (-not $found) { Write-Host "::warning::The CI job left no test-results artifact; AC-QA-34 will report it."; return }
        $download = Join-Path (Join-Path (Join-Path $RepoRoot "artifacts") "ci-local") "download"
        gh run download $env:GITHUB_RUN_ID --repo $repo --name test-results --dir $download
        if ($LASTEXITCODE -ne 0) { throw "Could not download the test-results artifact." }
        $head = Join-Path (Join-Path (Join-Path $RepoRoot "artifacts") "ci-local") "head"
        New-Item -ItemType Directory -Force -Path $head | Out-Null
        $summary = Join-Path (Join-Path (Join-Path $download "artifacts") "ci-local") "summary.json"
        Copy-Item -LiteralPath $summary -Destination (Join-Path $head "summary.json")
        Copy-Item -LiteralPath $summary -Destination (Join-Path (Join-Path (Join-Path $RepoRoot "artifacts") "ci-local") "summary.json")
        $results = Join-Path $head "TestResults"
        New-Item -ItemType Directory -Force -Path $results | Out-Null
        Get-ChildItem -LiteralPath $download -Filter "*.trx" -Recurse | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $results }
    }
}

function Invoke-TestRun {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][string]$Project, [Parameter(Mandatory)][object[]]$Entries)
    $parts = @($Entries | ForEach-Object { "FullyQualifiedName~.$($_.class)." })
    $filter = "(" + ($parts -join '|') + ")"
    # An entry of excluded[] may name one method: Class.Method.
    foreach ($exclusion in @($scopeFile.excluded | Where-Object { [string]$_.class -like "*.*" })) {
        $filter += "&FullyQualifiedName!~.$($exclusion.class)"
    }
    Measure-Step "Tests $Name" {
        $ErrorActionPreference = "Continue"
        dotnet test $Project --configuration Release --no-build --filter $filter `
            --logger "trx;LogFileName=$Name.trx" --results-directory $trxDir --blame-hang-timeout 20m --blame-hang-dump-type none 2>&1 |
            Tee-Object -FilePath (Join-Path $logDir "$Name.log") | Select-String -Pattern '^\s*(Failed|Passed!|Failed!|Skipped) ' | ForEach-Object { Write-Host $_.Line }
        if (-not (Test-Path -LiteralPath (Join-Path $trxDir "$Name.trx"))) { [void]$jobFailures.Add("Tests ${Name}: dotnet test left no trx.") }
    }
}

$scopePath = if ([System.IO.Path]::IsPathRooted($Scope)) { $Scope } else { Join-Path $RepoRoot $Scope }
$scopeFile = Read-AcceptanceJson -Path $scopePath
if ($null -eq $scopeFile) { throw "Cannot read the CI scope: $scopePath" }

$ran = switch ($Profile) { "Lab" { "lab" } "ProdLike" { "prodLike" } default { "both" } }
$skippedHere = New-Object System.Collections.Generic.List[string]
$jobFailures = New-Object System.Collections.Generic.List[string]
$piiScanned = $false

if ($limit.Count -gt 0) {
    $inThisRun = @(@("lab", "prodLike") | Where-Object { $ran -eq "both" -or $_ -eq $ran } | ForEach-Object { @(Get-AcceptanceProperty $scopeFile $_) } | ForEach-Object { [string]$_.class })
    foreach ($name in $limit) {
        if ($name -notin $inThisRun) { [void]$jobFailures.Add("-Classes names $name, which is not in the part of the scope this run covers ($ran).") }
    }
    Write-Host "::warning::Narrowed run: only $($limit -join ', ') ($ran). It says nothing about the other required classes."
    # A green narrowed run on a branch would stand as that commit's Acceptance check. Reruns for a diagnosis stay possible.
    if ($inCi -and [string]$env:GITHUB_REF -ne "refs/heads/main") {
        [void]$jobFailures.Add("A narrowed run is red outside main: it cannot stand as the Acceptance check of $env:GITHUB_REF.")
    }
}

function Merge-CommittedEvidence {
    # D-QA-20: the folders this run did not rewrite stay as they are committed, so their states stay honest (Stale, NotRun).
    $committed = Join-Path $RepoRoot "acceptance-evidence"
    Get-ChildItem -LiteralPath $committed -Directory | ForEach-Object {
        $target = Join-Path $evidenceDir $_.Name
        if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse }
        else {
            # A folder inside a rewritten one (AC-OPS-39/vps: evidence of the demo server, which CI cannot recreate).
            foreach ($nested in @(Get-ChildItem -LiteralPath $_.FullName -Directory | Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName "result.json") })) {
                $nestedTarget = Join-Path $target $nested.Name
                if (-not (Test-Path -LiteralPath $nestedTarget)) { Copy-Item -LiteralPath $nested.FullName -Destination $nestedTarget -Recurse }
            }
        }
    }
}

function Invoke-Summary {
    Invoke-RepoScript -Script "scripts/acceptance-evidence-summarize.ps1" -Arguments @("-EvidenceDir", $evidenceDir, "-Head", $SourceSha)
}

try {
    Measure-Step "Prepare" {
        if (-not $onWindows) {
            bash scripts/prepare-ci-compose-env.sh
            if ($LASTEXITCODE -ne 0) { throw "prepare-ci-compose-env.sh failed with exit code $LASTEXITCODE" }
        }
        Invoke-RepoScript -Script "scripts/acceptance-reset.ps1"
        # An interrupted run can leave this stack on the acceptance ports.
        docker compose -p hrm-leastpriv down --volumes --remove-orphans
    }
    Measure-Step "Build the test projects" {
        dotnet build src/Tests/HRManagement.E2E/HRManagement.E2E.csproj --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "The E2E project did not build." }
        dotnet build $acceptanceProject --configuration Release
        if ($LASTEXITCODE -ne 0) { throw "The acceptance project did not build." }
    }

    foreach ($stage in @($scopeFile.order)) {
        if ($ran -ne "both" -and [string]$stage.part -ne $ran) { continue }
        $entries = @(Get-AcceptanceProperty $scopeFile ([string]$stage.part)) | Where-Object { [string]$_.group -eq [string]$stage.group }
        if ($limit.Count -gt 0) { $entries = @($entries | Where-Object { [string]$_.class -in $limit }) }
        $entries = @($entries)
        if ($entries.Count -eq 0) { continue }

        $label = "$($stage.part)-$($stage.group)"
        if ([bool](Get-AcceptanceProperty $stage "ciOnly") -and -not $inCi) {
            foreach ($entry in $entries) { [void]$skippedHere.Add([string]$entry.class) }
            Write-Host "== $label needs the results of the CI job: it runs in CI only =="
            continue
        }

        $stageProfile = if ([string]$stage.part -eq "lab") { "lab" } else { "prodlike" }
        $env:ACCEPTANCE_PROFILE = $stageProfile

        # Plan step 6: the PII scan reads the acceptance database and logs, so it runs before the stack goes.
        if (-not $piiScanned -and [string]$stage.stack -notin @("lab", "prodlike") -and $script:currentStack -in @("lab", "prodlike")) {
            Measure-Step "PII scan" {
                try { Invoke-RepoScript -Script "scripts/ac026-scan-pii.ps1" }
                catch { [void]$jobFailures.Add("PII scan: $($_.Exception.Message)") }
            }
            $piiScanned = $true
        }

        Use-Stack -Stack ([string]$stage.stack)
        if ([string]$stage.group -eq "meta") { Get-CiJobResults }

        $projects = @($entries | Group-Object -Property { if (Get-AcceptanceProperty $_ "project") { [string]$_.project } else { $acceptanceProject } })
        foreach ($project in $projects) {
            $short = if ($project.Name -eq $acceptanceProject) { "" } else { "-e2e" }
            if ([string]$stage.group -eq "isolated") {
                # P-7: a class that stops or recreates containers runs alone, in its own test process.
                foreach ($entry in $project.Group) { Invoke-TestRun -Name "$label$short-$($entry.class)" -Project $project.Name -Entries @($entry) }
            }
            else {
                Invoke-TestRun -Name "$label$short" -Project $project.Name -Entries @($project.Group)
            }
        }
    }

    if (-not $piiScanned -and $script:currentStack -in @("lab", "prodlike")) {
        Measure-Step "PII scan" {
            try { Invoke-RepoScript -Script "scripts/ac026-scan-pii.ps1" }
            catch { [void]$jobFailures.Add("PII scan: $($_.Exception.Message)") }
        }
    }
}
catch {
    [void]$jobFailures.Add("The job stopped: $($_.Exception.Message)")
    Write-Host "::error::$($_.Exception.Message)"
}

Merge-CommittedEvidence
Invoke-Summary

# P-4: the full validator on the run's own summary.
$shell = if ($hasPwsh7) { "pwsh" } else { "powershell" }
& $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scripts "acceptance-baseline-validate.ps1") -EvidenceDir $evidenceDir -Head $SourceSha
if ($LASTEXITCODE -ne 0) { [void]$jobFailures.Add("The baseline validator failed on the run's summary.") }

$guardClasses = $limit
if ($limit.Count -eq 0 -and $skippedHere.Count -gt 0) {
    $guardClasses = @(@($scopeFile.lab) + @($scopeFile.prodLike) | ForEach-Object { [string]$_.class } | Where-Object { $_ -notin $skippedHere })
}
# An empty argument does not survive "powershell -File": -Classes goes only when it has a value.
$guardArguments = @("-RepoRoot", $RepoRoot, "-Scope", $scopePath, "-TrxDir", $trxDir, "-EvidenceDir", $evidenceDir, "-OutDir", $runDir, "-Ran", $ran)
if (@($guardClasses).Count -gt 0) { $guardArguments += @("-Classes", (@($guardClasses) -join ",")) }
& $shell -NoProfile -ExecutionPolicy Bypass -File (Join-Path (Join-Path $scripts "ci") "acceptance-guard.ps1") @guardArguments
if ($LASTEXITCODE -ne 0) { [void]$jobFailures.Add("The guard failed: see summary.md.") }

if (-not [string]::IsNullOrWhiteSpace($simulated)) {
    $blocked = @(Get-ChildItem -LiteralPath $evidenceDir -Filter "result.json" -Recurse | Where-Object { (Get-Content -LiteralPath $_.FullName -Raw) -match "Simulated by ACCEPTANCE_SIMULATE_MISSING" })
    if ($blocked.Count -eq 0) { [void]$jobFailures.Add("ACCEPTANCE_SIMULATE_MISSING=$simulated did not reach its scenario.") }
    else { Write-Host "Simulated scenario blocked: $simulated" }
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$timingJson = [ordered]@{ sourceSha = $SourceSha; codeTree = $codeTree; steps = $timing.ToArray(); skippedHere = $skippedHere.ToArray(); failures = $jobFailures.ToArray() }
[System.IO.File]::WriteAllText((Join-Path $runDir "job-timing.json"), (($timingJson | ConvertTo-Json -Depth 4) + "`n"), $utf8NoBom)

if ($env:GITHUB_STEP_SUMMARY) {
    Get-Content -LiteralPath (Join-Path $runDir "summary.md") | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
    Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value ""
    Get-Content -LiteralPath (Join-Path $evidenceDir "summary.md") | Where-Object { $_ -match '^\*\*Totals' } | Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY
}

if ($jobFailures.Count -gt 0) {
    Write-Host "acceptance-job: FAIL"
    foreach ($failure in $jobFailures) { Write-Host "  - $failure"; if ($inCi) { Write-Host "::error::$failure" } }
    exit 1
}
Write-Host "acceptance-job: PASS"
exit 0
