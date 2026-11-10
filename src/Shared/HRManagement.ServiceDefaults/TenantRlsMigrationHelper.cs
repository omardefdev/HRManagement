using Microsoft.EntityFrameworkCore.Migrations;

namespace HRManagement.ServiceDefaults;

/// <summary>SQL helpers for TEN-002 backfill and PostgreSQL RLS.</summary>
public static class TenantRlsMigrationHelper
{
    public const string DefaultTenantId = "00000000-0000-4000-8000-000000000001";

    public static void BackfillTenantId(MigrationBuilder migrationBuilder, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(
                $"""
                UPDATE "{table}"
                SET "TenantId" = '{DefaultTenantId}'
                WHERE "TenantId" IS NULL OR "TenantId" = '00000000-0000-0000-0000-000000000000';
                """);
        }
    }

    /// <summary>
    /// Predicate shared by the policy USING and WITH CHECK clauses. A NULL/empty
    /// <c>app.current_tenant</c> matches nothing, so an unset session sees and writes no rows.
    /// </summary>
    private const string TenantPredicate =
        """ "TenantId"::text = NULLIF(current_setting('app.current_tenant', true), '') """;

    public static void EnableRls(MigrationBuilder migrationBuilder, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(
                $"""
                ALTER TABLE "{table}" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS tenant_isolation ON "{table}";
                CREATE POLICY tenant_isolation ON "{table}"
                    USING ({TenantPredicate})
                    WITH CHECK ({TenantPredicate});
                """);
        }
    }

    public static void DisableRls(MigrationBuilder migrationBuilder, params string[] tables)
    {
        foreach (var table in tables)
        {
            migrationBuilder.Sql(
                $"""
                DROP POLICY IF EXISTS tenant_isolation ON "{table}";
                ALTER TABLE "{table}" DISABLE ROW LEVEL SECURITY;
                """);
        }
    }

    /// <summary>
    /// TEN-002B: enable RLS on every public table that has a "TenantId" column,
    /// excluding platform/global tables. Idempotent — safe to re-run.
    /// </summary>
    public static void EnableRlsOnAllTenantIdTables(MigrationBuilder migrationBuilder)
        => migrationBuilder.Sql(HardenAllTenantIdTablesSql);

    /// <summary>
    /// Idempotent SQL applying TEN-002C hardening to every tenant table:
    /// ENABLE + FORCE row level security and a tenant_isolation policy carrying both
    /// USING and WITH CHECK.
    /// <para>
    /// FORCE is required because PostgreSQL exempts a table's owner from RLS policies.
    /// WITH CHECK is required because a USING-only policy still permits INSERT/UPDATE
    /// of rows belonging to another tenant.
    /// </para>
    /// Skips tables the current role does not own, since ALTER TABLE requires ownership;
    /// <see cref="VerifyTenantRlsSql"/> reports anything left unhardened.
    /// </summary>
    public const string HardenAllTenantIdTablesSql =
        """
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
                  -- MassTransit inbox/outbox infrastructure (not tenant-filtered by design)
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
            END LOOP;
        END $$;
        """;

    /// <summary>
    /// Returns one row per tenant table that is not fully hardened, with the reason.
    /// An empty result set is the pass condition for TEN-002C.
    /// </summary>
    public const string VerifyTenantRlsSql =
        """
        SELECT c.relname AS table_name,
               c.relrowsecurity AS rls_enabled,
               c.relforcerowsecurity AS rls_forced,
               EXISTS (
                   SELECT 1 FROM pg_policy p
                   WHERE p.polrelid = c.oid
                     AND p.polname = 'tenant_isolation'
                     AND p.polwithcheck IS NOT NULL
               ) AS has_with_check
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
              OR NOT EXISTS (
                  SELECT 1 FROM pg_policy p
                  WHERE p.polrelid = c.oid
                    AND p.polname = 'tenant_isolation'
                    AND p.polwithcheck IS NOT NULL
              )
          );
        """;

    /// <summary>PLAN-48: the SELECT-only policy that shows the national rows of the national shared tables to every tenant.</summary>
    public const string NationalSharedReadPolicyName = "national_shared_read";

    private static string NationalTablesArray =>
        "ARRAY[" + string.Join(", ", HRManagement.SharedKernel.Tenancy.NationalTenant.TableNames.Select(t => $"'{t}'")) + "]::text[]";

    /// <summary>
    /// PLAN-48 (§1): for each table of <c>NationalTenant.TableNames</c> this role owns, a PERMISSIVE policy FOR SELECT that admits
    /// the national tenant's rows. PostgreSQL ORs it with <c>tenant_isolation</c> for reads only; INSERT, UPDATE and DELETE keep
    /// <c>tenant_isolation</c> alone. The same as <c>scripts/sql/ten048-national-shared-read.sql</c>. Idempotent.
    /// </summary>
    public static string NationalSharedReadSql =>
        $$"""
        DO $$
        DECLARE
            national_tables text[] := {{NationalTablesArray}};
            t text;
        BEGIN
            FOREACH t IN ARRAY national_tables
            LOOP
                CONTINUE WHEN to_regclass(format('public.%I', t)) IS NULL;
                CONTINUE WHEN (SELECT pg_catalog.pg_get_userbyid(c.relowner) FROM pg_class c
                               WHERE c.oid = to_regclass(format('public.%I', t))) <> current_user;
                EXECUTE format('DROP POLICY IF EXISTS {{NationalSharedReadPolicyName}} ON %I', t);
                EXECUTE format(
                    'CREATE POLICY {{NationalSharedReadPolicyName}} ON %I AS PERMISSIVE FOR SELECT USING ("TenantId"::text = %L)',
                    t, '{{DefaultTenantId}}');
            END LOOP;
        END $$;
        """;

    /// <summary>
    /// One row per problem: a listed table without the national read policy (or missing), or the policy on any other table.
    /// An empty result is the pass condition.
    /// </summary>
    public static string VerifyNationalSharedReadSql =>
        $$"""
        SELECT problem FROM (
            SELECT format('%s(missing)', t) AS problem
            FROM unnest({{NationalTablesArray}}) AS t
            WHERE to_regclass(format('public.%I', t)) IS NULL
               OR NOT EXISTS (
                   SELECT 1 FROM pg_policy p
                   WHERE p.polrelid = to_regclass(format('public.%I', t))
                     AND p.polname = '{{NationalSharedReadPolicyName}}'
                     AND p.polcmd = 'r'
                     AND p.polpermissive
                     AND p.polwithcheck IS NULL
                     AND pg_get_expr(p.polqual, p.polrelid) LIKE '%{{DefaultTenantId}}%')
            UNION ALL
            SELECT format('%s(unexpected)', c.relname)
            FROM pg_policy p
            JOIN pg_class c ON c.oid = p.polrelid
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE p.polname = '{{NationalSharedReadPolicyName}}'
              AND NOT (n.nspname = 'public' AND c.relname = ANY ({{NationalTablesArray}}))
        ) problems
        ORDER BY problem;
        """;

    public static void SeedDefaultTenant(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            $"""
            INSERT INTO "Tenants" ("Id", "Code", "NameAr", "NameEn", "IsActive", "CreatedAt")
            SELECT '{DefaultTenantId}', 'PLATFORM', N'الجهة الافتراضية', 'Default Platform Tenant', TRUE, NOW()
            WHERE NOT EXISTS (SELECT 1 FROM "Tenants" WHERE "Id" = '{DefaultTenantId}');
            """);
    }
}
