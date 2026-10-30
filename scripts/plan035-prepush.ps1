# PLAN-35 (G-QA-01): pre-push check. Run before the owner pushes local work to GitHub. It never pushes.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/plan035-prepush.ps1
#   powershell -File scripts/plan035-prepush.ps1 -Only gitleaks,forbidden            # selected checks
#   powershell -File scripts/plan035-prepush.ps1 -RepoRoot $env:TEMP\x -Base base     # another repository / base ref
#
# Checks (each PASS or FAIL in <Out>/report.md; exit 1 when any check fails):
#   gitleaks   - gitleaks (pinned digest, docs/qa/tool-pins.json) over the whole history AND over <Base>..<Head>, redacted.
#   actionlint - actionlint (pinned digest) on .github/workflows/*.yml.
#   cilocal    - the ci-local summary exists, its gitSha is <Head>, nothing failed (not-run allowed only for -AllowNotRun).
#   forbidden  - every file touched by any commit in <Base>..<Head>: no .env, .acceptance/, artifacts/,
#                testdata/*/out/*.json, no blob over 50 MB. global.json (P-3): HEAD:global.json equals the pin commit's
#                content and every version of it in the range has rollForward "disable".
#   nonprod    - python3 scripts/check-non-prod-data-policy.py (GOV-001-D); runs in the ci-local image when this
#                machine has no Python.
#   signatures - prints the signature status of the range; fails only with -RequireSignatures (D-QA-16).
# Windows PowerShell 5.1 and PowerShell 7. ASCII only.

param(
    [string]$RepoRoot = "",
    [string]$Base = "origin/main",
    [string]$Head = "HEAD",
    [string[]]$Only = @(),
    [string]$Out = "artifacts/plan35/prepush",
    [string]$CiLocalSummary = "artifacts/ci-local/summary.json",
    [string[]]$AllowNotRun = @("Integration tests (Testcontainers)", "Coverage gate"),
    [string]$GlobalJsonPinCommit = "544ba7a",
    [switch]$RequireSignatures,
    [string]$Branch = "release/plan-35-first-push"
)

$ErrorActionPreference = "Stop"
$scriptRepo = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
if (-not $RepoRoot) { $RepoRoot = $scriptRepo }
$RepoRoot = (Resolve-Path $RepoRoot).Path
# Native tools write progress to stderr; Windows PowerShell 5.1 would turn that into terminating errors.
$ErrorActionPreference = "Continue"

$allChecks = @("gitleaks", "actionlint", "cilocal", "forbidden", "nonprod", "signatures")
$selected = @($Only | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim().ToLowerInvariant() } | Where-Object { $_ })
if ($selected.Count -eq 0) { $selected = $allChecks }
foreach ($s in $selected) { if ($allChecks -notcontains $s) { throw "unknown check '$s' (known: $($allChecks -join ', '))" } }

if (-not [IO.Path]::IsPathRooted($Out)) { $Out = Join-Path $RepoRoot $Out }
New-Item -ItemType Directory -Force $Out | Out-Null
if (-not [IO.Path]::IsPathRooted($CiLocalSummary)) { $CiLocalSummary = Join-Path $RepoRoot $CiLocalSummary }

$pins = Get-Content -Raw (Join-Path $scriptRepo "docs/qa/tool-pins.json") | ConvertFrom-Json
$gitleaksRef = $pins.tools.gitleaks.ref
$actionlintRef = $pins.tools.actionlint.ref

# Commits in the range that may touch otherwise forbidden paths, with the reason (P-3 and the PLAN-26 snapshot).
# Matched by commit prefix AND exact path.
$allowedCommitPaths = @(
    @{ commit = "544ba7a"; path = "testdata/mig002/out/runtime-validate-report.json"; reason = "PLAN-26 snapshot commit: regenerated validation report (counts/dates only), reviewed in PLAN-35 P1" },
    @{ commit = "544ba7a"; path = "testdata/mig002/out/validate-report.json"; reason = "PLAN-26 snapshot commit: regenerated validation report (counts/dates only), reviewed in PLAN-35 P1" },
    @{ commit = "544ba7a"; path = "testdata/rel004/out/compat-validate-ledger.json"; reason = "PLAN-26 snapshot commit: regenerated validation report (counts/dates only), reviewed in PLAN-35 P1" },
    @{ commit = "544ba7a"; path = "testdata/str005/out/runtime-validate-report.json"; reason = "PLAN-26 snapshot commit: regenerated validation report (counts/dates only), reviewed in PLAN-35 P1" }
)

