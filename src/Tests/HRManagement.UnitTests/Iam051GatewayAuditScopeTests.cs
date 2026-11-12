using System.Security.Claims;
using HRManagement.Gateway.Controllers;
using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using HRManagement.ServiceDefaults;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-51 (§3, P-5, D-IAM-7): the gateway's audit log is for its owners (<c>Gateway.Audit</c>: platform-admin, auditor, compliance; not
/// hr-admin), and each agency sees its own rows. The filter is in <see cref="GatewayAuditQueryService"/>; the rows with no agency are the
/// platform's alone; a caller without a valid tenant sees nothing. Proved on the in-memory provider and on SQLite; PostgreSQL in
/// <c>Iam051GatewayAuditPostgreSqlTests</c>.
/// </summary>
public sealed class Iam051GatewayAuditScopeTests
{
    private static readonly Guid AgencyA = Guid.Parse("a0000000-0000-0000-0000-00000000000a");
    private static readonly Guid AgencyB = Guid.Parse("b0000000-0000-0000-0000-00000000000b");
    private static readonly Guid AgencyC = Guid.Parse("c0000000-0000-0000-0000-00000000000c");

    private static GatewayAuditEntry Row(Guid? tenantId, string corr) => new()
    {
        OccurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
        CorrelationId = corr,
        Method = "GET",
        Path = "/ui/hr-core",
        QueryString = "",
        StatusCode = 200,
        ElapsedMs = 3,
        Decision = "allowed",
        TenantId = tenantId,
    };

    private static void Seed(GatewayAuditDbContext db)
    {
        db.AuditEntries.AddRange(
            Row(AgencyA, "a-1"), Row(AgencyA, "a-2"),
            Row(AgencyB, "b-1"),
            Row(AgencyC, "c-1"),
            Row(null, "none-1"), Row(null, "none-2"));
        db.SaveChanges();
    }

    public static TheoryData<string> Providers() => new("inmemory", "sqlite");

    private static (GatewayAuditDbContext Db, IDisposable? Connection) Create(string provider)
    {
        if (provider == "inmemory")
            return (new GatewayAuditDbContext(new DbContextOptionsBuilder<GatewayAuditDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options), null);

        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var db = new GatewayAuditDbContext(new DbContextOptionsBuilder<GatewayAuditDbContext>()
            .UseSqlite(connection)
            .ReplaceService<IModelCustomizer, SqliteDateTimeOffsetCustomizer>()
            .Options);
        db.Database.EnsureCreated();
        return (db, connection);
    }

    /// <summary>
    /// The gateway runs on PostgreSQL; SQLite cannot compare <see cref="DateTimeOffset"/> columns, so this test-only customizer stores them as
    /// numbers. The query service and its filter are the production ones.
    /// </summary>
    private sealed class SqliteDateTimeOffsetCustomizer(ModelCustomizerDependencies dependencies) : RelationalModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            foreach (var property in modelBuilder.Model.GetEntityTypes().SelectMany(e => e.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                property.SetValueConverter(new DateTimeOffsetToBinaryConverter());
        }
    }

