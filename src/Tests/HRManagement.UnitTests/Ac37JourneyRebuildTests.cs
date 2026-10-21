using HRManagement.Gateway.Data;
using HRManagement.Gateway.Models;
using HRManagement.Gateway.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HRManagement.UnitTests;

public class Ac37JourneyRebuildTests
{
    [Fact]
    public async Task JourneyRebuild_collects_gateway_http_events_with_tenant_scope()
    {
        var corr = Guid.NewGuid().ToString("D");
        var tenant = Guid.Parse("a0000001-0000-4000-8000-000000000001");
        var scopeOrgUnitId = Guid.Parse("a0000001-0000-4000-8000-000000000102");

        await using var sp = CreateProvider("ac37-" + Guid.NewGuid().ToString("N"));
        using (var diScope = sp.CreateScope())
        {
            var db = diScope.ServiceProvider.GetRequiredService<GatewayAuditDbContext>();
            var entry = new GatewayAuditEntry
            {
                OccurredAtUtc = DateTimeOffset.UtcNow,
                SequenceNumber = 1,
                CorrelationId = corr,
                UserName = "branch-mgr-br1",
                Method = "GET",
                Path = "/api/hr-core/api/lookup/employees",
                QueryString = "",
                StatusCode = 200,
                ElapsedMs = 45,
                Decision = "allowed",
                TenantId = tenant,
                ScopeOrgUnitId = scopeOrgUnitId,
                ChainKey = AuditHashChain.DefaultChainKey
            };
            AuditHashChain.Seal(entry, AuditHashChain.GenesisPrevHash);
            db.AuditEntries.Add(entry);
            await db.SaveChangesAsync();
        }

        var service = sp.GetRequiredService<IJourneyRebuildService>();
        var journey = await service.RebuildAsync(corr, authorizationHeader: null);

        Assert.Equal(corr, journey.CorrelationId);
        Assert.Equal(tenant, journey.TenantId);
        Assert.Equal(scopeOrgUnitId, journey.ScopeOrgUnitId);
        Assert.Single(journey.HttpEvents);
        Assert.Empty(journey.Gaps);
    }

    [Fact]
    public void Tenant_scope_fields_affect_hash_chain()
    {
        var withScope = new GatewayAuditEntry
        {
            OccurredAtUtc = DateTimeOffset.Parse("2026-07-28T12:00:00Z"),
            SequenceNumber = 1,
            CorrelationId = "corr",
            Method = "GET",
            Path = "/api/x",
            StatusCode = 200,
            ElapsedMs = 1,
            Decision = "allowed",
            TenantId = Guid.Parse("a0000001-0000-4000-8000-000000000001"),
            ScopeOrgUnitId = Guid.Parse("a0000001-0000-4000-8000-000000000102"),
            ChainKey = AuditHashChain.DefaultChainKey
        };
        AuditHashChain.Seal(withScope, AuditHashChain.GenesisPrevHash);

        var withoutScope = new GatewayAuditEntry
        {
            OccurredAtUtc = DateTimeOffset.Parse("2026-07-28T12:00:00Z"),
            SequenceNumber = 1,
            CorrelationId = "corr",
            Method = "GET",
            Path = "/api/x",
            StatusCode = 200,
            ElapsedMs = 1,
            Decision = "allowed",
            ChainKey = AuditHashChain.DefaultChainKey
        };
        AuditHashChain.Seal(withoutScope, AuditHashChain.GenesisPrevHash);

        Assert.NotEqual(withScope.EventHash, withoutScope.EventHash);
    }

    private static ServiceProvider CreateProvider(string dbName)
    {
        var services = new ServiceCollection();
        services.AddDbContext<GatewayAuditDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integration:HrCoreBaseUrl"] = "http://localhost:5010"
            })
            .Build());
        services.AddLogging();
        services.AddHttpClient("hr-core-journey")
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler());
        services.AddScoped<IJourneyRebuildService, JourneyRebuildService>();
        return services.BuildServiceProvider();
    }
}
