# PLAN-67 (UX-A): static checks of one content security policy per page and nothing inline. ASCII only; Windows PowerShell 5.1.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ux067-validate.ps1 [-SelfTest]
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ux067-validate.ps1 -Headers http://localhost:6200/careers,http://localhost:6200/ui/hr-core/
#
# What it checks (InterfaceTextGuardTests, Ux067* and AC-UX-67 prove the behaviour; this script guards the files a reviewer or an
# operator edits by hand):
#   1. The policy builder allows scripts and styles from the app's origin or with the response's nonce only: no 'unsafe-inline',
#      'unsafe-hashes' or 'unsafe-eval', no wildcard and no external source, object-src 'none'.
#   2. Every appsettings.Production.json enforces the policy (Security:CspMode Enforce, never ReportOnly) and adds no wildcard or
#      unsafe connect source (a named host such as the sign-in server is allowed, as UX-001 says).
#   3. The prod-like acceptance stack and the CI E2E stack enforce it on all fifteen applications.
#   4. No view carries an inline event handler, a style attribute or a javascript: URL.
#   5. The interface guard's lists (tests/architecture/ui-text-allowlist) hold no row: they only shrink, and PLAN-67 emptied them.
#   6. The gateway's user-name migration has no byte order mark, and its lookup POST takes the antiforgery token.
#   7. AC-UX-67 is a required prod-like class of the main group that needs the browser, owned by PLAN-67 and proving its claim.
# -SelfTest runs each check on a planted defect and fails unless every one of them is refused.
# -Headers asks each URL once with curl.exe (no redirect followed) and fails unless its answer carries exactly one Content-Security-Policy policy,
# no Report-Only policy, and nothing unsafe in it.
param(
    [switch]$SelfTest,
    [string[]]$Headers = @()
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($root)) { $root = (Get-Location).Path }

$paths = @{
    Builder    = "src/Shared/HRManagement.ServiceDefaults/HrSecurityHeadersExtensions.cs"
    ProdLike   = "docker-compose.acceptance-prodlike.yml"
    CiE2e      = "docker-compose.ci-e2e.yml"
    Lists      = "tests/architecture/ui-text-allowlist"
    Migration  = "src/Gateway/HRManagement.Gateway/Data/Migrations/20261113170228_UX067_UserNameRegistry.cs"
    Designer   = "src/Gateway/HRManagement.Gateway/Data/Migrations/20261113170228_UX067_UserNameRegistry.Designer.cs"
    UserNames  = "src/Gateway/HRManagement.Gateway/Controllers/UserNamesController.cs"
    Scope      = "tests/acceptance/ci-scope.json"
    Ownership  = "tests/acceptance/ac-ownership.json"
}
$appCount = 15

