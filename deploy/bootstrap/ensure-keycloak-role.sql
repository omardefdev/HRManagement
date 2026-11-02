-- PLAN-39 (section 3): the keycloak role, idempotent; step 3 of bootstrap.sh, after the <svc>_app/_migrator roles.
-- Keycloak connects to keycloak_db with this role (KC_DB_USERNAME: keycloak), not as the PostgreSQL administrator.
-- Run as the administrator, connected to the maintenance database:
--   HRM_KEYCLOAK_PASSWORD=... psql -v transfer=false -f ensure-keycloak-role.sql
-- The password comes from the environment (\getenv), never from the command line, and is not echoed.
--
-- A new server: keycloak_db is empty, its owner becomes keycloak, and Keycloak creates its tables as keycloak.
-- An existing server (keycloak_db objects owned by another role, the administrator before PLAN-39): this script stops
-- with an error unless the documented ownership transfer is requested (-v transfer=true, bootstrap switch
-- HRM_KEYCLOAK_TRANSFER_OWNERSHIP=true, after a verified backup; docs/OPS_VPS_SINGLE_HOST.md section 13), because
-- Keycloak could not read tables it does not own.
\set ON_ERROR_STOP on
\set QUIET on
\getenv kc_pwd HRM_KEYCLOAK_PASSWORD

SELECT 'CREATE ROLE keycloak LOGIN'
WHERE NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'keycloak')
\gexec

-- Like the application roles: no administration, no RLS bypass. The password is set again on every run (rotation).
ALTER ROLE keycloak WITH LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION NOBYPASSRLS PASSWORD :'kc_pwd';

GRANT CONNECT ON DATABASE keycloak_db TO keycloak;

-- No cross-database connection (PUBLIC has no CONNECT since ensure-databases.sql): revoke any explicit grant.
SELECT format('REVOKE CONNECT ON DATABASE %I FROM keycloak', datname)
FROM pg_database
WHERE datname <> 'keycloak_db' AND NOT datistemplate
\gexec

\connect keycloak_db

\if :transfer
\ir transfer-keycloak-ownership.sql
\endif

-- Objects of keycloak_db (relations, routines, standalone types, schemas but the system ones) that keycloak does not own.
SELECT count(*) AS foreign_owned FROM (
    SELECT c.relowner AS owner FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
      AND c.relkind IN ('r', 'p', 'S', 'v', 'm', 'f')
    UNION ALL
    SELECT p.proowner FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema')
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
    UNION ALL
    SELECT t.typowner FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND t.typtype IN ('c', 'd', 'e', 'r', 'm')
      AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind <> 'c')
      AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = t.oid AND d.deptype = 'e')
    UNION ALL
    SELECT nspowner FROM pg_namespace
    WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema'
      AND nspowner <> 'pg_database_owner'::regrole
) o
WHERE o.owner <> 'keycloak'::regrole
\gset

SELECT :foreign_owned > 0 AS has_foreign_owned \gset
\if :has_foreign_owned
\echo '[keycloak-role] keycloak_db holds objects owned by another role: run the ownership transfer (HRM_KEYCLOAK_TRANSFER_OWNERSHIP=true, after a verified backup)'
SELECT 1 / 0 AS keycloak_db_has_objects_of_another_owner;
\endif

-- Nothing foreign in it: the database itself (and through pg_database_owner its public schema) belongs to keycloak.
SELECT format('ALTER DATABASE keycloak_db OWNER TO keycloak')
WHERE (SELECT datdba FROM pg_database WHERE datname = 'keycloak_db') <> 'keycloak'::regrole
\gexec
