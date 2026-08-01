-- TEN-002C: enforce tenant row level security on every tenant table, then verify.
--
-- Two defects this closes:
--   1. ENABLE ROW LEVEL SECURITY alone does not apply to a table's owner. Because the
--      migrating role owns the tables, policies were silently inert.
--   2. A policy with only USING permits INSERT/UPDATE of rows carrying another tenant's id.
--
-- Idempotent. Must run as the role that owns the tables (the *_migrator role).
-- Exits non-zero (via the final DO block) when any tenant table is left unhardened.

\set ON_ERROR_STOP on

DO $$
DECLARE
    r RECORD;
    excluded text[] := ARRAY[
        'Tenants',
        'TenantAssignmentRules',
        'TenantQuarantineEntries',
        'SodRules',
        'ConflictingRolePairs',
        'AuditLogs',
        'EntityAuditLogs'
    ];
    predicate text := '"TenantId"::text = NULLIF(current_setting(''app.current_tenant'', true), '''''''')';
    hardened int := 0;
BEGIN
    FOR r IN
        SELECT c.relname AS table_name
        FROM pg_class c
        JOIN pg_namespace n ON n.oid = c.relnamespace
        JOIN pg_attribute a ON a.attrelid = c.oid
        WHERE n.nspname = 'public'
          AND c.relkind = 'r'
          AND a.attname = 'TenantId'
          AND a.attnum > 0
          AND NOT a.attisdropped
          AND NOT (c.relname = ANY (excluded))
          AND c.relname NOT LIKE 'InboxState%'
          AND c.relname NOT LIKE 'OutboxState%'
          AND c.relname NOT LIKE 'OutboxMessage%'
          AND pg_catalog.pg_get_userbyid(c.relowner) = current_user
    LOOP
        EXECUTE format('ALTER TABLE %I ENABLE ROW LEVEL SECURITY', r.table_name);
        EXECUTE format('ALTER TABLE %I FORCE ROW LEVEL SECURITY', r.table_name);
        EXECUTE format('DROP POLICY IF EXISTS tenant_isolation ON %I', r.table_name);
        EXECUTE format(
            'CREATE POLICY tenant_isolation ON %I USING (%s) WITH CHECK (%s)',
            r.table_name, predicate, predicate);
        hardened := hardened + 1;
    END LOOP;

    RAISE NOTICE 'TEN-002C: hardened % tenant table(s) owned by %.', hardened, current_user;
END $$;

-- No application role may hold BYPASSRLS; it would defeat every policy above.
DO $$
DECLARE
    offenders text;
BEGIN
    SELECT string_agg(rolname, ', ')
    INTO offenders
    FROM pg_roles
    WHERE rolbypassrls
      AND NOT rolsuper
      AND rolname LIKE '%\_app';

    IF offenders IS NOT NULL THEN
        RAISE EXCEPTION 'TEN-002C FAILED: application roles hold BYPASSRLS: %', offenders;
    END IF;
END $$;

DO $$
DECLARE
    offenders text;
BEGIN
    SELECT string_agg(
               format('%s(enabled=%s,forced=%s,withCheck=%s)',
                      c.relname,
                      c.relrowsecurity,
                      c.relforcerowsecurity,
                      EXISTS (SELECT 1 FROM pg_policy p
                              WHERE p.polrelid = c.oid
                                AND p.polname = 'tenant_isolation'
                                AND p.polwithcheck IS NOT NULL)),
               ', ')
    INTO offenders
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    JOIN pg_attribute a ON a.attrelid = c.oid
    WHERE n.nspname = 'public'
      AND c.relkind = 'r'
      AND a.attname = 'TenantId'
      AND a.attnum > 0
      AND NOT a.attisdropped
      AND c.relname NOT IN (
          'Tenants', 'TenantAssignmentRules', 'TenantQuarantineEntries',
          'SodRules', 'ConflictingRolePairs', 'AuditLogs', 'EntityAuditLogs')
      AND c.relname NOT LIKE 'InboxState%'
      AND c.relname NOT LIKE 'OutboxState%'
      AND c.relname NOT LIKE 'OutboxMessage%'
      AND (
          NOT c.relrowsecurity
          OR NOT c.relforcerowsecurity
          OR NOT EXISTS (SELECT 1 FROM pg_policy p
                         WHERE p.polrelid = c.oid
                           AND p.polname = 'tenant_isolation'
                           AND p.polwithcheck IS NOT NULL)
      );

    IF offenders IS NOT NULL THEN
        RAISE EXCEPTION 'TEN-002C FAILED: tenant isolation not enforced on: %', offenders;
    END IF;

    RAISE NOTICE 'TEN-002C: verification passed.';
END $$;
