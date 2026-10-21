-- PLAN-13 AC-34: logical relationship checks after restore (single database context).
-- Usage: psql -d $DRILL_DB -v ON_ERROR_STOP=1 -f scripts/verify-restore-relationships.sql

\set ON_ERROR_STOP on

DO $$
BEGIN
    RAISE NOTICE 'relationship-check database=%', current_database();
END $$;

-- hr_core_db expectations
DO $$
BEGIN
    IF to_regclass('public."Employees"') IS NOT NULL THEN
        IF (SELECT count(*) FROM public."Employees") = 0 THEN
            RAISE EXCEPTION 'Employees table empty after restore';
        END IF;
    END IF;
END $$;

-- payroll_db expectations
DO $$
BEGIN
    IF to_regclass('public."PayrollRunLines"') IS NOT NULL THEN
        IF (SELECT count(*) FROM public."PayrollRunLines" WHERE "EmployeeId" IS NULL) > 0 THEN
            RAISE EXCEPTION 'PayrollRunLines has NULL EmployeeId';
        END IF;
    END IF;
END $$;

-- admin_db expectations
DO $$
BEGIN
    IF to_regclass('public."AssetAssignments"') IS NOT NULL THEN
        IF (SELECT count(*) FROM public."AssetAssignments" WHERE "EmployeeId" IS NULL) > 0 THEN
            RAISE EXCEPTION 'AssetAssignments has NULL EmployeeId';
        END IF;
    END IF;
END $$;

-- gateway_audit_db expectations
DO $$
BEGIN
    IF to_regclass('public.gateway_audit_entries') IS NOT NULL THEN
        IF (SELECT count(*) FROM public.gateway_audit_entries WHERE "EventHash" IS NULL OR "EventHash" = '') > 0 THEN
            RAISE EXCEPTION 'gateway_audit_entries missing EventHash';
        END IF;
    END IF;
END $$;

SELECT 'relationship-sql-ok' AS status;
