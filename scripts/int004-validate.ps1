# INT-004: validate government connector registration and configuration.
# Usage:
#   powershell -File scripts/int004-validate.ps1

param(
    [string]$RepoRoot = ""
)

$ErrorActionPreference = "Stop"
if (-not $RepoRoot) {
    $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Resolve-RepoPath([string]$rel) {
    return (Join-Path $RepoRoot ($rel -replace '/', [IO.Path]::DirectorySeparatorChar))
}

$failures = @()

# --- Source file checks ---
$connectorDir = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults/Connectors"
$requiredFiles = @(
    "IntegrationConnectorMode.cs",
    "IGovernmentConnector.cs",
    "GovernmentConnectorOptions.cs",
    "LiveConnectorRegistry.cs",
    "GovernmentConnectorStartupValidator.cs",
    "TreasuryLiveConnector.cs",
    "CivilRegistryLiveConnector.cs",
    "FederationConnectorDescriptor.cs",
    "GovernmentConnectorExtensions.cs"
)

foreach ($f in $requiredFiles) {
    $path = Join-Path $connectorDir $f
    if (-not (Test-Path $path)) {
        $failures += "missing connector source file: $f"
    }
}

# --- Enum mode check ---
$enumFile = Join-Path $connectorDir "IntegrationConnectorMode.cs"
if (Test-Path $enumFile) {
    $enumContent = Get-Content -Raw -Path $enumFile
    foreach ($mode in @("File", "Stub", "Sandbox", "Live")) {
        if ($enumContent -notmatch "\b$mode\b") {
            $failures += "IntegrationConnectorMode missing value: $mode"
        }
    }
}

# --- DI extension check ---
$extFile = Join-Path $connectorDir "GovernmentConnectorExtensions.cs"
if (Test-Path $extFile) {
    $extContent = Get-Content -Raw -Path $extFile
    if ($extContent -notmatch "AddGovernmentConnectors") {
        $failures += "GovernmentConnectorExtensions missing AddGovernmentConnectors method"
    }
    if ($extContent -notmatch "GovernmentConnectorStartupValidator") {
        $failures += "GovernmentConnectorExtensions not registering startup validator"
    }
}

# --- Startup validator production guard ---
$validatorFile = Join-Path $connectorDir "GovernmentConnectorStartupValidator.cs"
if (Test-Path $validatorFile) {
    $validatorContent = Get-Content -Raw -Path $validatorFile
    if ($validatorContent -notmatch "Stub") {
        $failures += "StartupValidator must reject Stub mode in production"
    }
    if ($validatorContent -notmatch "IsProduction") {
        $failures += "StartupValidator must check IsProduction"
    }
}

# --- Program.cs wiring ---
$gatewayProgram = Resolve-RepoPath "src/Gateway/HRManagement.Gateway/Program.cs"
if (Test-Path $gatewayProgram) {
    $gwContent = Get-Content -Raw -Path $gatewayProgram
    if ($gwContent -notmatch "AddGovernmentConnectors") {
        $failures += "Gateway Program.cs not wiring AddGovernmentConnectors"
    }
} else {
    $failures += "Gateway Program.cs not found"
}

$coreProgram = Resolve-RepoPath "src/Services/HRCore/HR.Core.Web/Program.cs"
if (Test-Path $coreProgram) {
    $coreContent = Get-Content -Raw -Path $coreProgram
    if ($coreContent -notmatch "AddGovernmentConnectors") {
        $failures += "HR.Core Program.cs not wiring AddGovernmentConnectors"
    }
} else {
    $failures += "HR.Core Program.cs not found"
}

# --- Documentation check ---
$docPath = Resolve-RepoPath "docs/INT_004_CONNECTORS.md"
if (-not (Test-Path $docPath)) {
    $failures += "missing docs/INT_004_CONNECTORS.md"
}

# --- Test file check ---
$testPath = Resolve-RepoPath "src/Tests/HRManagement.UnitTests/Int004ConnectorTests.cs"
if (-not (Test-Path $testPath)) {
    $failures += "missing Int004ConnectorTests.cs"
}

# --- EXT stubs check ---
$extModules = @("ExtFederation", "ExtCivil", "ExtTreasury", "ExtPki", "ExtApi", "ExtSandbox", "ExtSector")
$sdRoot = Resolve-RepoPath "src/Shared/HRManagement.ServiceDefaults"
foreach ($mod in $extModules) {
    $modDir = Join-Path $sdRoot $mod
    if (-not (Test-Path $modDir)) {
        $failures += "missing EXT stub module: $mod"
    }
}

# --- Report ---
$outDir = Join-Path $RepoRoot "testdata/int004/out"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$report = [ordered]@{
    card         = "INT-004"
    ok           = ($failures.Count -eq 0)
    failureCount = $failures.Count
    failures     = $failures
    connectors   = @("treasury", "civil-registry", "federation")
    modes        = @("File", "Stub", "Sandbox", "Live")
}
$reportPath = Join-Path $outDir "validate-report.json"
($report | ConvertTo-Json -Depth 5) | Set-Content -Path $reportPath -Encoding utf8
Write-Host "Wrote $reportPath"

if ($failures.Count -gt 0) {
    Write-Host "FAIL:"
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host "PASS: INT-004 government connectors validate"
exit 0
