-- PLAN-39 (section 3): moves the ownership of every object of keycloak_db to the keycloak role, for a server whose
-- Keycloak connected as the PostgreSQL administrator before PLAN-39. Included by ensure-keycloak-role.sql (connected
-- to keycloak_db) only when the bootstrap switch HRM_KEYCLOAK_TRANSFER_OWNERSHIP=true is set; off by default.
--
-- Inside keycloak_db only, object by object (REASSIGN OWNED BY the bootstrap superuser is refused by PostgreSQL, and
-- would reach other databases' shared objects): tables, sequences, views, materialized views, foreign tables,
-- routines, standalone types and every non-system schema; indexes and row types follow their tables. Then the
-- database itself. The administrator role and every other database are never touched. Idempotent: a second run finds
-- nothing to move. It refuses to run while any other session is connected to keycloak_db (stop Keycloak first, and
-- take the backup before: docs/OPS_VPS_SINGLE_HOST.md section 13).
\set ON_ERROR_STOP on

SELECT current_database() = 'keycloak_db' AS in_keycloak_db \gset
\if :in_keycloak_db
\else
SELECT 1 / 0 AS transfer_must_run_inside_keycloak_db;
\endif

SELECT count(*) > 0 AS keycloak_db_in_use FROM pg_stat_activity
WHERE datname = 'keycloak_db' AND pid <> pg_backend_pid() \gset
\if :keycloak_db_in_use
\echo '[keycloak-transfer] another session is connected to keycloak_db (Keycloak still running?): stop it first; nothing was changed'
SELECT 1 / 0 AS keycloak_db_is_in_use;
\endif

BEGIN;

-- Tables (and partitioned tables) first: their indexes, row types and the sequences they own follow them.
SELECT format('ALTER TABLE %I.%I OWNER TO keycloak', n.nspname, c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
  AND c.relkind IN ('r', 'p') AND c.relowner <> 'keycloak'::regrole
ORDER BY 1
\gexec

SELECT format('ALTER %s %I.%I OWNER TO keycloak',
              CASE c.relkind WHEN 'S' THEN 'SEQUENCE' WHEN 'v' THEN 'VIEW' WHEN 'm' THEN 'MATERIALIZED VIEW' ELSE 'FOREIGN TABLE' END,
              n.nspname, c.relname)
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
  AND c.relkind IN ('S', 'v', 'm', 'f') AND c.relowner <> 'keycloak'::regrole
ORDER BY 1
\gexec

SELECT format('ALTER ROUTINE %s OWNER TO keycloak', p.oid::regprocedure)
FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND p.proowner <> 'keycloak'::regrole
  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = p.oid AND d.deptype = 'e')
ORDER BY 1
\gexec

SELECT format('ALTER TYPE %s OWNER TO keycloak', t.oid::regtype)
FROM pg_type t JOIN pg_namespace n ON n.oid = t.typnamespace
WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND t.typtype IN ('c', 'd', 'e', 'r', 'm')
  AND NOT EXISTS (SELECT 1 FROM pg_class c WHERE c.oid = t.typrelid AND c.relkind <> 'c')
  AND t.typowner <> 'keycloak'::regrole
  AND NOT EXISTS (SELECT 1 FROM pg_depend d WHERE d.objid = t.oid AND d.deptype = 'e')
ORDER BY 1
\gexec

-- Schemas owned by a specific role (public stays with pg_database_owner, which becomes keycloak with the database).
SELECT format('ALTER SCHEMA %I OWNER TO keycloak', nspname)
FROM pg_namespace
WHERE nspname NOT LIKE 'pg_%' AND nspname <> 'information_schema'
  AND nspowner NOT IN ('keycloak'::regrole, 'pg_database_owner'::regrole)
ORDER BY 1
\gexec

SELECT format('ALTER DATABASE keycloak_db OWNER TO keycloak')
WHERE (SELECT datdba FROM pg_database WHERE datname = 'keycloak_db') <> 'keycloak'::regrole
\gexec

COMMIT;

SELECT '[keycloak-transfer] keycloak_db objects by owner after the transfer: '
       || coalesce(string_agg(owner || '=' || n, ', ' ORDER BY owner), 'none') AS summary
FROM (
    SELECT pg_get_userbyid(c.relowner) AS owner, count(*) AS n
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname NOT IN ('pg_catalog', 'information_schema') AND n.nspname NOT LIKE 'pg_toast%'
    GROUP BY 1
) s
\gset
\echo :summary
