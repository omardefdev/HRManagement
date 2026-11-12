using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-51 (§3, P-5): the tenant filter of the gateway's audit query on PostgreSQL, on the migrated schema: three agencies and rows with no
/// agency. Each agency sees its own rows, the platform sees all of them, a caller without a valid tenant sees none, and no agency sees a
/// row with no agency. The table has no row security: the filter is the query service's, so the fixture's connection proves it.
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam051GatewayAuditPostgreSqlTests(PostgreSqlFixture fixture)
{
    private static readonly Guid AgencyA = Guid.Parse("a1000000-0000-0000-0000-00000000000a");
    private static readonly Guid AgencyB = Guid.Parse("b1000000-0000-0000-0000-00000000000b");
    private static readonly Guid AgencyC = Guid.Parse("c1000000-0000-0000-0000-00000000000c");

    private static GatewayAuditEntry Row(Guid? tenantId, string corr, int status = 200) => new()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-2),
        CorrelationId = corr,
        Method = "GET",
        Path = "/ui/hr-core/Employees",
        QueryString = "",
        StatusCode = status,
        ElapsedMs = 4,
        Decision = status == 403 ? "forbidden" : "allowed",
        TenantId = tenantId,
    };

    private async Task<GatewayAuditDbContext> CreateAsync()
    {
        var connection = await fixture.CreateDatabaseAsync("iam051_audit_" + Guid.NewGuid().ToString("N")[..12]);
        var db = new GatewayAuditDbContext(new DbContextOptionsBuilder<GatewayAuditDbContext>()
            .UseNpgsql(connection, npgsql => npgsql.MigrationsAssembly(typeof(GatewayAuditDbContext).Assembly.GetName().Name))
            .Options);
        await db.Database.MigrateAsync();
        db.AuditEntries.AddRange(
            Row(AgencyA, "a-1"), Row(AgencyA, "a-2", 403),
            Row(AgencyB, "b-1"), Row(AgencyB, "b-2"), Row(AgencyB, "b-3", 403),
            Row(AgencyC, "c-1"),
            Row(null, "none-1"), Row(null, "none-2", 403));
        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<(IReadOnlyList<string> Ids, int Count403)> SearchAsync(GatewayAuditDbContext db, GatewayAuditScope scope)
    {
        var result = await new GatewayAuditQueryService(db).SearchAsync(new GatewayAuditSearchRequest { TimeRange = "1h" }, scope, CancellationToken.None);
        Assert.Equal(result.Items.Count, result.TotalCount);
        return (result.Items.Select(i => i.CorrelationId).Order(StringComparer.Ordinal).ToList(), result.Count403);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Each_agency_sees_its_own_rows_and_the_platform_sees_all()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");
        await using var db = await CreateAsync();

        var a = await SearchAsync(db, GatewayAuditScope.Tenant(AgencyA));
        Assert.Equal(["a-1", "a-2"], a.Ids);
        Assert.Equal(1, a.Count403);

        var b = await SearchAsync(db, GatewayAuditScope.Tenant(AgencyB));
        Assert.Equal(["b-1", "b-2", "b-3"], b.Ids);
        Assert.Equal(1, b.Count403);

        Assert.Equal(["c-1"], (await SearchAsync(db, GatewayAuditScope.Tenant(AgencyC))).Ids);

        var platform = await SearchAsync(db, GatewayAuditScope.Platform);
        Assert.Equal(["a-1", "a-2", "b-1", "b-2", "b-3", "c-1", "none-1", "none-2"], platform.Ids);
        Assert.Equal(3, platform.Count403);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task No_agency_sees_a_row_with_no_agency_and_no_tenant_sees_nothing()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");
        await using var db = await CreateAsync();

        foreach (var agency in new[] { AgencyA, AgencyB, AgencyC })
            Assert.DoesNotContain((await SearchAsync(db, GatewayAuditScope.Tenant(agency))).Ids, id => id.StartsWith("none-", StringComparison.Ordinal));

        Assert.Empty((await SearchAsync(db, GatewayAuditScope.None)).Ids);
        Assert.Empty((await SearchAsync(db, GatewayAuditScope.Tenant(Guid.Empty))).Ids);
        Assert.Empty((await SearchAsync(db, GatewayAuditScope.Tenant(Guid.NewGuid()))).Ids);
    }
}