function Read-Text([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { return $null }
    return [System.IO.File]::ReadAllText($path)
}

function Read-Bytes([string]$relative) {
    $path = Join-Path $root $relative
    if (-not (Test-Path $path)) { return $null }
    return [System.IO.File]::ReadAllBytes($path)
}

function Get-Json([string]$text) {
    if ($null -eq $text) { return $null }
    return ($text | ConvertFrom-Json)
}

# Tracked files only: a build output or a local copy is not the product.
function Get-TrackedFiles([string]$pattern) {
    Push-Location $root
    try { return @(git ls-files -- $pattern | Where-Object { $_ -notmatch '/(bin|obj)/' }) }
    finally { Pop-Location }
}

# A policy text, or the builder's source: what no policy of the product may hold.
function Test-PolicyText([string]$name, [string]$text, [switch]$AllowHosts) {
    $problems = @()
    foreach ($unsafe in @("'unsafe-inline'", "'unsafe-hashes'", "'unsafe-eval'", "'unsafe-allow-redirects'")) {
        if ($text.Contains($unsafe)) { $problems += "${name}: allows $unsafe" }
    }
    if ($text -match "(^|[\s'""])\*(\.|[\s;'""]|$)") { $problems += "${name}: has a wildcard source" }
    if (-not $AllowHosts -and $text -match "https?://") { $problems += "${name}: names an external source" }
    return $problems
}

function Test-Builder([string]$text) {
    if ($null -eq $text) { return @("the policy builder is missing: $($paths.Builder)") }
    $start = $text.IndexOf("string BuildContentSecurityPolicy(")
    if ($start -lt 0) { return @("BuildContentSecurityPolicy is missing") }
    $end = $text.IndexOf("return csp.ToString();", $start)
    if ($end -lt 0) { return @("BuildContentSecurityPolicy no longer ends with return csp.ToString()") }
    $body = $text.Substring($start, $end - $start)
    $problems = @(Test-PolicyText "the policy builder" $body)
    if ($body -notmatch "script-src 'self' 'nonce-\{nonce\}';") { $problems += "the policy builder: script-src is not 'self' with the nonce alone" }
    if ($body -notmatch "style-src 'self' 'nonce-\{nonce\}';") { $problems += "the policy builder: style-src is not 'self' with the nonce alone" }
    if ($body -notmatch "object-src 'none'") { $problems += "the policy builder: object-src is not 'none'" }
    if ($text -notmatch 'Response\.OnStarting\(') { $problems += "the security headers are no longer written when the response starts (D-67-1)" }
    return $problems
}

# $files: relative path -> text, every tracked appsettings.Production.json of src
function Test-ProductionModes([hashtable]$files) {
    $problems = @()
    if ($files.Count -lt $appCount) { $problems += "only $($files.Count) Production settings files, $appCount expected" }
    foreach ($name in $files.Keys) {
        $text = $files[$name]
        if ($text -notmatch '"CspMode"\s*:\s*"Enforce"') { $problems += "${name}: Security:CspMode is not Enforce" }
        if ($text -match '"CspMode"\s*:\s*"ReportOnly"') { $problems += "${name}: Security:CspMode is ReportOnly" }
        $match = [regex]::Match($text, '"CspConnectSrcExtra"\s*:\s*\[(?<v>[^\]]*)\]')
        # A named host (the sign-in server, UX-001) may be added to connect-src; nothing unsafe and no wildcard.
        if ($match.Success) { $problems += Test-PolicyText "${name} CspConnectSrcExtra" $match.Groups["v"].Value -AllowHosts }
    }
    return $problems
}

function Test-Compose([string]$name, [string]$text) {
    if ($null -eq $text) { return @("${name} is missing") }
    $problems = @()
    $enforced = ([regex]::Matches($text, '(?m)^\s+Security__CspMode:\s*"?Enforce"?\s*$')).Count
    if ($enforced -lt $appCount) { $problems += "${name}: $enforced applications enforce the policy, $appCount expected" }
    if ($text -match 'Security__CspMode:\s*"?ReportOnly') { $problems += "${name}: an application only reports the policy" }
    return $problems
}

# $views: relative path -> text, every tracked .cshtml of src
function Test-Views([hashtable]$views) {
    $problems = @()
    foreach ($name in $views.Keys) {
        $lines = $views[$name] -split "`n"
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match "\son[a-z]+\s*=\s*[""']") { $problems += "${name}:$($i + 1): an inline event handler" }
            if ($line -match "\sstyle\s*=\s*[""']") { $problems += "${name}:$($i + 1): a style attribute" }
            if ($line -match "(?i)javascript:") { $problems += "${name}:$($i + 1): a javascript: URL" }
        }
    }
    return $problems
}

# $files: file name -> text, the files of tests/architecture/ui-text-allowlist
function Test-GuardLists([hashtable]$files) {
    $problems = @()
    foreach ($name in $files.Keys) {
        $rows = @($files[$name] -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith("#") })
        if ($rows.Count -gt 0) { $problems += "ui-text-allowlist/${name}: $($rows.Count) row(s); PLAN-67 emptied the lists and they only shrink" }
    }
    return $problems
}

