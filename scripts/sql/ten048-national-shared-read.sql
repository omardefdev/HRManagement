-- PLAN-48 (TEN-048): national settings are readable by every agency under forced row security, and writable only by the
-- national tenant.
--
-- For each national shared table (D-TEN-2), a PERMISSIVE policy FOR SELECT that admits the national tenant's rows
-- (PLATFORM, 00000000-0000-4000-8000-000000000001). PostgreSQL ORs it with tenant_isolation for reads only: INSERT, UPDATE
-- and DELETE keep tenant_isolation alone (its USING and WITH CHECK), so an agency session never writes a national row.
--
-- The table list must equal NationalTenant.TableNames (HRManagement.SharedKernel) and the tables of the entities marked
-- INationalShared in the HR.Core EF model; NationalSharedArchitectureTests proves it.
--
-- hr_core_db only. Idempotent. Run after ten002c-harden-tenant-rls.sql as the role that owns the tables (hr_core_migrator).
-- Exits non-zero (via the final DO block) when the policy is missing on a listed table or present on any other table.
\set ON_ERROR_STOP on

DO $$
DECLARE
    national_tables text[] := ARRAY[
        'PolicyTemplates',
        'PolicyTemplateVersions',
        'PolicyRules',
        'PolicyFactsSchemas',
        'PolicyWorkflowBindings',
        'WorkflowDefinitions',
        'CodePolicyTemplates',
        'CodePolicyVersions',
        'CodePolicyRules',
        'ReferenceCatalogEntries'
    ];
    t text;
    applied int := 0;
BEGIN
    FOREACH t IN ARRAY national_tables
    LOOP
        CONTINUE WHEN to_regclass(format('public.%I', t)) IS NULL;
        -- ALTER and CREATE POLICY need ownership; a table this role does not own is left to the verification below.
        CONTINUE WHEN (SELECT pg_catalog.pg_get_userbyid(c.relowner) FROM pg_class c
                       WHERE c.oid = to_regclass(format('public.%I', t))) <> current_user;
        EXECUTE format('DROP POLICY IF EXISTS national_shared_read ON %I', t);
        EXECUTE format(
            'CREATE POLICY national_shared_read ON %I AS PERMISSIVE FOR SELECT USING ("TenantId"::text = %L)',
            t, '00000000-0000-4000-8000-000000000001');
        applied := applied + 1;
    END LOOP;
    RAISE NOTICE 'TEN-048: national_shared_read applied on % table(s) owned by %.', applied, current_user;
END $$;

DO $$
DECLARE
    national_tables text[] := ARRAY[
        'PolicyTemplates',
        'PolicyTemplateVersions',
        'PolicyRules',
        'PolicyFactsSchemas',
        'PolicyWorkflowBindings',
        'WorkflowDefinitions',
        'CodePolicyTemplates',
        'CodePolicyVersions',
        'CodePolicyRules',
        'ReferenceCatalogEntries'
    ];
    offenders text;
BEGIN
    SELECT string_agg(problem, ', ')
    INTO offenders
    FROM (
        SELECT format('%s(missing)', t) AS problem
        FROM unnest(national_tables) AS t
        WHERE to_regclass(format('public.%I', t)) IS NULL
           OR NOT EXISTS (
               SELECT 1 FROM pg_policy p
               WHERE p.polrelid = to_regclass(format('public.%I', t))
                 AND p.polname = 'national_shared_read'
                 AND p.polcmd = 'r'
                 AND p.polpermissive
                 AND p.polwithcheck IS NULL
                 AND pg_get_expr(p.polqual, p.polrelid) LIKE '%00000000-0000-4000-8000-000000000001%')
        UNION ALL
        SELECT format('%s(unexpected)', c.relname)
        FROM pg_policy p
        JOIN pg_class c ON c.oid = p.polrelid
        JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE p.polname = 'national_shared_read'
          AND NOT (n.nspname = 'public' AND c.relname = ANY (national_tables))
    ) problems;

    IF offenders IS NOT NULL THEN
        RAISE EXCEPTION 'TEN-048 FAILED: national_shared_read is wrong on: %', offenders;
    END IF;
    RAISE NOTICE 'TEN-048: national_shared_read verified on the % national tables.', array_length(national_tables, 1);
END $$;
