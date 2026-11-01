-- PLAN-38 (§1.1): the <svc>_app and <svc>_migrator roles of one database, idempotent. Every run sets the passwords
-- again, so rotating a password in .env and deploying is enough. Run as the PostgreSQL administrator, connected to
-- the maintenance database:
--   HRM_APP_PASSWORD=... HRM_MIGRATOR_PASSWORD=... psql -v role_base=hr_core -v db=hr_core_db -f ensure-roles.sql
-- The passwords come from the environment (\getenv), never from the command line, and are not echoed.
\set ON_ERROR_STOP on
\set QUIET on
\getenv app_pwd HRM_APP_PASSWORD
\getenv mig_pwd HRM_MIGRATOR_PASSWORD
\set app_role :role_base '_app'
\set mig_role :role_base '_migrator'

SELECT format('CREATE ROLE %I LOGIN', r)
FROM unnest(ARRAY[:'app_role', :'mig_role']) AS r
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = r)
\gexec

-- TEN-002C: no application role may bypass row level security, and neither role administers anything.
ALTER ROLE :"app_role" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'app_pwd';
ALTER ROLE :"mig_role" WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'mig_pwd';

GRANT CONNECT ON DATABASE :"db" TO :"app_role", :"mig_role";

-- No cross-database connection: revoke any explicit grant on the other databases (PUBLIC is revoked by
-- ensure-databases.sql).
SELECT format('REVOKE CONNECT ON DATABASE %I FROM %I, %I', datname, :'app_role', :'mig_role')
FROM pg_database
WHERE datname <> :'db' AND NOT datistemplate
\gexec

\connect :"db"

GRANT USAGE, CREATE ON SCHEMA public TO :"mig_role";
GRANT USAGE ON SCHEMA public TO :"app_role";
-- The application role never runs DDL (AC-OPS-38c): only the migrator creates and owns objects.
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
REVOKE CREATE ON SCHEMA public FROM :"app_role";

ALTER DEFAULT PRIVILEGES FOR ROLE :"mig_role" IN SCHEMA public
    GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO :"app_role";
ALTER DEFAULT PRIVILEGES FOR ROLE :"mig_role" IN SCHEMA public
    GRANT USAGE, SELECT ON SEQUENCES TO :"app_role";
GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO :"app_role";
GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO :"app_role";

-- PostgreSQL exempts a table's owner from row level security, so the application role must own nothing. On a
-- database the application role migrated itself (old development volumes) this moves the objects to the migrator.
-- Tables the administrator owns (demo mode before PLAN-38) are deliberately left alone (plan, Risks).
REASSIGN OWNED BY :"app_role" TO :"mig_role";
