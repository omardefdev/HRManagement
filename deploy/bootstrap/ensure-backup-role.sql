-- PLAN-38 (§1.1): hrm_backup, the logical-backup role (OPS-003), idempotent; the password is set on every run.
-- Run as the PostgreSQL administrator: HRM_BACKUP_PASSWORD=... psql -f ensure-backup-role.sql
-- pg_read_all_data (PostgreSQL 14+) gives SELECT on every table, view and sequence and USAGE on every schema of every
-- database, present and future, so no per-database default privileges are needed (stronger than "SELECT on all tables"
-- plus ALTER DEFAULT PRIVILEGES per migrator, and it covers keycloak_db, whose tables the administrator owns).
-- BYPASSRLS: pg_dump refuses tables with row level security without it. TEN-002C rejects BYPASSRLS on *_app only.
\set ON_ERROR_STOP on
\set QUIET on
\getenv backup_pwd HRM_BACKUP_PASSWORD

SELECT 'CREATE ROLE hrm_backup LOGIN'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'hrm_backup')
\gexec

ALTER ROLE hrm_backup WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION BYPASSRLS PASSWORD :'backup_pwd';
GRANT pg_read_all_data TO hrm_backup;

SELECT format('GRANT CONNECT ON DATABASE %I TO hrm_backup', d)
FROM unnest(ARRAY[
    'keycloak_db', 'gateway_audit_db',
    'hr_core_db', 'hr_contracts_db', 'hr_health_db', 'hr_movements_db', 'hr_training_db',
    'payroll_db', 'idcard_db', 'archive_db', 'admin_db', 'it_infra_db', 'analytics_db',
    'compliance_db', 'auth_directory_db', 'file_storage_db']) AS d
\gexec
