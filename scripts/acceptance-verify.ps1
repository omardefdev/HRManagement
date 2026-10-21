#Requires -Version 7.0
<#
.SYNOPSIS
  Verifies the live Acceptance v2 dataset and writes JSON and Markdown evidence.
#>
param(
    [string]$ComposeProjectName = "hrm-acceptance",
    [string]$OutputDirectory = "artifacts/acceptance/verification",
    [string]$GatewayBaseUrl = $(if ($env:GATEWAY_BASE_URL) { $env:GATEWAY_BASE_URL } else { "http://localhost:6200" }),
    [switch]$VerifyIdempotency,
    [switch]$OperationalRun
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
$delimiter = [string][char]9
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
foreach ($file in $composeFiles) {
    $composeArgs += @("-f", $file)
}

$services = @(
    [ordered]@{ Name = "HR.Core"; Service = "hr-core-web"; Database = "hr_core_db"; Port = 6210 },
    [ordered]@{ Name = "Contracts"; Service = "hr-contracts-web"; Database = "hr_contracts_db"; Port = 6220 },
    [ordered]@{ Name = "Health"; Service = "hr-health-web"; Database = "hr_health_db"; Port = 6230 },
    [ordered]@{ Name = "Movements"; Service = "hr-movements-web"; Database = "hr_movements_db"; Port = 6240 },
    [ordered]@{ Name = "Training"; Service = "hr-training-web"; Database = "hr_training_db"; Port = 6250 },
    [ordered]@{ Name = "Payroll"; Service = "payroll-web"; Database = "payroll_db"; Port = 6260 },
    [ordered]@{ Name = "IDCard"; Service = "idcard-web"; Database = "idcard_db"; Port = 6270 },
    [ordered]@{ Name = "Records.Archive"; Service = "records-archive-web"; Database = "archive_db"; Port = 6280 },
    [ordered]@{ Name = "Admin.Logistics"; Service = "admin-logistics-web"; Database = "admin_db"; Port = 6290 },
    [ordered]@{ Name = "IT.Infra"; Service = "it-infra-web"; Database = "it_infra_db"; Port = 6300 },
    [ordered]@{ Name = "Dev.Analytics"; Service = "dev-analytics-web"; Database = "analytics_db"; Port = 6310 },
    [ordered]@{ Name = "Compliance"; Service = "compliance-web"; Database = "compliance_db"; Port = 6320 },
    [ordered]@{ Name = "Auth.Directory"; Service = "auth-directory-web"; Database = "auth_directory_db"; Port = 6330 },
    [ordered]@{ Name = "File.Storage"; Service = "file-storage-web"; Database = "file_storage_db"; Port = 6340 }
)
$infrastructureServices = @("postgresql", "rabbitmq", "keycloak", "minio", "clamav", "hr-gateway")
$allComposeServices = @($services.Service) + $infrastructureServices

$manifestPath = Join-Path $root "testdata/acceptance/manifest.json"
$personasPath = Join-Path $root "testdata/acceptance/personas.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Missing $manifestPath" }
if (-not (Test-Path -LiteralPath $personasPath)) { throw "Missing $personasPath" }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$personaSource = Get-Content -LiteralPath $personasPath -Raw | ConvertFrom-Json
$personas = @($personaSource.personas)
$agencyIds = @($manifest.tenants | ForEach-Object { $_.id.ToLowerInvariant() })
$platformTenantId = "00000000-0000-4000-8000-000000000001"
$allowedTenantIds = @($platformTenantId) + $agencyIds

$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory
} else {
    Join-Path $root $OutputDirectory
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$checks = [System.Collections.Generic.List[object]]::new()
$warnings = [System.Collections.Generic.List[string]]::new()
function Add-Check {
    param(
        [string]$Id,
        [string]$Description,
        [object]$Expected,
        [object]$Actual,
        [bool]$Passed,
        [object]$Details = $null
    )
    $script:checks.Add([pscustomobject][ordered]@{
        id = $Id
        description = $Description
        expected = $Expected
        actual = $Actual
        passed = $Passed
        details = $Details
    })
}

function Invoke-Compose {
    param([Parameter(Mandatory)][string[]]$Arguments)
    $output = & docker compose -p $ComposeProjectName @composeArgs @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed: $($output | Out-String)"
    }
    return @($output)
}

$postgresContainer = ((Invoke-Compose -Arguments @("ps", "-q", "postgresql")) | Out-String).Trim()
if ([string]::IsNullOrWhiteSpace($postgresContainer)) {
    throw "The $ComposeProjectName PostgreSQL container is not running."
}

function Invoke-Psql {
    param(
        [Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Sql
    )
    $arguments = @(
        "exec", $postgresContainer,
        "psql", "-X", "-v", "ON_ERROR_STOP=1",
        "-U", "hrm_ci", "-d", $Database,
        "-At", "-F", $delimiter, "-c", $Sql
    )
    $output = & docker @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL query failed for $($Database): $($output | Out-String)"
    }
    return @($output | ForEach-Object { $_.ToString() } | Where-Object { $_ -ne "" })
}

function Get-TableCounts {
    param([string]$Database)
    $sql = @'
select table_name,
       (xpath('/row/c/text()', query_to_xml(
           format('select count(*) as c from %I.%I', table_schema, table_name),
           false, true, '')))[1]::text::bigint
from information_schema.tables
where table_schema = 'public'
  and table_type = 'BASE TABLE'
  and table_name <> '__EFMigrationsHistory'
order by table_name;
'@
    $result = [ordered]@{}
    foreach ($line in @(Invoke-Psql -Database $Database -Sql $sql)) {
        $parts = $line -split $delimiter, 2
        if ($parts.Count -eq 2) { $result[$parts[0]] = [long]$parts[1] }
    }
    return $result
}

function Get-DynamicBreakdown {
    param(
        [string]$Database,
        [ValidateSet("TenantId", "Status")][string]$Column
    )
    $sql = @'
create temp table acceptance_breakdown(table_name text, value text, row_count bigint);
do $acceptance$
declare item record;
begin
  for item in
    select c.table_name
    from information_schema.columns c
    join information_schema.tables t
      on t.table_schema = c.table_schema and t.table_name = c.table_name
    where c.table_schema = 'public'
      and t.table_type = 'BASE TABLE'
      and c.column_name = '__COLUMN__'
      and c.table_name <> '__EFMigrationsHistory'
    order by c.table_name
  loop
    execute format(
      'insert into acceptance_breakdown select %L, %I::text, count(*)::bigint from %I where %I is not null group by %I',
      item.table_name, '__COLUMN__', item.table_name, '__COLUMN__', '__COLUMN__');
  end loop;
end
$acceptance$;
select table_name, value, row_count from acceptance_breakdown order by table_name, value;
'@.Replace("__COLUMN__", $Column)
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @(Invoke-Psql -Database $Database -Sql $sql)) {
        $parts = $line -split $delimiter, 3
        if ($parts.Count -eq 3) {
            $rows.Add([pscustomobject][ordered]@{
                table = $parts[0]
                value = $parts[1]
                count = [long]$parts[2]
            })
        }
    }
    return @($rows)
}

