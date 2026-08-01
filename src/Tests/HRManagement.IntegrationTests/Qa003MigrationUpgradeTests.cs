using File.Storage.Infrastructure.Data;
using HR.Contracts.Infrastructure.Data;
using HR.Core.Infrastructure.Data;
using HR.Movements.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>QA-003-B: empty→head and N−1→head migrations on real PostgreSQL.</summary>
[Collection("PostgreSql")]
public class Qa003MigrationUpgradeTests
{
    private readonly PostgreSqlFixture _fixture;

    public Qa003MigrationUpgradeTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Payroll_Empty_To_Head_Migrates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertEmptyToHeadAsync<PayrollDbContext>(typeof(PayrollDbContext).Assembly.GetName().Name!);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Contracts_Empty_To_Head_Migrates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertEmptyToHeadAsync<ContractsDbContext>(typeof(ContractsDbContext).Assembly.GetName().Name!);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task HRCore_Empty_To_Head_Migrates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertEmptyToHeadAsync<HRCoreDbContext>(typeof(HRCoreDbContext).Assembly.GetName().Name!);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task FileStorage_Empty_To_Head_Migrates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertEmptyToHeadAsync<FileStorageDbContext>(typeof(FileStorageDbContext).Assembly.GetName().Name!);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Movements_Empty_To_Head_Migrates()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertEmptyToHeadAsync<MovementsDbContext>(typeof(MovementsDbContext).Assembly.GetName().Name!);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Payroll_NMinus1_To_Head_Upgrades()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertNMinus1ToHeadAsync<PayrollDbContext>(typeof(PayrollDbContext).Assembly.GetName().Name!);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Contracts_NMinus1_To_Head_Upgrades()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await AssertNMinus1ToHeadAsync<ContractsDbContext>(typeof(ContractsDbContext).Assembly.GetName().Name!);
    }

    private async Task AssertEmptyToHeadAsync<TContext>(string migrationsAssembly)
        where TContext : DbContext
    {
        var cs = await _fixture.CreateDatabaseAsync($"qa003_empty_{typeof(TContext).Name}_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly(migrationsAssembly))
            .Options;

        await using var db = (TContext)Activator.CreateInstance(typeof(TContext), options, null)!;
        await db.Database.MigrateAsync();
        Assert.True(await db.Database.CanConnectAsync());
        var applied = await db.Database.GetAppliedMigrationsAsync();
        Assert.NotEmpty(applied);
    }

    private async Task AssertNMinus1ToHeadAsync<TContext>(string migrationsAssembly)
        where TContext : DbContext
    {
        var cs = await _fixture.CreateDatabaseAsync($"qa003_upg_{typeof(TContext).Name}_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(cs, npgsql => npgsql.MigrationsAssembly(migrationsAssembly))
            .Options;

        await using var db = (TContext)Activator.CreateInstance(typeof(TContext), options, null)!;
        var all = db.Database.GetMigrations().ToList();
        Assert.True(all.Count >= 2, $"{typeof(TContext).Name} needs ≥2 migrations for N−1→head");

        var nMinus1 = all[^2];
        await db.Database.MigrateAsync(nMinus1);
        var mid = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Contains(nMinus1, mid);
        Assert.DoesNotContain(all[^1], mid);

        await db.Database.MigrateAsync();
        var head = (await db.Database.GetAppliedMigrationsAsync()).ToList();
        Assert.Equal(all.Count, head.Count);
        Assert.Contains(all[^1], head);
    }
}

/// <summary>QA-003-B: two isolated databases on one container without cross-talk.</summary>
[Collection("PostgreSql")]
public class Qa003ParallelDbIsolationTests
{
    private readonly PostgreSqlFixture _fixture;

    public Qa003ParallelDbIsolationTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Parallel_Databases_Do_Not_CrossTalk()
    {
        Skip.IfNot(RealInfra.IsEnabled);

        var csA = await _fixture.CreateDatabaseAsync($"qa003_iso_a_{Guid.NewGuid():N}");
        var csB = await _fixture.CreateDatabaseAsync($"qa003_iso_b_{Guid.NewGuid():N}");

        var optsA = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(csA, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options;
        var optsB = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(csB, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options;

        await using var dbA = new PayrollDbContext(optsA);
        await using var dbB = new PayrollDbContext(optsB);
        await dbA.Database.MigrateAsync();
        await dbB.Database.MigrateAsync();

        var marker = Guid.NewGuid();
        dbA.IdempotencyRecords.Add(new HRManagement.ServiceDefaults.IdempotencyRecord
        {
            TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Key = "iso-" + marker.ToString("N"),
            RequestHash = "h",
            ResponseStatus = 200,
            ResponseBody = "{}"
        });
        await dbA.SaveChangesAsync();

        Assert.Equal(1, await dbA.IdempotencyRecords.CountAsync());
        Assert.Equal(0, await dbB.IdempotencyRecords.CountAsync());
    }
}
