param(
    [string]$BackupDir = ".\backups",
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5432,
    [string]$PostgresUser = "postgres"
)

$databases = @(
    "hr_core","hr_contracts","hr_movements","hr_training","hr_health",
    "payroll","idcard","records_archive","admin_logistics","it_infra",
    "dev_analytics","compliance","auth_directory"
)

New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"

foreach ($db in $databases) {
    $out = Join-Path $BackupDir "$db-$stamp.sql"
    Write-Host "Backing up $db -> $out"
    & pg_dump -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $db -f $out
}

Write-Host "Backup complete in $BackupDir"