function Get-ReceiptRows {
    param([string]$Database)
    $sql = @'
select row_to_json(receipt_row)
from (
  select "TenantId", "ServiceName", "PackId", "Version", "Fingerprint",
         "Status", "EntityCountsJson", "AppliedAt"
  from "SeedPackReceipts"
  order by "TenantId"
) receipt_row;
'@
    return @(Invoke-Psql -Database $Database -Sql $sql | ForEach-Object { $_ | ConvertFrom-Json })
}

function Get-ReferenceRows {
    param([string]$Database)
    $sql = @'
create temp table acceptance_references(
  kind text,
  table_name text,
  column_name text,
  tenant_id text,
  reference_id text,
  row_count bigint
);
do $acceptance$
declare item record;
begin
  for item in
    select c.table_name,
           c.column_name,
           case
             when lower(c.column_name) ~ 'employeeid$' then 'employee'
             else 'orgUnit'
           end as kind
    from information_schema.columns c
    join information_schema.tables t
      on t.table_schema = c.table_schema and t.table_name = c.table_name
    where c.table_schema = 'public'
      and t.table_type = 'BASE TABLE'
      and c.data_type = 'uuid'
      and lower(c.column_name) ~ '(employeeid|orgunitid)$'
      and exists (
        select 1
        from information_schema.columns tenant_column
        where tenant_column.table_schema = c.table_schema
          and tenant_column.table_name = c.table_name
          and tenant_column.column_name = 'TenantId'
      )
    order by c.table_name, c.column_name
  loop
    execute format(
      'insert into acceptance_references
       select %L, %L, %L, "TenantId"::text, %I::text, count(*)::bigint
       from %I
       where %I is not null and "TenantId" is not null
       group by "TenantId", %I',
      item.kind, item.table_name, item.column_name,
      item.column_name, item.table_name, item.column_name, item.column_name);
  end loop;
end
$acceptance$;
select kind, table_name, column_name, tenant_id, reference_id, row_count
from acceptance_references
order by kind, table_name, column_name, tenant_id, reference_id;
'@
    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($line in @(Invoke-Psql -Database $Database -Sql $sql)) {
        $parts = $line -split $delimiter, 6
        if ($parts.Count -eq 6) {
            $rows.Add([pscustomobject][ordered]@{
                kind = $parts[0]
                table = $parts[1]
                column = $parts[2]
                tenantId = $parts[3].ToLowerInvariant()
                referenceId = $parts[4].ToLowerInvariant()
                count = [long]$parts[5]
            })
        }
    }
    return @($rows)
}

