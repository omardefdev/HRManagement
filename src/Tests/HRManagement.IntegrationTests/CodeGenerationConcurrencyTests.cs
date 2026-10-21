using System.Collections.Concurrent;
using HR.Core.Domain.Entities.CodeGeneration;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.CodeGeneration;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-04 AC-13: parallel code generation on PostgreSQL (RUN_TESTCONTAINERS=true).</summary>
public class CodeGenerationConcurrencyTests : IAsyncLifetime
{
    private const int ParallelGenerations = 250;
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private PostgreSqlContainer? _postgres;

    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable("RUN_TESTCONTAINERS"), "true", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync()
    {
        if (!Enabled) return;
        _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _postgres.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_postgres is not null)
            await _postgres.DisposeAsync();
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task AssetTag_ParallelGenerate_AllCodesUnique_CounterMatches()
    {
        Skip.IfNot(Enabled, "Set RUN_TESTCONTAINERS=true");

        await using var db = await MigrateHRCoreAsync();
        await CodeGenerationSeeder.SeedAsync(db);
        TenantIdFlow.Set(TenantId);

        var codes = new ConcurrentBag<string>();
        var errors = new ConcurrentBag<Exception>();

        await Parallel.ForEachAsync(
            Enumerable.Range(0, ParallelGenerations),
            new ParallelOptions { MaxDegreeOfParallelism = 50 },
            async (_, ct) =>
            {
                try
                {
                    await using var local = new HRCoreDbContext(NpgsqlOpts<HRCoreDbContext>(), Tenant());
                    var generator = new CodeGeneratorService(local, new MemoryCache(new MemoryCacheOptions()), NullLogger<CodeGeneratorService>.Instance);
                    var result = await generator.GenerateAsync(new CodeGenerationRequest
                    {
                        EntityKind = CodeEntityKind.AssetTag,
                        TenantId = TenantId,
                        Preview = false
                    }, ct);
                    codes.Add(result.Code);
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

        Assert.Empty(errors);
        Assert.Equal(ParallelGenerations, codes.Count);
        Assert.Equal(ParallelGenerations, codes.Distinct(StringComparer.Ordinal).Count());

        var issuanceCount = await db.CodeIssuances.CountAsync(i => i.TenantId == TenantId && i.EntityKind == CodeEntityKind.AssetTag);
        Assert.Equal(ParallelGenerations, issuanceCount);

        var counter = await db.CodeSequenceCounters
            .Where(c => c.TenantId == TenantId && c.EntityKind == CodeEntityKind.AssetTag)
            .OrderByDescending(c => c.LastValue)
            .FirstOrDefaultAsync();
        Assert.NotNull(counter);
        Assert.Equal(ParallelGenerations, counter!.LastValue);
    }

    private async Task<HRCoreDbContext> MigrateHRCoreAsync()
    {
        var db = new HRCoreDbContext(NpgsqlOpts<HRCoreDbContext>(), Tenant());
        await db.Database.MigrateAsync();
        return db;
    }

    private string Cs => _postgres!.GetConnectionString();

    private DbContextOptions<T> NpgsqlOpts<T>() where T : DbContext =>
        new DbContextOptionsBuilder<T>()
            .UseNpgsql(Cs, npgsql => npgsql.MigrationsAssembly(typeof(T).Assembly.GetName().Name))
            .Options;

    private static FixedTenantContext Tenant() => new(TenantId);

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
