# AC-26: verify encrypted Employee.NationalId storage, deterministic hashes, and log redaction.
param(
    [string]$PostgresContainer = "",
    [string]$HrCoreWebContainer = "",
    [string]$DbName = "hr_core_db",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$knownIds = @(
    "99100000327",
    "99100000328",
    "99100000598",
    "99200000001"
)

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $evidenceRoot = if ($env:ACCEPTANCE_EVIDENCE_DIR) {
        $env:ACCEPTANCE_EVIDENCE_DIR
    } else {
        Join-Path $root "artifacts/acceptance/evidence"
    }
    $ReportPath = Join-Path $evidenceRoot "AC-26/pii-scan-report.json"
}

$report = [ordered]@{
    ok = $true
    scannedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    checks = @()
    warnings = @()
}

function Add-Check([string]$Name, [bool]$Passed, [object]$Detail) {
    $report.checks += [ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    }
    if (-not $Passed) { $report.ok = $false }
}

function Resolve-AcceptanceContainer([string]$Service) {
    return docker ps `
        --filter "label=com.docker.compose.project=hrm-acceptance" `
        --filter "label=com.docker.compose.service=$Service" `
        --format "{{.Names}}" | Select-Object -First 1
}

function Invoke-PostgresScalar([string]$Sql) {
    $output = docker exec $PostgresContainer psql -U hrm_ci -d $DbName -t -A -c $Sql 2>&1
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL query failed: $output" }
    return ($output | Out-String).Trim()
}

try {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw "docker CLI not found"
    }

    if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
        $PostgresContainer = Resolve-AcceptanceContainer "postgresql"
    }
    if ([string]::IsNullOrWhiteSpace($HrCoreWebContainer)) {
        $HrCoreWebContainer = Resolve-AcceptanceContainer "hr-core-web"
    }
    if ([string]::IsNullOrWhiteSpace($PostgresContainer)) {
        throw "The hrm-acceptance PostgreSQL container is not running."
    }

    $plaintextCount = Invoke-PostgresScalar @'
SELECT COUNT(*) FROM "Employees" WHERE "NationalId" ~ '^[0-9]{9,11}$';
'@
    Add-Check "employees-national-id-no-digit-plaintext" ($plaintextCount -eq "0") @{
        plaintextDigitRows = [int]$plaintextCount
    }

    $nullHashCount = Invoke-PostgresScalar @'
SELECT COUNT(*) FROM "Employees" WHERE "NationalIdHash" IS NULL OR "NationalIdHash" = '';
'@
    Add-Check "employees-national-id-hash-present" ($nullHashCount -eq "0") @{
        missingHashRows = [int]$nullHashCount
    }

    foreach ($id in $knownIds) {
        $hash = [Convert]::ToHexString(
            [System.Security.Cryptography.SHA256]::HashData(
                [System.Text.Encoding]::UTF8.GetBytes($id))).ToLowerInvariant()
        $found = Invoke-PostgresScalar "SELECT COUNT(*) FROM `"Employees`" WHERE `"NationalIdHash`" = '$hash';"
        Add-Check "acceptance-hash-$id" ([int]$found -eq 1) @{
            hash = $hash
            matches = [int]$found
        }
    }

    $recruitmentPlaintext = Invoke-PostgresScalar @'
SELECT COUNT(*) FROM "RecruitmentApplications" WHERE "NationalId" ~ '^[0-9]{9,11}$';
'@
    if ([int]$recruitmentPlaintext -ne 0) {
        $report.warnings += "RecruitmentApplications contains $recruitmentPlaintext digit-only NationalId rows outside the Employee AC-26 scope."
    }

    if ([string]::IsNullOrWhiteSpace($HrCoreWebContainer)) {
        Add-Check "hr-core-logs-no-known-national-ids" $false @{ error = "hr-core-web container not running" }
    } else {
        $logs = docker logs $HrCoreWebContainer --tail 5000 2>&1 | Out-String
        $matchedIds = @($knownIds | Where-Object { $logs.Contains($_) })
        Add-Check "hr-core-logs-no-known-national-ids" ($matchedIds.Count -eq 0) @{
            matches = $matchedIds
        }
    }
}
catch {
    Add-Check "scan-exception" $false @{ error = $_.Exception.Message }
}

$dir = Split-Path -Parent $ReportPath
if ($dir -and -not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
}
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output ($report | ConvertTo-Json -Compress -Depth 8)
if (-not $report.ok) { exit 1 }
exit 0
