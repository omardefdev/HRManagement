using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace HRManagement.IntegrationTests;

[Collection("PostgreSql")]
public class HRContractsDbContextPostgreSqlTests
{
    private readonly PostgreSqlFixture _fixture;

    public HRContractsDbContextPostgreSqlTests(PostgreSqlFixture fixture) => _fixture = fixture;

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task ContractsDbContext_migrates_against_postgres_container()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
            .Options;

        await using var db = new ContractsDbContext(options);
        await db.Database.MigrateAsync();
        Assert.True(await db.Database.CanConnectAsync());
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task ContractsDbContext_saves_unspecified_signed_at_to_postgres()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker to run this test.");

        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseNpgsql(_fixture.ConnectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(ContractsDbContext).Assembly.GetName().Name))
            .Options;

        await using var db = new ContractsDbContext(options);
        await db.Database.MigrateAsync();

        var typeId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        db.ContractTypes.Add(new ContractType
        {
            Id = typeId,
            Code = $"T-{suffix}",
            NameAr = "اختبار",
            NameEn = "Test"
        });
        db.Contracts.Add(new Contract
        {
            Id = contractId,
            EmployeeId = Guid.NewGuid(),
            ContractTypeId = typeId,
            StartDate = new DateOnly(2025, 1, 1),
            Status = ContractStatus.Active
        });
        await db.SaveChangesAsync();

        var signedAt = new DateTime(2025, 6, 1, 14, 30, 0);
        Assert.Equal(DateTimeKind.Unspecified, signedAt.Kind);

        db.ContractSignatures.Add(new ContractSignature
        {
            ContractId = contractId,
            SignedBy = Guid.NewGuid(),
            SignedAt = signedAt,
            SignatureType = SignatureType.Digital
        });

        await db.SaveChangesAsync();

        var saved = await db.ContractSignatures
            .AsNoTracking()
            .SingleAsync(s => s.ContractId == contractId);

        Assert.Equal(DateTimeKind.Utc, saved.SignedAt.Kind);
        Assert.Equal(signedAt, saved.SignedAt);
    }
}
