using Microsoft.EntityFrameworkCore;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

[Collection("PostgreSql")]
public class PayrollDbContextPostgreSqlTests
{
    private readonly PostgreSqlFixture _fixture;

    public PayrollDbContextPostgreSqlTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task PayrollDbContext_migrates_against_postgres_container()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var cs = await _fixture.CreateDatabaseAsync($"payroll_migrate_{Guid.NewGuid():N}");
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseNpgsql(cs, npgsql =>
                npgsql.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .Options;

        await using var db = new PayrollDbContext(options);
        await db.Database.MigrateAsync();
        Assert.True(await db.Database.CanConnectAsync());
    }
}
