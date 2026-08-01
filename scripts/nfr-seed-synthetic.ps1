# NFR-001: generate deterministic synthetic employee/org SQL + manifest for load tests.
# Does not require live DB; optional -Apply runs psql when NFR_CORE_DB_URL is set.
# Usage:
#   pwsh scripts/nfr-seed-synthetic.ps1 -Employees 10000
#   pwsh scripts/nfr-seed-synthetic.ps1 -Employees 1000 -Apply
param(
    [int]$Employees = 10000,
    [int]$OrgUnits = 50,
    [int]$Seed = 42,
    [string]$OutDir = "data/nfr-seed",
    [string]$TenantId = "11111111-1111-1111-1111-111111111111",
    [switch]$Apply
)

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$manifest = [ordered]@{
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    seed           = $Seed
    employees      = $Employees
    orgUnits       = $OrgUnits
    tenantId       = $TenantId
    scale_factor   = [math]::Round($Employees / 5000000.0, 6)
    nationalRef    = 5000000
}

$rnd = [Random]::new($Seed)
$orgIds = @()
for ($i = 0; $i -lt $OrgUnits; $i++) {
    $orgIds += [guid]::NewGuid().ToString()
}

$sql = New-Object System.Text.StringBuilder
[void]$sql.AppendLine("-- NFR-001 synthetic seed (seed=$Seed employees=$Employees)")
[void]$sql.AppendLine("-- Target DB: hr_core (adjust schema if needed)")
[void]$sql.AppendLine("BEGIN;")

# Root-ish org units (minimal columns — extend if your schema requires more NOT NULL fields)
for ($i = 0; $i -lt $OrgUnits; $i++) {
    $id = $orgIds[$i]
    $code = "NFR-OU-{0:D4}" -f $i
    $name = "NFR Org Unit $i"
    [void]$sql.AppendLine(@"
INSERT INTO ""OrgUnits"" (""Id"", ""Code"", ""NameAr"", ""NameEn"", ""IsActive"", ""CreatedAt"", ""TenantId"")
VALUES ('$id', '$code', '$name', '$name', TRUE, NOW() AT TIME ZONE 'utc', '$TenantId')
ON CONFLICT DO NOTHING;
"@)
}

$empSqlPath = Join-Path $OutDir "employees.sql"
$batch = New-Object System.Text.StringBuilder
[void]$batch.AppendLine("-- employees batch")
[void]$batch.AppendLine("BEGIN;")

for ($e = 1; $e -le $Employees; $e++) {
    $id = [guid]::NewGuid().ToString()
    $nid = "9{0:D10}" -f (($Seed * 100000 + $e) % 10000000000)
    $fn = "NFR$e"
    $ln = "Load"
    $full = "$fn $ln"
    $ou = $orgIds[$e % $OrgUnits]
    $gender = if (($e % 2) -eq 0) { 1 } else { 0 }
    $birth = "1990-01-01"
    [void]$batch.AppendLine(@"
INSERT INTO ""Employees"" (""Id"", ""NationalId"", ""FirstName"", ""LastName"", ""FullName"", ""BirthDate"", ""Gender"", ""PrimaryOrgUnitId"", ""IsActive"", ""CreatedAt"", ""TenantId"")
VALUES ('$id', '$nid', '$fn', '$ln', '$full', '$birth', $gender, '$ou', TRUE, NOW() AT TIME ZONE 'utc', '$TenantId')
ON CONFLICT DO NOTHING;
"@)
    if (($e % 500) -eq 0) {
        Write-Host "generated $e / $Employees ..."
    }
}

[void]$batch.AppendLine("COMMIT;")
[System.IO.File]::WriteAllText((Join-Path $OutDir "org-units.sql"), $sql.ToString() + "`nCOMMIT;`n", [Text.UTF8Encoding]::new($false))
[System.IO.File]::WriteAllText($empSqlPath, $batch.ToString(), [Text.UTF8Encoding]::new($false))

$manifestPath = Join-Path $OutDir "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

# Fixture for k6 (sample national IDs / counts only — not full dump)
$fixture = @{
    tenantId     = $TenantId
    employeeCount = $Employees
    orgUnitCount  = $OrgUnits
    sampleNationalIds = @(1..([Math]::Min(20, $Employees)) | ForEach-Object { "9{0:D10}" -f (($Seed * 100000 + $_) % 10000000000) })
    lookupPath   = "/api/hr-core/api/lookup/employees"
    orgTreePath  = "/api/hr-core/api/lookup/org-units/tree"
}
$fixture | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $OutDir "k6-fixture.json") -Encoding UTF8

Write-Host "Wrote $OutDir (manifest, org-units.sql, employees.sql, k6-fixture.json)"

if ($Apply) {
    $url = $env:NFR_CORE_DB_URL
    if (-not $url) { throw "Set NFR_CORE_DB_URL to apply (postgres connection string for psql)" }
    if (-not (Get-Command psql -ErrorAction SilentlyContinue)) { throw "psql not found" }
    Write-Host "Applying org-units.sql ..."
    & psql $url -f (Join-Path $OutDir "org-units.sql")
    Write-Host "Applying employees.sql (may take a while) ..."
    & psql $url -f $empSqlPath
    Write-Host "Apply complete."
}
else {
    Write-Host "Dry SQL only. Re-run with -Apply and NFR_CORE_DB_URL to load into DB."
    Write-Host "NOTE: Adjust column lists if your OrgUnits/Employees schema differs (TenantId, path columns, etc.)."
}