function Invoke-Git([string[]]$GitArgs) {
    $o = & git -C $RepoRoot @GitArgs 2>&1
    $script:gitExit = $LASTEXITCODE
    return @($o | ForEach-Object { "$_" })
}

$results = [System.Collections.Generic.List[object]]::new()
function Add-Result([string]$check, [bool]$ok, [string[]]$details) {
    $script:results.Add([pscustomobject]@{ check = $check; ok = $ok; details = @($details) })
    $mark = "PASS"
    if (-not $ok) { $mark = "FAIL" }
    Write-Host ("[{0}] {1}" -f $mark, $check)
    foreach ($d in $details) { Write-Host ("       {0}" -f $d) }
}

$headSha = @(Invoke-Git @("rev-parse", "--verify", "$Head^{commit}"))[0]
if ($gitExit -ne 0) { throw "head ref '$Head' not found in $RepoRoot" }
$baseSha = @(Invoke-Git @("rev-parse", "--verify", "$Base^{commit}"))[0]
if ($gitExit -ne 0) { throw "base ref '$Base' not found in $RepoRoot (git fetch origin first)" }
$range = "$baseSha..$headSha"
$rangeCount = @(Invoke-Git @("rev-list", "--count", $range))[0]
Write-Host "Repository: $RepoRoot"
Write-Host "Head $Head ($headSha), base $Base ($baseSha), $rangeCount commit(s) in the range"

# Runs a native command, writes its stdout and stderr to $log as plain text, returns its exit code.
function Invoke-Logged([string]$log, [string]$exe, [string[]]$NativeArgs) {
    $lines = @(& $exe @NativeArgs 2>&1 | ForEach-Object { ("$_" -replace '\x1b\[[0-9;]*m', '') })
    $code = $LASTEXITCODE
    [IO.File]::WriteAllLines($log, [string[]]$lines)
    return $code
}

function Test-Docker {
    & docker version --format "{{.Server.Version}}" *> $null
    return ($LASTEXITCODE -eq 0)
}

# ---------------------------------------------------------------- gitleaks
if ($selected -contains "gitleaks") {
    $details = @()
    $ok = $true
    if (-not (Test-Docker)) {
        Add-Result "gitleaks" $false @("docker is not available; gitleaks runs from $gitleaksRef")
    }
    else {
        $configArgs = @()
        if (Test-Path (Join-Path $RepoRoot ".gitleaks.toml")) { $configArgs = @("--config", "/repo/.gitleaks.toml") }
        $scans = @(
            @{ name = "history"; extra = @("--log-opts=$headSha") },
            @{ name = "range"; extra = @("--log-opts=$range") }
        )
        foreach ($scan in $scans) {
            $report = "gitleaks-$($scan.name).json"
            $log = Join-Path $Out "gitleaks-$($scan.name).log"
            $dockerArgs = @("run", "--rm", "-v", "${RepoRoot}:/repo", "-v", "${Out}:/out", $gitleaksRef,
                "detect", "--source", "/repo", "--redact", "--no-banner", "--no-color", "--exit-code", "1",
                "--report-format", "json", "--report-path", "/out/$report") + $configArgs + $scan.extra
            $reportPath = Join-Path $Out $report
            Remove-Item $reportPath -ErrorAction SilentlyContinue
            $code = Invoke-Logged $log "docker" $dockerArgs
            $findings = @()
            if (Test-Path $reportPath) {
                $raw = Get-Content -Raw $reportPath
                # Windows PowerShell 5.1 turns "[]" into one empty element: keep only real findings.
                if ($raw -and $raw.Trim() -ne "null") {
                    $findings = @($raw | ConvertFrom-Json | ForEach-Object { $_ } | Where-Object { $_ -and $_.RuleID })
                }
            }
            $scanned = (Select-String -Path $log -Pattern "commits scanned" | Select-Object -First 1)
            $scannedText = ""
            if ($scanned) { $scannedText = ($scanned.Line -replace '^.*INF\s*', '').Trim() }
            if ($code -eq 0 -and $findings.Count -eq 0) {
                $details += "$($scan.name): no leaks ($scannedText)"
            }
            elseif ($code -eq 1 -and $findings.Count -gt 0) {
                $ok = $false
                $details += "$($scan.name): $($findings.Count) finding(s) (redacted report $report):"
                foreach ($f in $findings) {
                    $short = "$($f.Commit)"
                    if ($short.Length -gt 8) { $short = $short.Substring(0, 8) }
                    $details += ("  {0}:{1} rule={2} commit={3}" -f $f.File, $f.StartLine, $f.RuleID, $short)
                }
            }
            else {
                $ok = $false
                $details += "$($scan.name): gitleaks exited $code without a usable report (log $log)"
            }
        }
        Add-Result "gitleaks" $ok $details
    }
}

