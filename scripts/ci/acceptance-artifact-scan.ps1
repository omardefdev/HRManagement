#Requires -Version 5.1
# PLAN-41 (P-5): nothing generated on the runner may reach the acceptance artifact. Exit 1 names the file, never the value:
#   - a file named like the local secrets (credentials.json, .env, accounts.local.*) or a memory dump (.dmp, .core);
#   - any value of .acceptance/credentials.json or a generated value of .env.leastpriv in a text file;
#   - a JSON Web Token.
param(
    [string]$RepoRoot = "",
    [string]$Target = "artifacts/acceptance/run",
    # Tests: other sources of the values to look for.
    [string]$CredentialsFile = ".acceptance/credentials.json",
    [string]$LeastPrivEnv = ".env.leastpriv"
)

$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RepoRoot)) { $RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot) }
. (Join-Path (Join-Path $RepoRoot "scripts") "acceptance-evidence-lib.ps1")
function Resolve-ScanPath([string]$Path) { if ([System.IO.Path]::IsPathRooted($Path)) { return $Path } return (Join-Path $RepoRoot $Path) }

$targetPath = Resolve-ScanPath $Target
if (-not (Test-Path -LiteralPath $targetPath)) { Write-Host "acceptance-artifact-scan: nothing at $Target"; exit 0 }

$values = New-Object System.Collections.Generic.HashSet[string]
$credentials = Read-AcceptanceJson -Path (Resolve-ScanPath $CredentialsFile)
if ($null -ne $credentials) {
    foreach ($property in $credentials.passwords.PSObject.Properties) { [void]$values.Add([string]$property.Value) }
    $proof = Get-AcceptanceProperty $credentials "gatewayCallerProofSecret"
    if ($proof) { [void]$values.Add([string]$proof) }
}
$envPath = Resolve-ScanPath $LeastPrivEnv
if (Test-Path -LiteralPath $envPath) {
    foreach ($line in [System.IO.File]::ReadAllLines($envPath)) {
        if ($line -match '^[A-Z][A-Z0-9_]*(PASSWORD|_PASS|SECRET|_KEY|TOKEN)=(.{20,})$') { [void]$values.Add($Matches[2].Trim('"')) }
    }
}
$values.Remove("") | Out-Null

$pattern = "eyJ[A-Za-z0-9_-]{20,}\.eyJ[A-Za-z0-9_-]{20,}\."
if ($values.Count -gt 0) { $pattern += "|" + (($values | ForEach-Object { [regex]::Escape($_) }) -join "|") }
$regex = New-Object System.Text.RegularExpressions.Regex($pattern)

$binary = @(".png", ".jpg", ".jpeg", ".pdf", ".zip", ".gz", ".dll", ".exe")
$findings = New-Object System.Collections.Generic.List[string]
$count = 0
foreach ($file in @(Get-ChildItem -LiteralPath $targetPath -Recurse -File -Force)) {
    $relative = $file.FullName.Substring($targetPath.TrimEnd('\', '/').Length + 1) -replace '\\', '/'
    if ($file.Name -in @("credentials.json", ".env", ".env.leastpriv") -or $file.Name -like "accounts.local.*") {
        [void]$findings.Add("$relative is a local secrets file.")
        continue
    }
    # Process memory holds every value in a form the text search below cannot see.
    if ($file.Extension.ToLowerInvariant() -in @(".dmp", ".core")) {
        [void]$findings.Add("$relative is a memory dump.")
        continue
    }
    if ($binary -contains $file.Extension.ToLowerInvariant()) { continue }
    $count++
    if ($regex.IsMatch([System.IO.File]::ReadAllText($file.FullName))) { [void]$findings.Add("$relative contains a generated secret or a token.") }
}

if ($findings.Count -gt 0) {
    Write-Host "acceptance-artifact-scan: FAIL ($($findings.Count))"
    foreach ($finding in $findings) { Write-Host "  - $finding" }
    exit 1
}
Write-Host "acceptance-artifact-scan: PASS ($count files, $($values.Count) values)"
exit 0