    private static async Task<IReadOnlyList<string>> CorrelationIdsAsync(string provider, GatewayAuditScope scope)
    {
        var (db, connection) = Create(provider);
        using (connection)
        await using (db)
        {
            Seed(db);
            var result = await new GatewayAuditQueryService(db).SearchAsync(new GatewayAuditSearchRequest(), scope, CancellationToken.None);
            Assert.Equal(result.Items.Count, result.TotalCount);
            return result.Items.Select(i => i.CorrelationId).Order(StringComparer.Ordinal).ToList();
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task An_agency_sees_its_own_rows_only(string provider)
    {
        Assert.Equal(["a-1", "a-2"], await CorrelationIdsAsync(provider, GatewayAuditScope.Tenant(AgencyA)));
        Assert.Equal(["b-1"], await CorrelationIdsAsync(provider, GatewayAuditScope.Tenant(AgencyB)));
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task The_platform_sees_every_agency_and_the_rows_with_no_agency(string provider) =>
        Assert.Equal(["a-1", "a-2", "b-1", "c-1", "none-1", "none-2"], await CorrelationIdsAsync(provider, GatewayAuditScope.Platform));

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task A_caller_without_a_valid_tenant_sees_nothing(string provider)
    {
        Assert.Empty(await CorrelationIdsAsync(provider, GatewayAuditScope.None));
        Assert.Empty(await CorrelationIdsAsync(provider, GatewayAuditScope.Tenant(Guid.Empty)));
    }

    private static ClaimsPrincipal User(string? tenant, params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r)).ToList();
        if (tenant is not null)
            claims.Add(new Claim(TenantContext.TenantClaimType, tenant));
        claims.Add(new Claim("azp", "hr-gateway-client"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    [Fact]
    public void The_scope_comes_from_the_caller_platform_admin_first()
    {
        Assert.Equal(GatewayAuditScope.Platform, GatewayAuditScope.For(User(AgencyA.ToString(), HrManagementRoles.PlatformAdmin)));
        Assert.Equal(GatewayAuditScope.Platform, GatewayAuditScope.For(User(null, HrManagementRoles.PlatformAdmin)));
        Assert.Equal(GatewayAuditScope.Tenant(AgencyB), GatewayAuditScope.For(User(AgencyB.ToString(), HrManagementRoles.Auditor)));
        Assert.Equal(GatewayAuditScope.None, GatewayAuditScope.For(User("not-a-guid", HrManagementRoles.Compliance)));
        Assert.Equal(GatewayAuditScope.None, GatewayAuditScope.For(User(null, HrManagementRoles.Compliance)));
        Assert.Equal(GatewayAuditScope.None, GatewayAuditScope.For(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    private static async Task<bool> AuditPolicyAsync(ClaimsPrincipal user, IDictionary<string, string?>? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build());
        services.AddHrManagementAuthorizationPolicies();
        await using var provider = services.BuildServiceProvider();
        return (await provider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, HrManagementPolicies.GatewayAudit)).Succeeded;
    }

    [Fact]
    public async Task The_audit_is_for_platform_admin_auditor_and_compliance_not_hr_admin()
    {
        var tenant = AgencyA.ToString();
        Assert.True(await AuditPolicyAsync(User(tenant, HrManagementRoles.PlatformAdmin)));
        Assert.True(await AuditPolicyAsync(User(tenant, HrManagementRoles.Auditor)));
        Assert.True(await AuditPolicyAsync(User(tenant, HrManagementRoles.Compliance)));
        Assert.False(await AuditPolicyAsync(User(tenant, HrManagementRoles.Admin)));
        Assert.False(await AuditPolicyAsync(User(tenant, HrManagementRoles.HrEmployee)));
        Assert.False(await AuditPolicyAsync(User(tenant, HrManagementRoles.HrCore, HrManagementRoles.BranchManager)));
    }

    [Fact]
    public async Task The_audit_roles_come_from_configuration()
    {
        var config = new Dictionary<string, string?> { ["Gateway:Audit:Roles:0"] = HrManagementRoles.PlatformAdmin };
        Assert.True(await AuditPolicyAsync(User(AgencyA.ToString(), HrManagementRoles.PlatformAdmin), config));
        Assert.False(await AuditPolicyAsync(User(AgencyA.ToString(), HrManagementRoles.Auditor), config));
    }

    [Theory]
    [InlineData(nameof(HomeController.Operations))]
    [InlineData(nameof(HomeController.Audit))]
    [InlineData(nameof(HomeController.AuditSearch))]
    [InlineData(nameof(HomeController.MetricsSummary))]
    public void The_three_audit_pages_carry_the_audit_policy(string action)
    {
        var method = typeof(HomeController).GetMethods().Single(m => m.Name == action);
        var policies = method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Select(a => a.Policy);
        Assert.Contains(HrManagementPolicies.GatewayAudit, policies);
    }
}
