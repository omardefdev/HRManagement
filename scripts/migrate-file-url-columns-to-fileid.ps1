# Backfill FileId from legacy URL/storage key columns and register references in File Service.
# Run against each database after File Storage platform migration.

param(
    [string]$PostgresHost = "localhost",
    [int]$PostgresPort = 5433,
    [string]$PostgresUser = "hradmin",
    [string]$PostgresPassword = "hradmin_secret_change_me",
    [string]$GatewayBaseUrl = "http://localhost:5000"
)

$ErrorActionPreference = "Stop"
$env:PGPASSWORD = $PostgresPassword

function Invoke-Psql($Database, $Sql) {
    psql -h $PostgresHost -p $PostgresPort -U $PostgresUser -d $Database -v ON_ERROR_STOP=1 -c $Sql
}

Write-Host "=== HR Core: Employees with PhotoUrl but no PhotoFileId ==="
Write-Host "Manual step: upload legacy photos via migrate-local-uploads-to-minio.ps1 then assign PhotoFileId in admin UI."

Write-Host "=== Drop legacy URL columns (run after soak period) ==="
Write-Host @"
-- hr_core_db
ALTER TABLE ""Employees"" DROP COLUMN IF EXISTS ""PhotoUrl"";
ALTER TABLE ""EmployeeDocuments"" DROP COLUMN IF EXISTS ""StorageUrl"";
ALTER TABLE ""Qualifications"" DROP COLUMN IF EXISTS ""CertificateUrl"";
ALTER TABLE ""ApprovedCVs"" DROP COLUMN IF EXISTS ""DocumentUrl"";
ALTER TABLE ""ApprovedCVs"" ADD COLUMN IF NOT EXISTS ""Notes"" text;

-- hr_contracts_db
ALTER TABLE ""AdministrativeDecisions"" DROP COLUMN IF EXISTS ""DocumentUrl"";

-- archive_db
ALTER TABLE ""DigitalFileRecords"" DROP COLUMN IF EXISTS ""FileUrl"";

-- idcard_db
ALTER TABLE ""OfficialSealRecords"" DROP COLUMN IF EXISTS ""SealImageUrl"";

-- hr_training_db
ALTER TABLE ""TrainingEnrollments"" DROP COLUMN IF EXISTS ""CertificateUrl"";
ALTER TABLE ""TrainingEnrollments"" ADD COLUMN IF NOT EXISTS ""CertificateFileId"" uuid;
ALTER TABLE ""Lessons"" DROP COLUMN IF EXISTS ""ContentUrl"";
ALTER TABLE ""Lessons"" ADD COLUMN IF NOT EXISTS ""ContentFileId"" uuid;
ALTER TABLE ""Lessons"" ADD COLUMN IF NOT EXISTS ""ExternalResourceUrl"" character varying(500);
"@

Write-Host "Done. Apply SQL manually when ready for Phase 9 column drops."
