#Requires -Version 7.0
<#
.SYNOPSIS
  AC-20: verifies that every service role can connect only to its own database.
#>
param(
    [string]$ComposeProjectName = $(if ($env:COMPOSE_PROJECT_NAME) { $env:COMPOSE_PROJECT_NAME } else { "hrm-acceptance" }),
    [string]$EvidenceDir = $(if ($env:ACCEPTANCE_EVIDENCE_DIR) { $env:ACCEPTANCE_EVIDENCE_DIR } else { "artifacts/acceptance/evidence" })
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
. "$PSScriptRoot/acceptance-local-env.ps1"
Import-AcceptanceLocalEnvironment -RepoRoot $root
$composeFiles = @(
    "docker-compose.yml",
    "docker-compose.services.yml",
    "docker-compose.dev-profiles.yml",
    "docker-compose.full-stack-gateway.yml",
    "docker-compose.acceptance.yml"
)
$composeArgs = @()
foreach ($file in $composeFiles) { $composeArgs += @("-f", (Join-Path $root $file)) }

$container = (& docker compose -p $ComposeProjectName @composeArgs ps -q postgresql | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($container)) {
    throw "The $ComposeProjectName PostgreSQL container is not running."
}

$roles = [ordered]@{
    hr_core_app = "hr_core_db"
    hr_contracts_app = "hr_contracts_db"
    hr_health_app = "hr_health_db"
    hr_movements_app = "hr_movements_db"
    hr_training_app = "hr_training_db"
    payroll_app = "payroll_db"
    idcard_app = "idcard_db"
    archive_app = "archive_db"
    admin_app = "admin_db"
    it_infra_app = "it_infra_db"
    analytics_app = "analytics_db"
    compliance_app = "compliance_db"
    auth_directory_app = "auth_directory_db"
    file_storage_app = "file_storage_db"
    gateway_audit_app = "gateway_audit_db"
}
$databases = @($roles.Values)
$violations = [System.Collections.Generic.List[object]]::new()
$checkedPairs = 0

foreach ($role in $roles.Keys) {
    foreach ($database in $databases) {
        $checkedPairs++
        $sql = "select has_database_privilege('$role', '$database', 'CONNECT');"
        $actual = (& docker exec $container psql -X -U hrm_ci -d postgres -Atc $sql | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw "Unable to inspect $role on $database." }
        $expected = $roles[$role] -eq $database
        if (($actual -eq "t") -ne $expected) {
            $violations.Add([pscustomobject]@{
                role = $role
                database = $database
                expectedConnect = $expected
                actualConnect = ($actual -eq "t")
            })
        }
    }
}

$previousPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$negativeOutput = & docker exec $container sh -lc 'PGPASSWORD="$POSTGRES_APP_PASSWORD" psql -h 127.0.0.1 -U hr_core_app -d payroll_db -Atc "select 1"' 2>&1
$negativeExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousPreference
if ($negativeExitCode -eq 0) {
    $violations.Add([pscustomobject]@{
        role = "hr_core_app"
        database = "payroll_db"
        expectedConnect = $false
        actualConnect = $true
        source = "live connection"
    })
}

$evidenceRoot = if ([System.IO.Path]::IsPathRooted($EvidenceDir)) {
    $EvidenceDir
} else {
    Join-Path $root $EvidenceDir
}
$ac20Dir = Join-Path $evidenceRoot "AC-20"
New-Item -ItemType Directory -Force -Path $ac20Dir | Out-Null
$logPath = Join-Path $ac20Dir "verify-roles.log"
$log = [System.Text.StringBuilder]::new()
[void]$log.AppendLine("AC-20 PostgreSQL service-role isolation")
[void]$log.AppendLine("Compose project: $ComposeProjectName")
[void]$log.AppendLine("Checked role/database pairs: $checkedPairs")
[void]$log.AppendLine("Violations: $($violations.Count)")
if ($violations.Count -eq 0) {
    [void]$log.AppendLine("All cross-DB CONNECT checks passed")
} else {
    [void]$log.AppendLine(($violations | ConvertTo-Json -Depth 5))
}
$log.ToString() | Set-Content -LiteralPath $logPath -Encoding utf8

[pscustomobject][ordered]@{
    attempted = "hr_core_app -> payroll_db"
    expected = "connection denied"
    actualExitCode = $negativeExitCode
    output = ($negativeOutput | Out-String).Trim()
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $ac20Dir "cross-db-attempt.json") -Encoding utf8

Write-Host $log.ToString()
Write-Host "Evidence: $ac20Dir"
if ($violations.Count -gt 0) { exit 1 }
exit 0
