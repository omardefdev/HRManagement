# PLAN-48 (TEN-048, D-TEN-4): report the numbers issued without their prefix. Report only: nothing is rewritten.
#
# Reads CodeIssuances (HR.Core) and the number columns of every service database (RequisitionNumber, OfferNumber,
# ReferenceNumber, DecisionNumber, SerialNumber, TagNumber, CaseNumber) and lists every value that starts with "-" (the shape
# of the old silent fallback, "-2026-00031"). CodeIssuances rows with no policy version (a fallback number) are listed too.
# Writes <OutDir>/prefixless-codes.csv and prints one summary line per source.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ten048-find-prefixless-codes.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ten048-find-prefixless-codes.ps1 -Since 2026-11-09
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts/ten048-find-prefixless-codes.ps1 -Target Server -FailIfAny
#
# -Target Acceptance reads the local stack's PostgreSQL container (default hrm-acceptance-postgresql-1); -Target Server reads
# the demo server's hrm-postgresql over "ssh <SshHost>" (read-only queries; the superuser name comes from the container).
# -Since limits the report to rows issued or created at or after that time (UTC). -FailIfAny exits 1 when a row is found.
param(
    [ValidateSet('Acceptance', 'Server')]
    [string]$Target = 'Acceptance',
    [string]$Container = '',
    [string]$SshHost = 'hrm-vps',
    [Nullable[datetime]]$Since = $null,
    [string]$OutDir = 'artifacts/ten048',
    [switch]$FailIfAny
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Container)) {
    if ($Target -eq 'Server') { $Container = 'hrm-postgresql' } else { $Container = 'hrm-acceptance-postgresql-1' }
}

$NumberColumns = @('RequisitionNumber', 'OfferNumber', 'ReferenceNumber', 'DecisionNumber', 'SerialNumber', 'TagNumber', 'CaseNumber')

function Invoke-Psql {
    param([string]$Database, [string]$Sql)
    # No double quote anywhere: Windows PowerShell 5.1 mangles embedded quotes in native arguments. sh unescapes "\|".
    $inner = 'psql -X -q -At -F \| -v ON_ERROR_STOP=1 -U $POSTGRES_USER -d ' + $Database
    if ($Target -eq 'Server') {
        $remote = "docker exec -i $Container sh -c '" + $inner + "'"
        $output = $Sql | & ssh -o BatchMode=yes $SshHost $remote
    }
    else {
        $output = $Sql | & docker exec -i $Container sh -c $inner
    }
    if ($LASTEXITCODE -ne 0) { throw "psql failed on $Database (exit $LASTEXITCODE)" }
    return @($output | Where-Object { $_ -ne $null -and $_.Trim() -ne '' })
}

function Quote-Literal([string]$Value) { return "'" + $Value.Replace("'", "''") + "'" }

$sinceSql = ''
if ($Since) {
    $sinceSql = $Since.Value.ToUniversalTime().ToString('yyyy-MM-dd HH:mm:ss', [System.Globalization.CultureInfo]::InvariantCulture)
}

$databases = @(Invoke-Psql -Database 'postgres' -Sql "SELECT datname FROM pg_database WHERE datname LIKE '%\_db' AND datname NOT IN ('keycloak_db') ORDER BY 1;")
$rows = New-Object System.Collections.Generic.List[object]
$summary = New-Object System.Collections.Generic.List[string]

foreach ($db in $databases) {
    $columnList = ($NumberColumns | ForEach-Object { Quote-Literal $_ }) -join ','
    $catalog = @(Invoke-Psql -Database $db -Sql @"
SELECT c.table_name, c.column_name,
       EXISTS (SELECT 1 FROM information_schema.columns t WHERE t.table_schema = 'public' AND t.table_name = c.table_name AND t.column_name = 'TenantId'),
       COALESCE((SELECT t.column_name FROM information_schema.columns t
                 WHERE t.table_schema = 'public' AND t.table_name = c.table_name AND t.column_name IN ('IssuedAt', 'CreatedAt')
                 ORDER BY CASE t.column_name WHEN 'IssuedAt' THEN 0 ELSE 1 END LIMIT 1), '')
FROM information_schema.columns c
WHERE c.table_schema = 'public'
  AND c.data_type IN ('character varying', 'text')
  AND (c.column_name IN ($columnList) OR (c.table_name = 'CodeIssuances' AND c.column_name = 'Code'))
ORDER BY 1, 2;
"@)
    foreach ($line in $catalog) {
        $parts = $line.Split('|')
        $table = $parts[0]; $column = $parts[1]; $hasTenant = $parts[2] -eq 't'; $stamp = $parts[3]
        $tenantExpr = 'NULL'
        if ($hasTenant) { $tenantExpr = '"TenantId"::text' }
        $stampExpr = 'NULL'
        if ($stamp -ne '') { $stampExpr = 'to_char("' + $stamp + '", ''YYYY-MM-DD"T"HH24:MI:SS"Z"'')' }
        $where = '"' + $column + '" LIKE ''-%'''
        $reason = "'starts-with-dash'"
        if ($table -eq 'CodeIssuances') {
            $where = '("Code" LIKE ''-%'' OR "PolicyVersionId" IS NULL)'
            $reason = 'CASE WHEN "Code" LIKE ''-%'' THEN ''starts-with-dash'' ELSE ''no-policy-version'' END'
            if ($hasTenant) { $tenantExpr = '"TenantId"::text || '':'' || "EntityKind"' }
        }
        if ($sinceSql -ne '' -and $stamp -ne '') { $where = $where + ' AND "' + $stamp + '" >= ' + (Quote-Literal $sinceSql) }
        $found = @(Invoke-Psql -Database $db -Sql ('SELECT ' + $tenantExpr + ', "' + $column + '", ' + $stampExpr + ', ' + $reason + ' FROM "' + $table + '" WHERE ' + $where + ' ORDER BY 2;'))
        foreach ($f in $found) {
            $p = $f.Split('|')
            $rows.Add([pscustomobject]@{ Database = $db; Table = $table; Column = $column; Tenant = $p[0]; Code = $p[1]; At = $p[2]; Reason = $p[3] })
        }
        $summary.Add(('{0,-18} {1,-28} {2,-18} {3,5}' -f $db, $table, $column, $found.Count))
    }
}

if (-not (Test-Path $OutDir)) { New-Item -ItemType Directory -Force -Path $OutDir | Out-Null }
$csv = Join-Path $OutDir 'prefixless-codes.csv'
$rows | Select-Object Database, Table, Column, Tenant, Code, At, Reason | Export-Csv -Path $csv -NoTypeInformation -Encoding ASCII

Write-Host ('PLAN-48 prefixless codes, target ' + $Target + $(if ($sinceSql -ne '') { ', since ' + $sinceSql + ' UTC' } else { '' }))
$summary | ForEach-Object { Write-Host $_ }
Write-Host ('PREFIXLESS TOTAL: ' + $rows.Count + ' (report: ' + $csv + ')')
if ($FailIfAny -and $rows.Count -gt 0) { exit 1 }
exit 0
