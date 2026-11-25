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

-- Live regular PayrollRuns, as IX_PayrollRuns_LivePeriod defines them since PLAN-56: RunType = Regular, and neither
-- Reversed (5) nor Cancelled (8). A cancelled run frees its period, and a correction has the period of its source.
SELECT 'PayrollRuns' AS kind, "TenantId"::text, "PeriodStart"::text || '/' || "PeriodEnd"::text || '/' || "RunType", COUNT(*)::text
FROM "PayrollRuns"
WHERE "RunType" = 'Regular' AND "Status" <> 5 AND "Status" <> 8
GROUP BY "TenantId", "PeriodStart", "PeriodEnd", "RunType"
HAVING COUNT(*) > 1;

-- Open corrections per source run, as IX_PayrollRuns_OneOpenCorrectionPerSource defines them (PLAN-56): not Posted (2),
-- Reversed (5) or Cancelled (8).
SELECT 'PayrollRunCorrections' AS kind, "TenantId"::text, "SourceRunId"::text, COUNT(*)::text
FROM "PayrollRuns"
WHERE "RunType" = 'Correction' AND "SourceRunId" IS NOT NULL AND "Status" NOT IN (2, 5, 8)
GROUP BY "TenantId", "SourceRunId"
HAVING COUNT(*) > 1;