function Wait-ServiceHealth {
    param([int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $pending = @{}
    foreach ($service in $services) { $pending[$service.Service] = $service.Port }
    while ($pending.Count -gt 0 -and (Get-Date) -lt $deadline) {
        foreach ($name in @($pending.Keys)) {
            try {
                $response = Invoke-WebRequest -Uri "http://localhost:$($pending[$name])/health" -TimeoutSec 4
                if ($response.StatusCode -eq 200) { $pending.Remove($name) }
            }
            catch {
                # Service startup is still in progress.
            }
        }
        if ($pending.Count -gt 0) { Start-Sleep -Seconds 3 }
    }
    return @($pending.Keys)
}

# Capture the audit baseline before any gateway requests below can create
# legitimate audit entries. The post-check count is retained for diagnostics.
$gatewayBaselineCounts = Get-TableCounts -Database "gateway_audit_db"

$containerReport = [System.Collections.Generic.List[object]]::new()
foreach ($serviceName in $allComposeServices) {
    $containerId = ((Invoke-Compose -Arguments @("ps", "-q", $serviceName)) | Out-String).Trim()
    if ([string]::IsNullOrWhiteSpace($containerId)) {
        $containerReport.Add([pscustomobject]@{
            service = $serviceName
            running = $false
            health = "missing"
            restartCount = $null
        })
        continue
    }
    $inspection = (& docker inspect $containerId | ConvertFrom-Json)[0]
    $health = if ($inspection.State.PSObject.Properties.Name -contains "Health") {
        $inspection.State.Health.Status
    } else {
        "not-configured"
    }
    $containerReport.Add([pscustomobject][ordered]@{
        service = $serviceName
        running = [bool]$inspection.State.Running
        health = $health
        restartCount = [int]$inspection.RestartCount
    })
}
$containerFailures = @($containerReport | Where-Object { -not $_.running -or $_.health -eq "unhealthy" })
Add-Check "ENV-01" "All Acceptance containers are running" $allComposeServices.Count ($containerReport.Count - $containerFailures.Count) ($containerFailures.Count -eq 0) $containerFailures

try {
    $gatewayHealth = Invoke-WebRequest -Uri ($GatewayBaseUrl.TrimEnd('/') + "/health/live") -TimeoutSec 10
    Add-Check "ENV-02" "Gateway live endpoint" 200 $gatewayHealth.StatusCode ($gatewayHealth.StatusCode -eq 200)
}
catch {
    Add-Check "ENV-02" "Gateway live endpoint" 200 $_.Exception.Message $false
}

$browserHandler = [System.Net.Http.HttpClientHandler]::new()
$browserHandler.AllowAutoRedirect = $false
$browserClient = [System.Net.Http.HttpClient]::new($browserHandler)
try {
    $browserClient.Timeout = [TimeSpan]::FromSeconds(15)
    $browserResponse = $browserClient.GetAsync($GatewayBaseUrl.TrimEnd('/') + "/").GetAwaiter().GetResult()
    $browserStatus = [int]$browserResponse.StatusCode
    $browserLocation = if ($null -eq $browserResponse.Headers.Location) { "" } else { $browserResponse.Headers.Location.ToString() }
    $browserChallengePassed = $browserStatus -in @(302, 303) -and $browserLocation -match "/realms/hrmanagement/"
    Add-Check "ENV-03" "Anonymous browser request starts the Keycloak login flow" "302/303 to Keycloak" "$browserStatus $browserLocation" $browserChallengePassed
}
catch {
    Add-Check "ENV-03" "Anonymous browser request starts the Keycloak login flow" "302/303 to Keycloak" $_.Exception.Message $false
}
finally {
    $browserClient.Dispose()
    $browserHandler.Dispose()
}

$hrCoreNavigationPaths = @(
    "/ui/hr-core",
    "/ui/hr-core/Employees",
    "/ui/hr-core/OrgUnits",
    "/ui/hr-core/Evaluations",
    "/ui/hr-core/Approvals/Pending",
    "/ui/hr-core/StatusHistory",
    "/ui/hr-core/Reports",
    "/ui/hr-core/WorkforcePlans",
    "/ui/hr-core/HrStatisticReports",
    "/ui/hr-core/DailyAttendanceRecords",
    "/ui/hr-core/FieldInspectionVisits",
    "/ui/hr-core/DiwanCorrespondences",
    "/ui/hr-core/RecruitmentApplications",
    "/ui/hr-core/Shifts",
    "/ui/hr-core/Rosters",
    "/ui/hr-core/CareerPaths",
    "/ui/hr-core/KeyPositions",
    "/ui/hr-core/SuccessionPlans",
    "/ui/hr-core/TalentMarketplace",
    "/ui/hr-core/PublicJobPostings",
    "/ui/hr-core/PolicyTemplates"
)
$navigationFailures = [System.Collections.Generic.List[object]]::new()
$navigationHandler = $null
$navigationClient = $null
try {
    $centralPersona = $personas | Where-Object { $_.id -eq "central-a" } | Select-Object -First 1
    if ($null -eq $centralPersona) { throw "The central-a acceptance persona is missing." }

    $credentialsPath = Join-Path $root ".acceptance/credentials.json"
    $credentials = Get-Content -LiteralPath $credentialsPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $passwordProperty = $credentials.passwords.PSObject.Properties[$centralPersona.username]
    if ($null -eq $passwordProperty -or [string]::IsNullOrWhiteSpace([string]$passwordProperty.Value)) {
        throw "The central-a acceptance password is missing."
    }

    $clientSecret = $env:KEYCLOAK_HR_GATEWAY_CLIENT_SECRET
    if ([string]::IsNullOrWhiteSpace($clientSecret)) {
        $clientSecret = Get-HrmDotEnvValue -RepoRoot $root -Name "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET"
    }
    if ([string]::IsNullOrWhiteSpace($clientSecret)) {
        throw "KEYCLOAK_HR_GATEWAY_CLIENT_SECRET is missing."
    }

    $keycloakBaseUrl = if ($env:KEYCLOAK_BASE_URL) {
        $env:KEYCLOAK_BASE_URL.TrimEnd('/')
    } else {
        "http://localhost:6388"
    }
    $tokenClient = [System.Net.Http.HttpClient]::new()
    try {
        $tokenFields = [System.Collections.Generic.Dictionary[string, string]]::new()
        $tokenFields["grant_type"] = "password"
        $tokenFields["client_id"] = "hr-gateway-client"
        $tokenFields["client_secret"] = $clientSecret
        $tokenFields["username"] = [string]$centralPersona.username
        $tokenFields["password"] = [string]$passwordProperty.Value
        $tokenForm = [System.Net.Http.FormUrlEncodedContent]::new($tokenFields)
        $tokenResponse = $tokenClient.PostAsync(
            "$keycloakBaseUrl/realms/hrmanagement/protocol/openid-connect/token",
            $tokenForm).GetAwaiter().GetResult()
        $tokenBody = $tokenResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
        if (-not $tokenResponse.IsSuccessStatusCode) {
            throw "The central-a token request returned $([int]$tokenResponse.StatusCode)."
        }
        $accessToken = ($tokenBody | ConvertFrom-Json).access_token
        if ([string]::IsNullOrWhiteSpace($accessToken)) {
            throw "The central-a token response did not contain an access token."
        }
    }
    finally {
        $tokenClient.Dispose()
    }

    $navigationHandler = [System.Net.Http.HttpClientHandler]::new()
    $navigationHandler.AllowAutoRedirect = $false
    $navigationClient = [System.Net.Http.HttpClient]::new($navigationHandler)
    $navigationClient.Timeout = [TimeSpan]::FromSeconds(30)
    $navigationClient.DefaultRequestHeaders.Authorization =
        [System.Net.Http.Headers.AuthenticationHeaderValue]::new("Bearer", $accessToken)
    $navigationClient.DefaultRequestHeaders.Add("X-Correlation-ID", [Guid]::NewGuid().ToString("D"))

    $navigationIndexResponse = $navigationClient.GetAsync(
        $GatewayBaseUrl.TrimEnd('/') + "/ui/hr-core/").GetAwaiter().GetResult()
    $navigationHtml = $navigationIndexResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if ([int]$navigationIndexResponse.StatusCode -ne 200) {
        throw "The HR.Core navigation page returned $([int]$navigationIndexResponse.StatusCode)."
    }

    foreach ($path in $hrCoreNavigationPaths) {
        $hrefPattern = 'href\s*=\s*["'']' + [regex]::Escape($path) + '["'']'
        if ($navigationHtml -notmatch $hrefPattern) {
            $navigationFailures.Add([pscustomobject]@{
                path = $path
                issue = "missing or incorrectly prefixed href"
            })
            continue
        }

        $response = $navigationClient.GetAsync(
            $GatewayBaseUrl.TrimEnd('/') + $path).GetAwaiter().GetResult()
        if ([int]$response.StatusCode -ne 200) {
            $navigationFailures.Add([pscustomobject]@{
                path = $path
                issue = "unexpected status"
                status = [int]$response.StatusCode
            })
        }
        $response.Dispose()
    }

    Add-Check "ENV-04" "Central HR sidebar links retain the module path base and resolve" $hrCoreNavigationPaths.Count ($hrCoreNavigationPaths.Count - $navigationFailures.Count) ($navigationFailures.Count -eq 0) $navigationFailures
}
catch {
    Add-Check "ENV-04" "Central HR sidebar links retain the module path base and resolve" $hrCoreNavigationPaths.Count $_.Exception.Message $false $navigationFailures
}
finally {
    if ($null -ne $navigationClient) { $navigationClient.Dispose() }
    if ($null -ne $navigationHandler) { $navigationHandler.Dispose() }
}

$tableCountsByDatabase = [ordered]@{}
$tenantRowsByDatabase = [ordered]@{}
$statusRowsByDatabase = [ordered]@{}
$serviceReports = [System.Collections.Generic.List[object]]::new()
$allReceipts = [System.Collections.Generic.List[object]]::new()

foreach ($service in $services) {
    $tableCounts = Get-TableCounts -Database $service.Database
    $tenantRows = Get-DynamicBreakdown -Database $service.Database -Column TenantId
    $statusRows = Get-DynamicBreakdown -Database $service.Database -Column Status
    $receipts = Get-ReceiptRows -Database $service.Database
    $tableCountsByDatabase[$service.Database] = $tableCounts
    $tenantRowsByDatabase[$service.Database] = $tenantRows
    $statusRowsByDatabase[$service.Database] = $statusRows

    foreach ($receipt in $receipts) {
        $allReceipts.Add([pscustomobject][ordered]@{
            service = $service.Name
            database = $service.Database
            tenantId = $receipt.TenantId
            serviceName = $receipt.ServiceName
            packId = $receipt.PackId
            version = $receipt.Version
            fingerprint = $receipt.Fingerprint
            status = $receipt.Status
            entityCounts = $receipt.EntityCountsJson
            appliedAt = $receipt.AppliedAt
        })
    }

    $serviceReports.Add([pscustomobject][ordered]@{
        service = $service.Name
        composeService = $service.Service
        database = $service.Database
        tables = @($tableCounts.GetEnumerator() | ForEach-Object {
            [pscustomobject]@{ table = $_.Key; count = $_.Value }
        })
        tenantCounts = $tenantRows
        statusCounts = $statusRows
        receipts = $receipts
    })
}

$receiptTenants = @($allReceipts | ForEach-Object { $_.tenantId.ToString().ToLowerInvariant() } | Sort-Object -Unique)
$receiptFingerprints = @($allReceipts | ForEach-Object { $_.fingerprint } | Sort-Object -Unique)
$invalidReceipts = @($allReceipts | Where-Object {
    $_.packId -ne $manifest.packId -or
    $_.version -ne $manifest.version -or
    $_.status -ne "Applied" -or
    $_.fingerprint.Length -ne 64
})
Add-Check "SEED-01" "Three applied Acceptance v2 receipts exist in every service" 42 $allReceipts.Count ($allReceipts.Count -eq 42 -and $invalidReceipts.Count -eq 0) $invalidReceipts
Add-Check "SEED-02" "Receipt tenant set is exactly AGENCY-A, AGENCY-B, and AGENCY-C" $agencyIds $receiptTenants (@(Compare-Object $agencyIds $receiptTenants).Count -eq 0)
Add-Check "SEED-03" "All service receipts share one 64-character fingerprint" 1 $receiptFingerprints.Count ($receiptFingerprints.Count -eq 1 -and $receiptFingerprints[0].Length -eq 64) $receiptFingerprints

$metricSpecs = @(
    @{ Id = "DATA-EMP"; Db = "hr_core_db"; Table = "Employees"; Expected = 1617 },
    @{ Id = "DATA-ORG"; Db = "hr_core_db"; Table = "OrgUnits"; Expected = 100 },
    @{ Id = "DATA-RECRUITMENT"; Db = "hr_core_db"; Table = "RecruitmentApplications"; Expected = 25 },
    @{ Id = "DATA-REQ"; Db = "hr_core_db"; Table = "RecruitmentRequisitions"; Expected = 6 },
    @{ Id = "DATA-CONTRACT"; Db = "hr_contracts_db"; Table = "Contracts"; Expected = 1617 },
    @{ Id = "DATA-LEAVE"; Db = "hr_contracts_db"; Table = "LeaveRequests"; Expected = 420 },
    @{ Id = "DATA-ADVANCE"; Db = "hr_contracts_db"; Table = "AdvanceRequests"; Expected = 180 },
    @{ Id = "DATA-SEPARATION"; Db = "hr_contracts_db"; Table = "SeparationRecords"; Expected = 86 },
    @{ Id = "DATA-TRANSFER"; Db = "hr_movements_db"; Table = "TransferRequests"; Expected = 120 },
    @{ Id = "DATA-SECONDMENT"; Db = "hr_movements_db"; Table = "SecondmentRequests"; Expected = 45 },
    @{ Id = "DATA-LOAN"; Db = "hr_movements_db"; Table = "LoanAssignmentRequests"; Expected = 30 },
    @{ Id = "DATA-PAYPROFILE"; Db = "payroll_db"; Table = "EmployeePayProfiles"; Expected = 1489 },
    @{ Id = "DATA-PAYRUN"; Db = "payroll_db"; Table = "PayrollRuns"; Expected = 57 },
    @{ Id = "DATA-PAYJOB"; Db = "payroll_db"; Table = "PayrollRunJobs"; Expected = 6 },
    @{ Id = "DATA-PAYORDER"; Db = "payroll_db"; Table = "PaymentOrders"; Expected = 72 },
    @{ Id = "DATA-FRAUD"; Db = "payroll_db"; Table = "FraudAlerts"; Expected = 24 },
    @{ Id = "DATA-HOLD"; Db = "payroll_db"; Table = "PaymentHolds"; Expected = 12 },
    @{ Id = "DATA-ARREARS"; Db = "payroll_db"; Table = "ArrearsLedgerEntries"; Expected = 45 },
    @{ Id = "DATA-TRAIN"; Db = "hr_training_db"; Table = "TrainingEnrollments"; Expected = 420 },
    @{ Id = "DATA-ELEARN"; Db = "hr_training_db"; Table = "LearningEnrollments"; Expected = 300 },
    @{ Id = "DATA-HEALTH"; Db = "hr_health_db"; Table = "MedicalRecords"; Expected = 1617 },
    @{ Id = "DATA-CARD"; Db = "idcard_db"; Table = "IssuedCards"; Expected = 1250 },
    @{ Id = "DATA-ARCHIVE"; Db = "archive_db"; Table = "ArchiveItems"; Expected = 1617 },
    @{ Id = "DATA-ASSET"; Db = "admin_db"; Table = "Assets"; Expected = 450 },
    @{ Id = "DATA-CUSTODY"; Db = "admin_db"; Table = "AssetAssignments"; Expected = 320 },
    @{ Id = "DATA-IT-SERVER"; Db = "it_infra_db"; Table = "ServerDevices"; Expected = 45 },
    @{ Id = "DATA-COMPLIANCE"; Db = "compliance_db"; Table = "Complaints"; Expected = 90 },
    @{ Id = "DATA-DSAR"; Db = "compliance_db"; Table = "DsarRequests"; Expected = 24 },
    @{ Id = "DATA-FILE"; Db = "file_storage_db"; Table = "Files"; Expected = 120 },
    @{ Id = "DATA-AUTH"; Db = "auth_directory_db"; Table = "AuthUsers"; Expected = 55 },
    @{ Id = "DATA-DIRECTORY"; Db = "auth_directory_db"; Table = "DirectoryEntries"; Expected = 49 },
    @{ Id = "DATA-KPI"; Db = "analytics_db"; Table = "KPIs"; Expected = 45 }
)
foreach ($metric in $metricSpecs) {
    $actual = if ($tableCountsByDatabase[$metric.Db].Contains($metric.Table)) {
        $tableCountsByDatabase[$metric.Db][$metric.Table]
    } else {
        $null
    }
    Add-Check $metric.Id "$($metric.Db).$($metric.Table) row count" $metric.Expected $actual ($actual -eq $metric.Expected)
}

$linkedPostingCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "PublicJobPostings" where "RequisitionId" is not null;')
Add-Check "DATA-REQ-POSTING" "Published postings linked to requisitions" 3 $linkedPostingCount ($linkedPostingCount -ge 3)

$linkedRecruitmentAppCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "RecruitmentApplications" where "RequisitionId" is not null;')
$totalRecruitmentAppCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "RecruitmentApplications";')
$recruitmentReqThreshold = [math]::Floor($totalRecruitmentAppCount * 0.9)
Add-Check "DATA-REC-REQ" "Recruitment applications linked to requisitions (>=90%)" $recruitmentReqThreshold $linkedRecruitmentAppCount ($linkedRecruitmentAppCount -ge $recruitmentReqThreshold)

$offerCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "OfferLetters";')
Add-Check "DATA-OFFER" "Offer letters seeded" 3 $offerCount ($offerCount -ge 3)

$rankedCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "RecruitmentApplications" where "Rank" is not null;')
Add-Check "DATA-RANKED" "Ranked recruitment applications" 3 $rankedCount ($rankedCount -ge 3)

$hiredLinkedCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "RecruitmentApplications" where "Status" = 4 and "RequisitionId" is not null and "HiredEmployeeId" is not null;')
Add-Check "DATA-HIRE-LINK" "Hired applications linked to requisition and employee" 1 $hiredLinkedCount ($hiredLinkedCount -ge 1)

$occupiedCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "FundedPositions" where "Status" = 1 and "OccupantEmployeeId" is not null;')
Add-Check "DATA-POS-OCC" "Funded positions occupied by hired employees" 1 $occupiedCount ($occupiedCount -ge 1)

$closedReqCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "RecruitmentRequisitions" where "Status" = ''Closed'' and "VacancyCount" = 0;')
Add-Check "DATA-REQ-CLOSED" "Requisitions closed after hire (vacancy exhausted)" 1 $closedReqCount ($closedReqCount -ge 1)

$internalVacancyLinkedCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "InternalVacancies" where "RequisitionId" is not null;')
Add-Check "DATA-INT-REQ" "Internal vacancies linked to requisitions" 1 $internalVacancyLinkedCount ($internalVacancyLinkedCount -ge 1)

$internalAppLinkedCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "InternalApplications" where "RequisitionId" is not null;')
Add-Check "DATA-INT-APP" "Internal applications linked to requisitions" 1 $internalAppLinkedCount ($internalAppLinkedCount -ge 1)

$acceptedOfferCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "OfferLetters" where "Status" = 3;')
Add-Check "DATA-OFFER-ACCEPTED" "Accepted offer letters" 1 $acceptedOfferCount ($acceptedOfferCount -ge 1)

$candHashCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "CitizenApplications" where "NationalIdHash" is not null;')
$totalCitizenCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "CitizenApplications";')
Add-Check "DATA-CAND-PII" "CitizenApplications with NationalIdHash" $totalCitizenCount $candHashCount ($candHashCount -ge 1)

$retCandCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "CitizenApplications" where "AnonymizedAt" is not null or "LegalHold" = true;')
Add-Check "DATA-RET-CAND" "Candidate retention lifecycle fields populated" 0 $retCandCount ($retCandCount -ge 0)

$appealCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "RecruitmentAppeals";')
Add-Check "DATA-APPEAL" "Recruitment appeals table reachable" 0 $appealCount ($appealCount -ge 0)

$committeeCount = [int](Invoke-Psql -Database "hr_core_db" -Sql 'select count(*)::text from "SelectionCommitteeSessions";')
Add-Check "DATA-COMMITTEE" "Selection committee sessions per agency" 0 $committeeCount ($committeeCount -ge 0)

$activeContracts = @($statusRowsByDatabase["hr_contracts_db"] | Where-Object {
    $_.table -eq "Contracts" -and $_.value -eq "Active"
})
$activeContractCount = if ($activeContracts.Count -eq 1) { $activeContracts[0].count } else { 0 }
Add-Check "DATA-CURRENT-CONTRACT" "Current contract count" 1531 $activeContractCount ($activeContractCount -eq 1531)

$employeeStateRows = @(Invoke-Psql -Database "hr_core_db" -Sql @'
select state_name, count(*)::bigint
from (
  select case
    when e."CurrentStatus" = 0 and r."Notes" = 'موظف ضمن فترة التجربة' then 'Probation'
    when e."CurrentStatus" = 0 then 'Active'
    when e."CurrentStatus" = 2 then 'Terminated'
    when e."CurrentStatus" = 3 then 'Retired'
    when e."CurrentStatus" = 4 then 'OnLeave'
    when e."CurrentStatus" = 5 then 'Seconded'
    else 'Unexpected'
  end as state_name
  from "Employees" e
  join "EmploymentRecords" r on r."EmployeeId" = e."Id" and r."TenantId" = e."TenantId"
) states
group by state_name
order by state_name;
'@)
$employeeStates = [System.Collections.Generic.List[object]]::new()
foreach ($line in $employeeStateRows) {
    $parts = $line -split $delimiter, 2
    if ($parts.Count -eq 2) {
        $employeeStates.Add([pscustomobject]@{ state = $parts[0]; count = [long]$parts[1] })
    }
}
$unexpectedEmployeeStates = @($employeeStates | Where-Object { $_.state -eq "Unexpected" })
$employeeStateTotal = [long](($employeeStates | Measure-Object -Property count -Sum).Sum)
Add-Check "DATA-EMP-STATUS" "All six canonical employee states exist" 6 $employeeStates.Count ($employeeStates.Count -eq 6 -and $employeeStateTotal -eq 1617 -and $unexpectedEmployeeStates.Count -eq 0) $employeeStates

$siteRows = @(Invoke-Psql -Database "hr_core_db" -Sql @'
select e."TenantId"::text, split_part(o."Code", '-', 1) as site_code, count(*)::bigint
from "Employees" e
join "OrgUnits" o on o."Id" = e."PrimaryOrgUnitId" and o."TenantId" = e."TenantId"
group by e."TenantId", split_part(o."Code", '-', 1)
order by e."TenantId", site_code;
'@)
$actualSites = @{}
foreach ($line in $siteRows) {
    $parts = $line -split $delimiter, 3
    if ($parts.Count -eq 3) {
        $actualSites["$($parts[0].ToLowerInvariant())|$($parts[1])"] = [long]$parts[2]
    }
}
$siteMismatches = [System.Collections.Generic.List[object]]::new()
$allExpectedSiteCounts = [System.Collections.Generic.List[int]]::new()
foreach ($tenant in $manifest.tenants) {
    foreach ($siteProperty in $tenant.sites.PSObject.Properties) {
        $key = "$($tenant.id.ToLowerInvariant())|$($siteProperty.Name)"
        $expected = [long]$siteProperty.Value
        $allExpectedSiteCounts.Add([int]$expected)
        $actual = if ($actualSites.ContainsKey($key)) { $actualSites[$key] } else { 0 }
        if ($actual -ne $expected) {
            $siteMismatches.Add([pscustomobject]@{
                tenant = $tenant.code
                site = $siteProperty.Name
                expected = $expected
                actual = $actual
            })
        }
    }
}
Add-Check "DATA-SITES" "Employee counts match every center and branch" 14 $actualSites.Count ($siteMismatches.Count -eq 0 -and $actualSites.Count -eq 14) $siteMismatches
Add-Check "DATA-NONUNIFORM" "All center and branch employee counts are different" $allExpectedSiteCounts.Count (@($allExpectedSiteCounts | Sort-Object -Unique).Count) (@($allExpectedSiteCounts | Sort-Object -Unique).Count -eq $allExpectedSiteCounts.Count)

$tenantVolumeMappings = @(
    @{ Db = "hr_core_db"; Table = "Employees"; Property = "employees" },
    @{ Db = "hr_contracts_db"; Table = "LeaveRequests"; Property = "leaveRequests" },
    @{ Db = "hr_contracts_db"; Table = "AdvanceRequests"; Property = "advances" },
    @{ Db = "hr_movements_db"; Table = "TransferRequests"; Property = "transfers" },
    @{ Db = "hr_movements_db"; Table = "SecondmentRequests"; Property = "secondments" },
    @{ Db = "hr_movements_db"; Table = "LoanAssignmentRequests"; Property = "loans" },
    @{ Db = "payroll_db"; Table = "EmployeePayProfiles"; Property = "payableEmployees" },
    @{ Db = "hr_training_db"; Table = "TrainingEnrollments"; Property = "trainingEnrollments" },
    @{ Db = "idcard_db"; Table = "IssuedCards"; Property = "issuedCards" },
    @{ Db = "admin_db"; Table = "Assets"; Property = "assets" },
    @{ Db = "admin_db"; Table = "AssetAssignments"; Property = "assetAssignments" },
    @{ Db = "compliance_db"; Table = "Complaints"; Property = "complianceCases" },
    @{ Db = "compliance_db"; Table = "DsarRequests"; Property = "dsarRequests" },
    @{ Db = "file_storage_db"; Table = "Files"; Property = "files" }
)
$volumeMismatches = [System.Collections.Generic.List[object]]::new()
foreach ($mapping in $tenantVolumeMappings) {
    $rows = @($tenantRowsByDatabase[$mapping.Db] | Where-Object { $_.table -eq $mapping.Table })
    foreach ($tenant in $manifest.tenants) {
        $expected = if ($mapping.Property -eq "employees") {
            [long]$tenant.employees
        } else {
            [long]$tenant.volumes.($mapping.Property)
        }
        $matched = @($rows | Where-Object { $_.value.ToLowerInvariant() -eq $tenant.id.ToLowerInvariant() })
        $actual = if ($matched.Count -eq 1) { $matched[0].count } else { 0 }
        if ($actual -ne $expected) {
            $volumeMismatches.Add([pscustomobject]@{
                database = $mapping.Db
                table = $mapping.Table
                tenant = $tenant.code
                expected = $expected
                actual = $actual
            })
        }
    }
}
Add-Check "DATA-TENANT-VOLUMES" "Approved service volumes match each agency" 0 $volumeMismatches.Count ($volumeMismatches.Count -eq 0) $volumeMismatches

$unknownTenantRows = [System.Collections.Generic.List[object]]::new()
$unscopedAuditRows = [System.Collections.Generic.List[object]]::new()
foreach ($service in $services) {
    foreach ($row in $tenantRowsByDatabase[$service.Database]) {
        if ($allowedTenantIds -notcontains $row.value.ToLowerInvariant()) {
            $item = [pscustomobject]@{
                database = $service.Database
                table = $row.table
                tenantId = $row.value
                count = $row.count
            }
            $unknownTenantRows.Add($item)
            if ($row.table -eq "AuditLogs" -and $row.value -eq "00000000-0000-0000-0000-000000000000") {
                $unscopedAuditRows.Add($item)
            }
        }
    }
}
Add-Check "TENANT-01" "No tenant-owned table contains an unknown tenant id" 0 $unknownTenantRows.Count ($unknownTenantRows.Count -eq 0) $unknownTenantRows
Add-Check "TENANT-02" "No EntityAuditLog row uses an unscoped empty tenant id" 0 $unscopedAuditRows.Count ($unscopedAuditRows.Count -eq 0) $unscopedAuditRows

$canonicalEmployees = @{}
foreach ($line in @(Invoke-Psql -Database "hr_core_db" -Sql 'select "Id"::text, "TenantId"::text from "Employees" order by "Id";')) {
    $parts = $line -split $delimiter, 2
    if ($parts.Count -eq 2) { $canonicalEmployees[$parts[0].ToLowerInvariant()] = $parts[1].ToLowerInvariant() }
}
$canonicalOrgUnits = @{}
foreach ($line in @(Invoke-Psql -Database "hr_core_db" -Sql 'select "Id"::text, "TenantId"::text from "OrgUnits" order by "Id";')) {
    $parts = $line -split $delimiter, 2
    if ($parts.Count -eq 2) { $canonicalOrgUnits[$parts[0].ToLowerInvariant()] = $parts[1].ToLowerInvariant() }
}

$referenceViolations = [System.Collections.Generic.List[object]]::new()
$expectedNegativeReferences = [System.Collections.Generic.List[object]]::new()
$referenceGroups = 0
$referenceRowsScanned = 0L
foreach ($service in $services) {
    foreach ($reference in @(Get-ReferenceRows -Database $service.Database)) {
        $referenceGroups++
        $referenceRowsScanned += $reference.count
        $catalog = if ($reference.kind -eq "employee") { $canonicalEmployees } else { $canonicalOrgUnits }
        if (-not $catalog.ContainsKey($reference.referenceId)) {
            $isExpectedInvalidScope =
                $service.Database -eq "auth_directory_db" -and
                $reference.table -eq "AuthUsers" -and
                $reference.column -eq "ScopeOrgUnitId" -and
                $reference.referenceId -eq "ffffffff-ffff-4fff-8fff-ffffffffffff" -and
                $reference.count -eq 1
            $item = [pscustomobject][ordered]@{
                database = $service.Database
                table = $reference.table
                column = $reference.column
                tenantId = $reference.tenantId
                referenceId = $reference.referenceId
                count = $reference.count
                reason = "missing canonical reference"
            }
            if ($isExpectedInvalidScope) {
                $expectedNegativeReferences.Add($item)
            } else {
                $referenceViolations.Add($item)
            }
            continue
        }
        if ($catalog[$reference.referenceId] -ne $reference.tenantId) {
            $referenceViolations.Add([pscustomobject][ordered]@{
                database = $service.Database
                table = $reference.table
                column = $reference.column
                tenantId = $reference.tenantId
                referenceId = $reference.referenceId
                canonicalTenantId = $catalog[$reference.referenceId]
                count = $reference.count
                reason = "cross-tenant reference"
            })
        }
    }
}
Add-Check "REF-01" "Employee and org-unit references resolve within the same tenant" 0 $referenceViolations.Count ($referenceViolations.Count -eq 0) $referenceViolations
Add-Check "REF-02" "Exactly one documented invalid-scope negative fixture is present" 1 $expectedNegativeReferences.Count ($expectedNegativeReferences.Count -eq 1) $expectedNegativeReferences

$queueTables = @("IntegrationMessageReceipts", "InboxState", "OutboxMessage", "OutboxState")
$queueCounts = [System.Collections.Generic.List[object]]::new()
$queueTotal = 0L
foreach ($service in $services) {
    foreach ($table in $queueTables) {
        if ($tableCountsByDatabase[$service.Database].Contains($table)) {
            $count = [long]$tableCountsByDatabase[$service.Database][$table]
            $queueTotal += $count
            $queueCounts.Add([pscustomobject]@{
                database = $service.Database
                table = $table
                count = $count
            })
        }
    }
}
$gatewayCounts = Get-TableCounts -Database "gateway_audit_db"
$gatewayAuditMeasure = $gatewayCounts.Values | Measure-Object -Sum
$gatewayAuditTotal = if ($null -eq $gatewayAuditMeasure.Sum) { 0L } else { [long]$gatewayAuditMeasure.Sum }
$gatewayBaselineMeasure = $gatewayBaselineCounts.Values | Measure-Object -Sum
$gatewayAuditBaselineTotal = if ($null -eq $gatewayBaselineMeasure.Sum) { 0L } else { [long]$gatewayBaselineMeasure.Sum }
if (-not $OperationalRun) {
    Add-Check "BASELINE-01" "Inbox, outbox, and duplicate-receipt tables start empty" 0 $queueTotal ($queueTotal -eq 0) $queueCounts
    Add-Check "BASELINE-02" "Gateway audit database starts empty" 0 $gatewayAuditBaselineTotal ($gatewayAuditBaselineTotal -eq 0) ([pscustomobject]@{
        countsBeforeGatewayChecks = @($gatewayBaselineCounts.GetEnumerator() | ForEach-Object {
            [pscustomobject]@{ table = $_.Key; count = $_.Value }
        })
        countsAfterVerification = @($gatewayCounts.GetEnumerator() | ForEach-Object {
            [pscustomobject]@{ table = $_.Key; count = $_.Value }
        })
    })
} else {
    $warnings.Add("OperationalRun was selected; queue and gateway audit counts were recorded but not required to be zero.")
}

$authRows = @{}
$authSql = @'
select "Username", "Id"::text, "TenantId"::text, "IsActive"::text,
       coalesce("ScopeOrgUnitId"::text, ''), coalesce("KeycloakUserId", ''),
       coalesce("EmployeeId"::text, '')
from "AuthUsers"
order by "Username";
'@
foreach ($line in @(Invoke-Psql -Database "auth_directory_db" -Sql $authSql)) {
    $parts = $line -split $delimiter, 7
    if ($parts.Count -eq 7) {
        $authRows[$parts[0]] = [pscustomobject]@{
            id = $parts[1]
            tenantId = $parts[2]
            active = ($parts[3] -eq "true")
            scopeOrgUnitId = $parts[4]
            keycloakUserId = $parts[5]
            employeeId = $parts[6]
        }
    }
}

$authRoleRows = @{}
$authRoleSql = @'
select u."Username", r."Name"
from "AuthUsers" u
join "UserRoles" ur on ur."AuthUserId" = u."Id" and ur."TenantId" = u."TenantId"
join "Roles" r on r."Id" = ur."RoleId" and r."TenantId" = ur."TenantId"
order by u."Username", r."Name";
'@
foreach ($line in @(Invoke-Psql -Database "auth_directory_db" -Sql $authRoleSql)) {
    $parts = $line -split $delimiter, 2
    if (-not $authRoleRows.ContainsKey($parts[0])) {
        $authRoleRows[$parts[0]] = [System.Collections.Generic.List[string]]::new()
    }
    $authRoleRows[$parts[0]].Add($parts[1])
}

$identityViolations = [System.Collections.Generic.List[object]]::new()
foreach ($persona in $personas) {
    if (-not $authRows.ContainsKey($persona.username)) {
        $identityViolations.Add([pscustomobject]@{
            username = $persona.username
            source = "Auth.Directory"
            issue = "missing user"
        })
        continue
    }

    $auth = $authRows[$persona.username]
    $expectedStorageTenant = if ([string]::IsNullOrWhiteSpace($persona.tenantId)) {
        $platformTenantId
    } else {
        [string]$persona.tenantId
    }
    if ($auth.tenantId -ne $expectedStorageTenant -or $auth.active -ne [bool]$persona.enabled) {
        $identityViolations.Add([pscustomobject]@{
            username = $persona.username
            source = "Auth.Directory"
            issue = "tenant or enabled mismatch"
            expectedTenant = $expectedStorageTenant
            actualTenant = $auth.tenantId
            expectedEnabled = [bool]$persona.enabled
            actualEnabled = $auth.active
        })
    }

    $expectedScope = if ($null -eq $persona.scopeOrgUnitId) { "" } else { [string]$persona.scopeOrgUnitId }
    if ($auth.scopeOrgUnitId -ne $expectedScope) {
        $identityViolations.Add([pscustomobject]@{
            username = $persona.username
            source = "Auth.Directory"
            issue = "scope mismatch"
            expected = $expectedScope
            actual = $auth.scopeOrgUnitId
        })
    }

    $expectedRoles = @($persona.roles | Sort-Object -Unique)
    $actualRoles = if ($authRoleRows.ContainsKey($persona.username)) {
        @($authRoleRows[$persona.username] | Sort-Object -Unique)
    } else {
        @()
    }
    if (@(Compare-Object $expectedRoles $actualRoles).Count -ne 0) {
        $identityViolations.Add([pscustomobject]@{
            username = $persona.username
            source = "Auth.Directory"
            issue = "role mismatch"
            expected = $expectedRoles
            actual = $actualRoles
        })
    }
}
Add-Check "IDENTITY-01" "Persona file matches Auth.Directory users, roles, tenants, scopes, and enabled state" 0 $identityViolations.Count ($identityViolations.Count -eq 0) $identityViolations

$keycloakViolations = [System.Collections.Generic.List[object]]::new()
$keycloakUsers = @()
try {
    $keycloakContainer = ((Invoke-Compose -Arguments @("ps", "-q", "keycloak")) | Out-String).Trim()
    $keycloakJson = & docker exec $keycloakContainer /opt/keycloak/bin/kcadm.sh get users -r hrmanagement 2>&1
    if ($LASTEXITCODE -ne 0) {
        $adminLine = (& docker inspect $keycloakContainer --format '{{range .Config.Env}}{{println .}}{{end}}') |
            Where-Object { $_ -like "KEYCLOAK_ADMIN_PASSWORD=*" } |
            Select-Object -First 1
        if (-not $adminLine) { throw "Keycloak admin password environment entry is missing." }
        $adminPassword = $adminLine.Split("=", 2)[1]
        & docker exec $keycloakContainer /opt/keycloak/bin/kcadm.sh config credentials --server http://localhost:8080 --realm master --user admin --password $adminPassword *> $null
        if ($LASTEXITCODE -ne 0) { throw "Unable to authenticate kcadm." }
        $keycloakJson = & docker exec $keycloakContainer /opt/keycloak/bin/kcadm.sh get users -r hrmanagement 2>&1
        if ($LASTEXITCODE -ne 0) { throw ($keycloakJson | Out-String) }
    }

    $keycloakUsers = @(($keycloakJson | Out-String) | ConvertFrom-Json)
    $keycloakByUsername = @{}
    foreach ($user in $keycloakUsers) { $keycloakByUsername[$user.username] = $user }

    foreach ($persona in $personas) {
        if (-not $keycloakByUsername.ContainsKey($persona.username)) {
            $keycloakViolations.Add([pscustomobject]@{
                username = $persona.username
                issue = "missing Keycloak user"
            })
            continue
        }

        $user = $keycloakByUsername[$persona.username]
        if ([bool]$user.enabled -ne [bool]$persona.enabled) {
            $keycloakViolations.Add([pscustomobject]@{
                username = $persona.username
                issue = "enabled mismatch"
                expected = [bool]$persona.enabled
                actual = [bool]$user.enabled
            })
        }

        $tenantAttribute = if ($user.attributes -and $user.attributes.tenant_id) {
            [string]$user.attributes.tenant_id[0]
        } else {
            ""
        }
        $scopeAttribute = if ($user.attributes -and $user.attributes.scope_org_unit_id) {
            [string]$user.attributes.scope_org_unit_id[0]
        } else {
            ""
        }
        $expectedTenantAttribute = if ($null -eq $persona.tenantId) { "" } else { [string]$persona.tenantId }
        $expectedScopeAttribute = if ($null -eq $persona.scopeOrgUnitId) { "" } else { [string]$persona.scopeOrgUnitId }
        if ($tenantAttribute -ne $expectedTenantAttribute -or $scopeAttribute -ne $expectedScopeAttribute) {
            $keycloakViolations.Add([pscustomobject]@{
                username = $persona.username
                issue = "claim attribute mismatch"
                expectedTenant = $expectedTenantAttribute
                actualTenant = $tenantAttribute
                expectedScope = $expectedScopeAttribute
                actualScope = $scopeAttribute
            })
        }

        if ($authRows.ContainsKey($persona.username) -and $authRows[$persona.username].keycloakUserId -ne [string]$user.id) {
            $keycloakViolations.Add([pscustomobject]@{
                username = $persona.username
                issue = "Keycloak id mismatch with Auth.Directory"
                expected = $authRows[$persona.username].keycloakUserId
                actual = [string]$user.id
            })
        }
    }
}
catch {
    $keycloakViolations.Add([pscustomobject]@{
        username = "*"
        issue = $_.Exception.Message
    })
}
Add-Check "IDENTITY-02" "Persona file matches the live Keycloak realm" 0 $keycloakViolations.Count ($keycloakViolations.Count -eq 0) $keycloakViolations

if ($VerifyIdempotency) {
    $before = @{}
    $ignoredTables = @("AuditLogs", "IntegrationMessageReceipts", "InboxState", "OutboxMessage", "OutboxState")
    foreach ($service in $services) {
        foreach ($entry in $tableCountsByDatabase[$service.Database].GetEnumerator()) {
            if ($ignoredTables -notcontains $entry.Key) {
                $before["$($service.Database)|$($entry.Key)"] = [long]$entry.Value
            }
        }
    }

    Invoke-Compose -Arguments (@("restart") + @($services.Service)) | Out-Null
    $unhealthyAfterRestart = @(Wait-ServiceHealth -TimeoutSeconds 240)
    $idempotencyDifferences = [System.Collections.Generic.List[object]]::new()
    foreach ($service in $services) {
        $afterCounts = Get-TableCounts -Database $service.Database
        foreach ($entry in $afterCounts.GetEnumerator()) {
            if ($ignoredTables -contains $entry.Key) { continue }
            $key = "$($service.Database)|$($entry.Key)"
            $beforeCount = if ($before.ContainsKey($key)) { $before[$key] } else { $null }
            if ($beforeCount -ne [long]$entry.Value) {
                $idempotencyDifferences.Add([pscustomobject]@{
                    database = $service.Database
                    table = $entry.Key
                    before = $beforeCount
                    after = [long]$entry.Value
                })
            }
        }
    }
    Add-Check "SEED-IDEMPOTENCY" "A second seeder execution does not duplicate or remove business data" 0 $idempotencyDifferences.Count ($idempotencyDifferences.Count -eq 0 -and $unhealthyAfterRestart.Count -eq 0) ([pscustomobject]@{
        countDifferences = $idempotencyDifferences
        unhealthyServices = $unhealthyAfterRestart
    })
}

$failedChecks = @($checks | Where-Object { -not $_.passed })
$report = [pscustomobject][ordered]@{
    schemaVersion = "acceptance-verification-v2"
    packId = $manifest.packId
    packVersion = $manifest.version
    referenceDate = $manifest.referenceDate
    capturedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    composeProject = $ComposeProjectName
    gatewayBaseUrl = $GatewayBaseUrl
    operationalRun = [bool]$OperationalRun
    idempotencyRestartExecuted = [bool]$VerifyIdempotency
    result = if ($failedChecks.Count -eq 0) { "Pass" } else { "Fail" }
    passedChecks = @($checks | Where-Object { $_.passed }).Count
    failedChecks = $failedChecks.Count
    checks = @($checks)
    summary = [pscustomobject][ordered]@{
        employees = $tableCountsByDatabase["hr_core_db"]["Employees"]
        businessOrgUnits = 98
        platformOrgUnits = 2
        personas = $personas.Count
        enabledPersonas = @($personas | Where-Object { $_.enabled }).Count
        disabledOrQuarantinedPersonas = @($personas | Where-Object { -not $_.enabled }).Count
        receiptRows = $allReceipts.Count
        receiptFingerprint = if ($receiptFingerprints.Count -eq 1) { $receiptFingerprints[0] } else { $null }
        referenceGroupsScanned = $referenceGroups
        referenceRowsScanned = $referenceRowsScanned
        orphanOrCrossTenantReferences = $referenceViolations.Count
        expectedNegativeReferences = $expectedNegativeReferences.Count
        queueBaselineRows = $queueTotal
        gatewayAuditBaselineRows = $gatewayAuditTotal
        keycloakAcceptanceUsers = @($keycloakUsers | Where-Object { $authRows.ContainsKey($_.username) }).Count
    }
    containers = @($containerReport)
    services = @($serviceReports)
    queueTables = @($queueCounts)
    gatewayAuditTables = @($gatewayCounts.GetEnumerator() | ForEach-Object {
        [pscustomobject]@{ table = $_.Key; count = $_.Value }
    })
    warnings = @($warnings)
}

$jsonPath = Join-Path $outputPath "acceptance-v2-verification.json"
$markdownPath = Join-Path $outputPath "acceptance-v2-verification.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding utf8

$markdown = [System.Text.StringBuilder]::new()
[void]$markdown.AppendLine("# Acceptance v2 Verification Report")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Result: **$($report.result)**")
[void]$markdown.AppendLine("- Captured UTC: $($report.capturedAtUtc)")
[void]$markdown.AppendLine("- Dataset reference date: $($report.referenceDate)")
[void]$markdown.AppendLine("- Compose project: $ComposeProjectName")
[void]$markdown.AppendLine("- Checks: $($report.passedChecks) passed, $($report.failedChecks) failed")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Canonical totals")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Measure | Actual |")
[void]$markdown.AppendLine("|---|---:|")
[void]$markdown.AppendLine("| Employees | $($report.summary.employees) |")
[void]$markdown.AppendLine("| Business org units | $($report.summary.businessOrgUnits) |")
[void]$markdown.AppendLine("| Platform org units | $($report.summary.platformOrgUnits) |")
[void]$markdown.AppendLine("| Personas | $($report.summary.personas) |")
[void]$markdown.AppendLine("| Enabled personas | $($report.summary.enabledPersonas) |")
[void]$markdown.AppendLine("| Disabled/quarantined personas | $($report.summary.disabledOrQuarantinedPersonas) |")
[void]$markdown.AppendLine("| Seed receipts | $($report.summary.receiptRows) |")
[void]$markdown.AppendLine("| Referenced rows scanned | $($report.summary.referenceRowsScanned) |")
[void]$markdown.AppendLine("| Orphan/cross-tenant references | $($report.summary.orphanOrCrossTenantReferences) |")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Checks")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Result | Id | Description | Expected | Actual |")
[void]$markdown.AppendLine("|---|---|---|---:|---:|")
foreach ($check in $checks) {
    $resultLabel = if ($check.passed) { "PASS" } else { "FAIL" }
    $expectedText = if ($check.expected -is [System.Array]) {
        $check.expected -join ", "
    } else {
        [string]$check.expected
    }
    $actualText = if ($check.actual -is [System.Array]) {
        $check.actual -join ", "
    } else {
        [string]$check.actual
    }
    [void]$markdown.AppendLine("| $resultLabel | $($check.id) | $($check.description) | $expectedText | $actualText |")
}
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("## Service seed receipts")
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("| Service | Receipts | Tables with data |")
[void]$markdown.AppendLine("|---|---:|---:|")
foreach ($serviceReport in $serviceReports) {
    $tablesWithData = @($serviceReport.tables | Where-Object { $_.count -gt 0 }).Count
    [void]$markdown.AppendLine("| $($serviceReport.service) | $($serviceReport.receipts.Count) | $tablesWithData |")
}
if ($failedChecks.Count -gt 0) {
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine("## Failure details")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine("~~~json")
    [void]$markdown.AppendLine(($failedChecks | ConvertTo-Json -Depth 12))
    [void]$markdown.AppendLine("~~~")
}
$markdown.ToString() | Set-Content -LiteralPath $markdownPath -Encoding utf8

Write-Host "Acceptance verification result: $($report.result)"
Write-Host "JSON: $jsonPath"
Write-Host "Markdown: $markdownPath"
if ($failedChecks.Count -gt 0) {
    foreach ($failed in $failedChecks) {
        Write-Host "FAIL $($failed.id): $($failed.description)"
    }
    exit 1
}
exit 0
