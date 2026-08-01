-- DATA-001: detect duplicates before applying unique/partial unique indexes.
-- Run against each service database as needed.

-- SalaryLines
SELECT 'SalaryLines' AS kind, "PayrollRunId"::text, "EmployeeId"::text, COUNT(*)::text AS cnt
FROM "SalaryLines"
GROUP BY "PayrollRunId", "EmployeeId"
HAVING COUNT(*) > 1;

-- Active EmployeePayProfiles
SELECT 'EmployeePayProfiles' AS kind, "EmployeeId"::text, '' AS c2, COUNT(*)::text
FROM "EmployeePayProfiles"
WHERE "IsActive"
GROUP BY "EmployeeId"
HAVING COUNT(*) > 1;

-- Live PayrollRuns (Status <> Reversed=5)
SELECT 'PayrollRuns' AS kind, "TenantId"::text, "PeriodStart"::text || '/' || "PeriodEnd"::text || '/' || "RunType", COUNT(*)::text
FROM "PayrollRuns"
WHERE "Status" <> 5
GROUP BY "TenantId", "PeriodStart", "PeriodEnd", "RunType"
HAVING COUNT(*) > 1;