function Test-Gateway([byte[]]$migration, [byte[]]$designer, [string]$controller) {
    $problems = @()
    foreach ($pair in @(@("the user-name migration", $migration), @("its designer file", $designer))) {
        $bytes = $pair[1]
        if ($null -eq $bytes) { $problems += "$($pair[0]) is missing"; continue }
        if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) { $problems += "$($pair[0]) starts with a byte order mark" }
    }
    if ($null -eq $controller) { $problems += "UserNamesController is missing" }
    else {
        if ($controller -notmatch '\[HttpPost\("by-ids"\)\]\s*\[ValidateAntiForgeryToken\]') { $problems += "the user-name lookup POST does not take the antiforgery token" }
        if ($controller -notmatch '\[HttpGet\("token"\)\]') { $problems += "the user-name lookup has no token endpoint" }
        if ($controller -notmatch '\[Authorize\(Policy = HrManagementPolicies\.GatewayUserNames\)\]') { $problems += "the user-name lookup is not under Gateway.UserNames" }
    }
    return $problems
}

function Test-Acceptance($scope, $ownership) {
    $problems = @()
    if ($null -eq $scope) { return @("the CI scope is missing") }
    $entry = @($scope.prodLike | Where-Object { $_.class -eq "AcUx67ReadableInterfaceTests" })
    if ($entry.Count -ne 1) { $problems += "AcUx67ReadableInterfaceTests is not listed once in prodLike[]" }
    else {
        if ($entry[0].required -ne $true) { $problems += "AcUx67ReadableInterfaceTests is not required" }
        if ($entry[0].group -ne "main") { $problems += "AcUx67ReadableInterfaceTests is not in the main group" }
        if ($entry[0].browser -ne $true) { $problems += "AcUx67ReadableInterfaceTests does not ask for the browser" }
    }
    $owner = if ($null -ne $ownership) { $ownership.acs."AC-UX-67" } else { $null }
    if ($null -eq $owner) { $problems += "AC-UX-67 has no ownership entry" }
    elseif ($owner.ownerPlan -ne "PLAN-67" -or $owner.provesClaim -ne $true) { $problems += "AC-UX-67 is not owned by PLAN-67 or does not prove its claim" }
    return $problems
}

function Get-TextMap([string[]]$relatives, [switch]$ByName) {
    $map = @{}
    foreach ($relative in $relatives) {
        $key = if ($ByName) { Split-Path -Leaf $relative } else { $relative }
        $map[$key] = Read-Text $relative
    }
    return $map
}

function Invoke-All {
    $all = @()
    $all += Test-Builder (Read-Text $paths.Builder)
    $all += Test-ProductionModes (Get-TextMap (Get-TrackedFiles "src/*appsettings.Production.json"))
    $all += Test-Compose $paths.ProdLike (Read-Text $paths.ProdLike)
    $all += Test-Compose $paths.CiE2e (Read-Text $paths.CiE2e)
    $all += Test-Views (Get-TextMap (Get-TrackedFiles "src/*.cshtml"))
    $all += Test-GuardLists (Get-TextMap (Get-TrackedFiles "$($paths.Lists)/*") -ByName)
    $all += Test-Gateway (Read-Bytes $paths.Migration) (Read-Bytes $paths.Designer) (Read-Text $paths.UserNames)
    $all += Test-Acceptance (Get-Json (Read-Text $paths.Scope)) (Get-Json (Read-Text $paths.Ownership))
    return @($all | Where-Object { $_ })
}

# ---------------------------------------------------------------- headers of a live answer

