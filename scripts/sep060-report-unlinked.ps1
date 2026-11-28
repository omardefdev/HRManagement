# PLAN-60 (P-15): what the SEP060 backfill (rule 7) or the start-up reconciler left unlinked, read only.
# ASCII only; Windows PowerShell 5.1 syntax (run here by pwsh 7).
#
#   pwsh -NoProfile -File scripts/sep060-report-unlinked.ps1 [-Server] [-Detail] [-Container <name>] [-Database hr_contracts_db]
#
# It reports the separation records that have no linked service transaction, and the Separation transactions that have no record:
# counts by default, ids (with the status and the creation time) with -Detail. It never prints a name, and it writes nothing.
#   -Server     runs psql on the demo server through "ssh hrm-vps" (container hrm-postgresql), in one connection.
#   (default)   runs psql in the local acceptance stack's PostgreSQL container (the first running container named *postgres*
#               of a compose project hrm-*), or -Container.
param(
    [switch]$Server,
    [switch]$Detail,
    [string]$Container = "",
    [string]$Database = "hr_contracts_db",
    [string]$SshHost = "hrm-vps"
)

$ErrorActionPreference = "Stop"

$counts = @(
    'SELECT ''records-without-transaction'', count(*) FROM "SeparationRecords" r WHERE NOT EXISTS (SELECT 1 FROM "ServiceTransactions" t WHERE t."SeparationRecordId" = r."Id");',
    'SELECT ''transactions-without-record'', count(*) FROM "ServiceTransactions" t WHERE t."TransactionType" = ''Separation'' AND t."SeparationRecordId" IS NULL;',
    'SELECT ''records'', count(*) FROM "SeparationRecords";',
    'SELECT ''separation-transactions'', count(*) FROM "ServiceTransactions" WHERE "TransactionType" = ''Separation'';'
)
$details = @(
    'SELECT ''record'', r."Id", r."Status", to_char(r."CreatedAt", ''YYYY-MM-DD"T"HH24:MI:SS'') FROM "SeparationRecords" r WHERE NOT EXISTS (SELECT 1 FROM "ServiceTransactions" t WHERE t."SeparationRecordId" = r."Id") ORDER BY r."CreatedAt", r."Id";',
    'SELECT ''transaction'', t."Id", t."State", to_char(t."CreatedAt", ''YYYY-MM-DD"T"HH24:MI:SS'') FROM "ServiceTransactions" t WHERE t."TransactionType" = ''Separation'' AND t."SeparationRecordId" IS NULL ORDER BY t."CreatedAt", t."Id";'
)
$sql = ($counts -join " ")
if ($Detail) { $sql = $sql + " " + ($details -join " ") }

function Invoke-Local([string]$name, [string]$query) {
    $output = & docker exec -e "Q=$query" -e "DB=$Database" $name sh -c 'psql -U "$POSTGRES_USER" -d "$DB" -AtF"|" -c "$Q"' 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql in $name failed: $output" }
    return $output
}

if ($Server) {
    # One connection; the query travels as an environment value of docker exec, never interpolated by a shell.
    $encoded = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($sql))
    $remote = "Q=`$(echo $encoded | base64 -d); docker exec -e Q=""`$Q"" -e DB=$Database hrm-postgresql sh -c 'psql -U ""`$POSTGRES_USER"" -d ""`$DB"" -AtF""|"" -c ""`$Q""'"
    $output = & ssh -o BatchMode=yes $SshHost $remote 2>&1
    if ($LASTEXITCODE -ne 0) { throw "psql on $SshHost failed: $output" }
}
else {
    if ([string]::IsNullOrWhiteSpace($Container)) {
        $Container = @(& docker ps --format "{{.Names}}" | Where-Object { $_ -like "hrm-*" -and $_ -like "*postgres*" -and $_ -ne "hrm-postgresql" }) | Select-Object -First 1
        if ([string]::IsNullOrWhiteSpace($Container)) { throw "No running PostgreSQL container of a local hrm-* stack; pass -Container." }
    }
    $output = Invoke-Local $Container $sql
}

$lines = @($output | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$summary = @{}
foreach ($line in $lines) {
    $parts = $line -split '\|'
    if ($parts.Count -eq 2) { $summary[$parts[0]] = [int]$parts[1] }
}
Write-Host ("sep060-report-unlinked ({0}): {1} record(s), {2} without a transaction; {3} Separation transaction(s), {4} without a record" -f `
        $(if ($Server) { $SshHost } else { $Container }), $summary['records'], $summary['records-without-transaction'], `
        $summary['separation-transactions'], $summary['transactions-without-record'])
if ($Detail) {
    foreach ($line in $lines) {
        $parts = $line -split '\|'
        if ($parts.Count -eq 4) { Write-Host ("  {0} {1} {2} {3}" -f $parts[0], $parts[1], $parts[2], $parts[3]) }
    }
}
exit 0