# ---------------------------------------------------------------- actionlint
if ($selected -contains "actionlint") {
    $wf = Join-Path $RepoRoot ".github/workflows"
    if (-not (Test-Path $wf)) {
        Add-Result "actionlint" $false @("no .github/workflows directory")
    }
    elseif (-not (Test-Docker)) {
        Add-Result "actionlint" $false @("docker is not available; actionlint runs from $actionlintRef")
    }
    else {
        $log = Join-Path $Out "actionlint.txt"
        $code = Invoke-Logged $log "docker" @("run", "--rm", "-v", "${RepoRoot}:/repo", "-w", "/repo", $actionlintRef, "-no-color", "-oneline")
        $lines = @(Get-Content $log | Where-Object { $_.Trim() })
        if ($code -eq 0) { Add-Result "actionlint" $true @("$((Get-ChildItem $wf -Filter *.yml).Count) workflow file(s), no findings") }
        else { Add-Result "actionlint" $false (@("exit $code, $($lines.Count) finding(s):") + ($lines | Select-Object -First 40)) }
    }
}

# ---------------------------------------------------------------- ci-local summary
if ($selected -contains "cilocal") {
    if (-not (Test-Path $CiLocalSummary)) {
        Add-Result "cilocal" $false @("missing $CiLocalSummary - run: powershell -File scripts/ci-local.ps1 -SkipTestcontainers")
    }
    else {
        $s = Get-Content -Raw $CiLocalSummary | ConvertFrom-Json
        $details = @("summary $CiLocalSummary, gitSha $($s.gitSha), generated $($s.generatedUtc)")
        $ok = $true
        if ($s.gitSha -ne $headSha) { $ok = $false; $details += "gitSha is not HEAD ($headSha): run ci-local again on HEAD" }
        if (@($s.dirtyTracked).Count -gt 0) { $ok = $false; $details += "summary records dirty tracked files: $(@($s.dirtyTracked) -join ', ')" }
        foreach ($st in @($s.steps)) {
            if ($st.status -eq "passed") { continue }
            if ($st.status -eq "not-run" -and $AllowNotRun -contains $st.name) {
                $details += "not-run (allowed, runs on ubuntu-latest and natively): $($st.name)"
                continue
            }
            $ok = $false
            $details += "$($st.status): $($st.name)"
        }
        if (@($s.steps).Count -eq 0) { $ok = $false; $details += "summary lists no steps" }
        Add-Result "cilocal" $ok $details
    }
}

