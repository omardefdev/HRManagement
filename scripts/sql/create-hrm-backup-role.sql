-- OPS-003: least-privilege role for logical backups (pg_dump).
-- Run as superuser / CNPG bootstrap admin once per cluster.
-- Password: set via \password or ALTER ROLE ... PASSWORD from Secret backup credentials.
-- Do NOT use application service roles for scheduled dumps.

DO $$
BEGIN
  IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'hrm_backup') THEN
    CREATE ROLE hrm_backup LOGIN;
  END IF;
END
$$;

-- Grant connect on all application databases (adjust list if inventory grows).
-- Example (run once as superuser):
--   GRANT CONNECT ON DATABASE hr_core_db TO hrm_backup;
-- Repeat for each DB in deploy/bootstrap/ensure-databases.sql (hrm-bootstrap does this: deploy/bootstrap/ensure-backup-role.sql).

-- Per-database (connect to each DB then):
GRANT USAGE ON SCHEMA public TO hrm_backup;
GRANT SELECT ON ALL TABLES IN SCHEMA public TO hrm_backup;
ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO hrm_backup;