# The CSP headers of one answer: the number of policies (one per header, more when a header joins policies with commas).
function Test-HeaderValues([string]$url, [string[]]$csp, [string[]]$reportOnly) {
    $problems = @()
    $policies = @($csp | ForEach-Object { $_ -split ',' } | Where-Object { $_ -match '-src' })
    if ($policies.Count -ne 1) { $problems += "${url}: $($policies.Count) Content-Security-Policy policies, 1 expected" }
    if (@($reportOnly | Where-Object { $_ }).Count -gt 0) { $problems += "${url}: a Content-Security-Policy-Report-Only header" }
    foreach ($policy in $policies) {
        foreach ($directive in @($policy -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
            # connect-src may name a host (the sign-in server, UX-001); every other directive stays on the origin and the nonce.
            if ($directive -match '^connect-src\b') { $problems += Test-PolicyText $url $directive -AllowHosts }
            else { $problems += Test-PolicyText $url $directive }
        }
    }
    return $problems
}

# The header block of one GET (no redirect followed), from curl.exe's -D output: status line, then "name: value" lines.
function Read-HeaderBlock([string[]]$lines) {
    $status = 0
    $csp = @()
    $reportOnly = @()
    foreach ($line in $lines) {
        $text = ([string]$line).TrimEnd("`r")
        if ($text -match '^HTTP/[\d\.]+\s+(\d{3})') { $status = [int]$Matches[1]; $csp = @(); $reportOnly = @(); continue }
        if ($text -match '^(?i)content-security-policy:\s*(.*)$') { $csp += $Matches[1]; continue }
        if ($text -match '^(?i)content-security-policy-report-only:\s*(.*)$') { $reportOnly += $Matches[1] }
    }
    return @{ Status = $status; Csp = $csp; ReportOnly = $reportOnly }
}

function Invoke-Headers([string[]]$urls) {
    $problems = @()
    foreach ($url in $urls) {
        $lines = @(& curl.exe -s -S -D - -o NUL --max-time 30 $url 2>&1)
        if ($LASTEXITCODE -ne 0) { $problems += "${url}: curl.exe exit $LASTEXITCODE ($($lines -join ' '))"; continue }
        $block = Read-HeaderBlock $lines
        Write-Host ("{0} -> HTTP {1}, {2} CSP header(s)" -f $url, $block.Status, $block.Csp.Count)
        $problems += Test-HeaderValues $url $block.Csp $block.ReportOnly
    }
    return @($problems | Where-Object { $_ })
}

if ($SelfTest) {
    $failures = @()
    $builder = Read-Text $paths.Builder
    if ((Test-Builder ($builder -replace "script-src 'self' 'nonce-\{nonce\}';", "script-src 'self' 'nonce-{nonce}' 'unsafe-inline';")).Count -eq 0) { $failures += "'unsafe-inline' in the builder was not refused" }
    if ((Test-Builder ($builder -replace "img-src 'self' data: blob:;", "img-src 'self' https://cdn.example.org;")).Count -eq 0) { $failures += "an external source in the builder was not refused" }
    if ((Test-Builder ($builder -replace "font-src 'self' data:;", "font-src *;")).Count -eq 0) { $failures += "a wildcard source in the builder was not refused" }
    $production = Get-TextMap (Get-TrackedFiles "src/*appsettings.Production.json")
    $first = @($production.Keys)[0]
    $planted = $production.Clone(); $planted[$first] = $planted[$first] -replace '"CspMode"(\s*):(\s*)"Enforce"', '"CspMode"$1:$2"ReportOnly"'
    if ((Test-ProductionModes $planted).Count -eq 0) { $failures += "a Production file in ReportOnly was not refused" }
    $prodLike = Read-Text $paths.ProdLike
    $once = ([regex]'(?m)^\s+Security__CspMode:\s*"?Enforce"?\s*\r?\n').Replace($prodLike, "", 1)
    if ((Test-Compose $paths.ProdLike $once).Count -eq 0) { $failures += "an application of the prod-like stack without Enforce was not refused" }
    foreach ($bad in @('<button onclick="go()">x</button>', '<div style="width: 3px">x</div>', '<a href="javascript:history.back()">x</a>')) {
        if ((Test-Views @{ "Planted.cshtml" = "<p>ok</p>`n$bad" }).Count -eq 0) { $failures += "a view with $bad was not refused" }
    }
    if ((Test-GuardLists @{ "hr-core.txt" = "# comment`nViews/X.cshtml|english-label|Save | IAM-F: later" }).Count -eq 0) { $failures += "a guard list with a row was not refused" }
    $migration = Read-Bytes $paths.Migration
    $bom = [byte[]](@(0xEF, 0xBB, 0xBF) + $migration)
    if ((Test-Gateway $bom (Read-Bytes $paths.Designer) (Read-Text $paths.UserNames)).Count -eq 0) { $failures += "a migration with a byte order mark was not refused" }
    $controller = (Read-Text $paths.UserNames) -replace '\[ValidateAntiForgeryToken\]', ''
    if ((Test-Gateway $migration (Read-Bytes $paths.Designer) $controller).Count -eq 0) { $failures += "a lookup POST without the antiforgery token was not refused" }
    $scope = Get-Json (Read-Text $paths.Scope)
    $ownership = Get-Json (Read-Text $paths.Ownership)
    $noBrowser = Get-Json (Read-Text $paths.Scope)
    foreach ($e in @($noBrowser.prodLike | Where-Object { $_.class -eq "AcUx67ReadableInterfaceTests" })) { $e.browser = $false }
    if ((Test-Acceptance $noBrowser $ownership).Count -eq 0) { $failures += "AC-UX-67 without the browser was not refused" }
    $without = Get-Json (Read-Text $paths.Scope)
    $without.prodLike = @($without.prodLike | Where-Object { $_.class -ne "AcUx67ReadableInterfaceTests" })
    if ((Test-Acceptance $without $ownership).Count -eq 0) { $failures += "a scope without AC-UX-67 was not refused" }
    $policy = "default-src 'self'; script-src 'self' 'nonce-abc'; object-src 'none'"
    if ((Test-HeaderValues "planted" @($policy, $policy) @()).Count -eq 0) { $failures += "two policies on one answer were not refused" }
    if ((Test-HeaderValues "planted" @("$policy, $policy") @()).Count -eq 0) { $failures += "two policies joined in one header were not refused" }
    if ((Test-HeaderValues "planted" @($policy) @($policy)).Count -eq 0) { $failures += "a Report-Only policy beside the enforced one was not refused" }
    if ((Test-HeaderValues "planted" @($policy) @()).Count -ne 0) { $failures += "one clean policy was refused" }
    if ((Test-HeaderValues "planted" @("$policy; connect-src 'self' http://sso.example.test") @()).Count -ne 0) { $failures += "a sign-in host in connect-src was refused" }
    if ((Test-HeaderValues "planted" @("default-src 'self'; script-src 'self' https://cdn.example.test; object-src 'none'") @()).Count -eq 0) { $failures += "an external script source was not refused" }
    $block = Read-HeaderBlock @("HTTP/1.1 200 OK", "Content-Security-Policy: $policy", "content-security-policy: $policy", "")
    if ((Test-HeaderValues "planted" $block.Csp $block.ReportOnly).Count -eq 0) { $failures += "two CSP header lines read by curl were not refused" }
    if ((Invoke-All).Count -ne 0) { $failures += "the repository itself does not pass" }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host "[FAIL] self-test: $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] ux067-validate self-test: 18 planted defects refused, two clean policies accepted, the repository passes" -ForegroundColor Green
    exit 0
}

if ($Headers.Count -gt 0) {
    $urls = @($Headers | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $problems = Invoke-Headers $urls
    if ($problems.Count -gt 0) {
        $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
        exit 1
    }
    Write-Host "[PASS] ux067-validate -Headers: $($urls.Count) answer(s), one enforced policy each, nothing unsafe" -ForegroundColor Green
    exit 0
}

$problems = Invoke-All
if ($problems.Count -gt 0) {
    $problems | ForEach-Object { Write-Host "[FAIL] $_" -ForegroundColor Red }
    exit 1
}
Write-Host "[PASS] ux067-validate: a nonce-only policy, Enforce in every Production file and both stacks, no inline handler or style in a view, empty guard lists, the gateway's migration and token, AC-UX-67" -ForegroundColor Green
exit 0