# ---------------------------------------------------------------- forbidden files
if ($selected -contains "forbidden") {
    $details = @()
    $ok = $true
    $commits = Invoke-Git @("rev-list", "--reverse", $range)
    foreach ($c in $commits) {
        $files = Invoke-Git @("diff-tree", "--no-commit-id", "--name-only", "-r", "--root", "-c", $c)
        foreach ($f in ($files | Sort-Object -Unique)) {
            $reason = $null
            if ($f -match '(^|/)\.env$') { $reason = ".env file" }
            elseif ($f -match '(^|/)\.acceptance/') { $reason = ".acceptance/ (local acceptance state)" }
            elseif ($f -match '^artifacts/') { $reason = "artifacts/ (generated output)" }
            elseif ($f -match '^testdata/[^/]+/out/.+\.json$') { $reason = "testdata/*/out/*.json (rewritten by validation runs)" }
            if (-not $reason) { continue }
            $allowed = $allowedCommitPaths | Where-Object { $c.StartsWith($_.commit) -and $_.path -eq $f } | Select-Object -First 1
            if ($allowed) { $details += "allowed: $f in $($c.Substring(0, 8)) - $($allowed.reason)"; continue }
            $ok = $false
            $details += "FORBIDDEN: $f in commit $($c.Substring(0, 8)) - $reason"
        }
    }

    # Blobs over 50 MB anywhere in the range (also catches a file added and removed again).
    $objects = Invoke-Git @("rev-list", "--objects", $range)
    if ($objects.Count -gt 0) {
        $tmp = Join-Path $Out "objects.txt"
        [IO.File]::WriteAllLines($tmp, [string[]]($objects | ForEach-Object { ($_ -split ' ')[0] }))
        $batch = Get-Content $tmp | & git -C $RepoRoot cat-file "--batch-check=%(objecttype) %(objectname) %(objectsize)"
        $big = @($batch | Where-Object { $_ -match '^blob (\S+) (\d+)$' -and [int64]$Matches[2] -gt 50MB })
        foreach ($b in $big) {
            $sha = ($b -split ' ')[1]
            $name = ($objects | Where-Object { $_.StartsWith($sha) } | Select-Object -First 1)
            $ok = $false
            $details += "FORBIDDEN: blob over 50 MB: $name ($b)"
        }
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }

    # global.json (P-3): HEAD content equals the pin commit's content; every version in the range keeps rollForward disable.
    $gjCommits = Invoke-Git @("rev-list", $range, "--", "global.json")
    foreach ($c in $gjCommits) {
        $content = (Invoke-Git @("show", "${c}:global.json")) -join "`n"
        if ($gitExit -ne 0) { $ok = $false; $details += "FORBIDDEN: global.json deleted in $($c.Substring(0, 8))"; continue }
        $roll = $null
        try { $roll = ($content | ConvertFrom-Json).sdk.rollForward } catch { $roll = "<unparseable>" }
        if ($roll -ne "disable") {
            $ok = $false
            $details += "FORBIDDEN: global.json in commit $($c.Substring(0, 8)) sets rollForward '$roll' (must be 'disable')"
        }
        else { $details += "global.json in $($c.Substring(0, 8)): rollForward disable" }
    }
    if ($gjCommits.Count -gt 0) {
        $pinned = (Invoke-Git @("show", "${GlobalJsonPinCommit}:global.json")) -join "`n"
        $pinExit = $gitExit
        $current = (Invoke-Git @("show", "${headSha}:global.json")) -join "`n"
        if ($pinExit -ne 0) { $ok = $false; $details += "FORBIDDEN: global.json changes in the range, but pin commit $GlobalJsonPinCommit is not in this repository" }
        elseif ($pinned.Trim() -ne $current.Trim()) { $ok = $false; $details += "FORBIDDEN: HEAD:global.json differs from the pin commit $GlobalJsonPinCommit" }
        else { $details += "HEAD:global.json equals pin commit $GlobalJsonPinCommit" }
    }
    $details = @("$($commits.Count) commit(s) checked in $range") + $details
    Add-Result "forbidden" $ok $details
}

