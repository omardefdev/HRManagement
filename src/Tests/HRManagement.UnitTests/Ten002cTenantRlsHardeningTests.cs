using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace HRManagement.UnitTests;

/// <summary>
/// TEN-002C: PostgreSQL exempts a table's owner from row level security unless FORCE is set,
/// and a policy with only USING still permits writing another tenant's rows. Both gaps made
/// tenant isolation inert, so the emitted SQL is asserted directly.
/// </summary>
public class Ten002cTenantRlsHardeningTests
{
    private static IReadOnlyList<string> EmittedSql(Action<MigrationBuilder> act)
    {
        var builder = new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL");
        act(builder);
        return builder.Operations.OfType<SqlOperation>().Select(o => o.Sql).ToList();
    }

    [Fact]
    public void EnableRls_forces_row_level_security()
    {
        var sql = string.Join("\n", EmittedSql(b => TenantRlsMigrationHelper.EnableRls(b, "Employees")));

        Assert.Contains("ENABLE ROW LEVEL SECURITY", sql);
        Assert.Contains("FORCE ROW LEVEL SECURITY", sql);
    }

    [Fact]
    public void EnableRls_policy_carries_with_check()
    {
        var sql = string.Join("\n", EmittedSql(b => TenantRlsMigrationHelper.EnableRls(b, "Employees")));

        Assert.Contains("CREATE POLICY tenant_isolation", sql);
        Assert.Contains("WITH CHECK", sql);
    }

    [Fact]
    public void EnableRls_predicate_matches_nothing_when_session_tenant_unset()
    {
        var sql = string.Join("\n", EmittedSql(b => TenantRlsMigrationHelper.EnableRls(b, "Employees")));

        // NULLIF(..., '') yields NULL for an unset session, and "TenantId" = NULL is never true,
        // so a connection that forgot to set app.current_tenant sees and writes no rows.
        Assert.Contains("NULLIF(current_setting('app.current_tenant', true), '')", sql);
    }

    [Fact]
    public void EnableRls_applies_to_every_requested_table()
    {
        var sql = string.Join("\n", EmittedSql(b =>
            TenantRlsMigrationHelper.EnableRls(b, "Employees", "Contracts", "PayrollRuns")));

        foreach (var table in new[] { "Employees", "Contracts", "PayrollRuns" })
        {
            Assert.Contains($"""ALTER TABLE "{table}" FORCE ROW LEVEL SECURITY""", sql);
        }
    }

    [Fact]
    public void BulkHardening_forces_rls_and_adds_with_check()
    {
        var sql = string.Join("\n", EmittedSql(TenantRlsMigrationHelper.EnableRlsOnAllTenantIdTables));

        Assert.Contains("FORCE ROW LEVEL SECURITY", sql);
        Assert.Contains("WITH CHECK", sql);
    }

    [Fact]
    public void BulkHardening_skips_tables_the_current_role_does_not_own()
    {
        // ALTER TABLE requires ownership; without this guard the whole migration aborts on the
        // first table owned by another role instead of hardening the ones it can.
        Assert.Contains(
            "pg_catalog.pg_get_userbyid(c.relowner) = current_user",
            TenantRlsMigrationHelper.HardenAllTenantIdTablesSql);
    }

    [Fact]
    public void BulkHardening_excludes_platform_and_messaging_tables()
    {
        var sql = TenantRlsMigrationHelper.HardenAllTenantIdTablesSql;

        Assert.Contains("'Tenants'", sql);
        Assert.Contains("InboxState%", sql);
        Assert.Contains("OutboxMessage%", sql);
    }

    [Fact]
    public void Verification_query_flags_missing_force_and_with_check()
    {
        var sql = TenantRlsMigrationHelper.VerifyTenantRlsSql;

        Assert.Contains("NOT c.relrowsecurity", sql);
        Assert.Contains("NOT c.relforcerowsecurity", sql);
        Assert.Contains("p.polwithcheck IS NOT NULL", sql);
    }

    [Fact]
    public void DisableRls_removes_policy_before_disabling()
    {
        var sql = string.Join("\n", EmittedSql(b => TenantRlsMigrationHelper.DisableRls(b, "Employees")));

        var dropIndex = sql.IndexOf("DROP POLICY", StringComparison.Ordinal);
        var disableIndex = sql.IndexOf("DISABLE ROW LEVEL SECURITY", StringComparison.Ordinal);
        Assert.True(dropIndex >= 0 && disableIndex > dropIndex);
    }
}
