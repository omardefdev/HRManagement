-- PLAN-38: the 16 databases, idempotent (also the first-init script of the postgres container, as
-- /docker-entrypoint-initdb.d/01-init-databases.sql). Run as the PostgreSQL administrator.
\set ON_ERROR_STOP on

SELECT format('CREATE DATABASE %I', d)
FROM unnest(ARRAY[
    'keycloak_db', 'gateway_audit_db',
    'hr_core_db', 'hr_contracts_db', 'hr_health_db', 'hr_movements_db', 'hr_training_db',
    'payroll_db', 'idcard_db', 'archive_db', 'admin_db', 'it_infra_db', 'analytics_db',
    'compliance_db', 'auth_directory_db', 'file_storage_db']) AS d
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = d)
\gexec

-- PostgreSQL grants CONNECT to PUBLIC by default; each role gets only its own database (ensure-roles.sql).
SELECT format('REVOKE CONNECT ON DATABASE %I FROM PUBLIC', d)
FROM unnest(ARRAY[
    'keycloak_db', 'gateway_audit_db',
    'hr_core_db', 'hr_contracts_db', 'hr_health_db', 'hr_movements_db', 'hr_training_db',
    'payroll_db', 'idcard_db', 'archive_db', 'admin_db', 'it_infra_db', 'analytics_db',
    'compliance_db', 'auth_directory_db', 'file_storage_db']) AS d
\gexec