# ---------------------------------------------------------------- non-prod data policy
if ($selected -contains "nonprod") {
    $script = "scripts/check-non-prod-data-policy.py"
    if (-not (Test-Path (Join-Path $RepoRoot $script))) {
        Add-Result "nonprod" $false @("missing $script")
    }
    else {
        $log = Join-Path $Out "nonprod.log"
        $py = $null
        foreach ($cand in @("python3", "python")) {
            if (Get-Command $cand -ErrorAction SilentlyContinue) {
                & $cand -c "import sys; sys.exit(0)" *> $null
                if ($LASTEXITCODE -eq 0) { $py = $cand; break }
            }
        }
        if ($py) {
            Push-Location $RepoRoot
            $code = Invoke-Logged $log $py @($script)
            Pop-Location
            $how = $py
        }
        else {
            # The Store "python" alias on Windows is not Python; use the ci-local image (python3) instead.
            $image = (& docker image ls "hrm-ci-local" --format "{{.Repository}}:{{.Tag}}" 2>$null | Select-Object -First 1)
            if (-not $image) { Add-Result "nonprod" $false @("no Python on this machine and no hrm-ci-local image (run scripts/ci-local.ps1 once)"); $code = -1 }
            else {
                $code = Invoke-Logged $log "docker" @("run", "--rm", "-v", "${RepoRoot}:/repo", "-w", "/repo", $image, "python3", $script)
                $how = "python3 in $image"
            }
        }
        if ($code -ne -1) {
            $tail = @(Get-Content $log | Select-Object -Last 5)
            Add-Result "nonprod" ($code -eq 0) (@("$how, exit $code") + $tail)
        }
    }
}

# ---------------------------------------------------------------- signatures
if ($selected -contains "signatures") {
    $sig = Invoke-Git @("log", "--format=%G?", $range)
    $groups = $sig | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }
    $unsigned = @($sig | Where-Object { $_ -ne "G" -and $_ -ne "U" }).Count
    $details = @("signature status in range (%G?): $($groups -join ' ') (N = no signature)")
    if ($RequireSignatures) {
        Add-Result "signatures" ($unsigned -eq 0) ($details + "D-QA-16 requires signed commits: $unsigned without a good signature")
    }
    else {
        Add-Result "signatures" $true ($details + "D-QA-16: signing not required (dated exception SCM-UNSIGNED-COMMITS); history is not rewritten")
    }
}

# ---------------------------------------------------------------- report
$failed = @($results | Where-Object { -not $_.ok })
$md = [System.Collections.Generic.List[string]]::new()
$md.Add("# PLAN-35 pre-push report")
$md.Add("")
$md.Add("- Generated (UTC): $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))")
$md.Add("- HEAD: ``$headSha``; base: ``$Base`` (``$baseSha``); commits in range: $rangeCount")
$md.Add("- Tools: gitleaks ``$gitleaksRef``; actionlint ``$actionlintRef``")
$verdict = "PASS"
if ($failed.Count -gt 0) { $verdict = "FAIL ($($failed.Count) check(s))" }
$md.Add("- Result: **$verdict**")
$md.Add("")
foreach ($r in $results) {
    $mark = "PASS"
    if (-not $r.ok) { $mark = "FAIL" }
    $md.Add("## $($r.check): $mark")
    $md.Add("")
    foreach ($d in $r.details) { $md.Add("- $d") }
    $md.Add("")
}
$md.Add("## Owner push commands (this script never pushes)")
$md.Add("")
$md.Add('```powershell')
$md.Add("git push -u origin $Branch")
$md.Add("gh pr create --base main --head $Branch --title `"First push: PLAN-25..35`" --body-file reports/plan35-first-push.md")
$md.Add('```')
[IO.File]::WriteAllLines((Join-Path $Out "report.md"), [string[]]$md)

Write-Host ""
Write-Host "Report: $(Join-Path $Out 'report.md')"
if ($failed.Count -gt 0) {
    Write-Host "PRE-PUSH FAILED: $(@($failed | ForEach-Object { $_.check }) -join ', ')"
    exit 1
}
Write-Host "PRE-PUSH OK. Owner commands (not run by this script):"
Write-Host "  git push -u origin $Branch"
Write-Host "  gh pr create --base main --head $Branch --title `"First push: PLAN-25..35`" --body-file reports/plan35-first-push.md"
exit 0
