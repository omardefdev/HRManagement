using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using Auth.Directory.Domain.Entities;
using Auth.Directory.Infrastructure.Consumers;
using Auth.Directory.Infrastructure.Data;
using Auth.Directory.Infrastructure.Keycloak;
using HRManagement.MessageContracts.Events;
using HRManagement.SharedKernel.Entities;
using IDCard.Domain.Entities;
using IDCard.Domain.Enums;
using IDCard.Infrastructure.Consumers;
using IDCard.Infrastructure.Data;
using IT.Infra.Infrastructure.Consumers;
using IT.Infra.Infrastructure.Data;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using AdminSeparationConsumer = Admin.Logistics.Infrastructure.Consumers.SeparationCompletedConsumer;
using AuthSeparationConsumer = Auth.Directory.Infrastructure.Consumers.SeparationCompletedConsumer;
using IdCardSeparationConsumer = IDCard.Infrastructure.Consumers.SeparationCompletedConsumer;
using ItSeparationConsumer = IT.Infra.Infrastructure.Consumers.SeparationCompletedConsumer;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class PeripheralConsumerIntegrationTests
{
    [Fact]
    public async Task AuthDirectory_EmployeeCreated_CreatesAuthUser()
    {
        await using var scope = await CreateAuthScopeAsync();
        var empId = Guid.NewGuid();

        await scope.Harness.Bus.Publish<IEmployeeCreated>(new
        {
            EmployeeId = empId,
            FullName = "New Employee",
            NationalId = "1234567890",
            EmploymentType = "FullTime",
            TenantId = (Guid?)null
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IEmployeeCreated>(scope.Harness);
        var db = scope.GetDb<AuthDirectoryDbContext>();
        db.ChangeTracker.Clear();
        var user = await db.AuthUsers.FirstOrDefaultAsync(u => u.EmployeeId == empId);
        Assert.NotNull(user);
        Assert.True(user!.IsActive);
    }

    [Fact]
    public async Task AuthDirectory_SeparationCompleted_DeactivatesUser()
    {
        await using var scope = await CreateAuthScopeAsync();
        var db = scope.GetDb<AuthDirectoryDbContext>();
        var empId = Guid.NewGuid();
        db.AuthUsers.Add(new AuthUser
        {
            Username = "peripheral.user",
            Email = "p@test.local",
            EmployeeId = empId,
            PasswordHash = "EXTERNAL_KEYCLOAK",
            IsActive = true
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ISeparationCompleted>(new
        {
            EmployeeId = empId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            ContractId = (Guid?)null,
            SeparationRecordId = Guid.NewGuid(),
            SchemaVersion = 1
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        db.ChangeTracker.Clear();
        Assert.False((await db.AuthUsers.FirstAsync(u => u.EmployeeId == empId)).IsActive);
    }

    [Fact]
    public async Task IDCard_SeparationCompleted_RevokesActiveCards()
    {
        await using var scope = await CreateIdCardScopeAsync();
        var db = scope.GetDb<IDCardDbContext>();
        var empId = Guid.NewGuid();
        var designId = Guid.NewGuid();
        db.CardDesigns.Add(new CardDesign { Id = designId, Name = "Standard", ValidityDays = 365 });
        db.IssuedCards.Add(new IssuedCard
        {
            EmployeeId = empId,
            CardDesignId = designId,
            SerialNumber = "CARD-001",
            Status = CardStatus.Active,
            IssuedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ISeparationCompleted>(new
        {
            EmployeeId = empId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            ContractId = (Guid?)null,
            SeparationRecordId = Guid.NewGuid(),
            SchemaVersion = 1
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        db.ChangeTracker.Clear();
        Assert.Equal(CardStatus.Revoked, (await db.IssuedCards.FirstAsync(c => c.EmployeeId == empId)).Status);
    }

    [Fact]
    public async Task AdminLogistics_SeparationCompleted_ReturnsOpenAssets()
    {
        await using var scope = await CreateAdminLogisticsScopeAsync();
        var db = scope.GetDb<AdminLogisticsDbContext>();
        var empId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        db.Assets.Add(new Asset { Id = assetId, TagNumber = "TAG-SEP", Name = "Phone", Status = AssetStatus.InUse });
        db.AssetAssignments.Add(new AssetAssignment
        {
            AssetId = assetId,
            EmployeeId = empId,
            AssignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        await scope.Harness.Bus.Publish<ISeparationCompleted>(new
        {
            EmployeeId = empId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            ContractId = (Guid?)null,
            SeparationRecordId = Guid.NewGuid(),
            SchemaVersion = 1
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        db.ChangeTracker.Clear();
        var assignment = await db.AssetAssignments.FirstAsync(a => a.EmployeeId == empId);
        Assert.NotNull(assignment.ReturnedAt);
        Assert.Equal(AssetStatus.InStorage, (await db.Assets.FindAsync(assetId))!.Status);
    }

    [Fact]
    public async Task ITInfra_SeparationCompleted_RecordsReceipt()
    {
        await using var scope = await CreateItInfraScopeAsync();
        var empId = Guid.NewGuid();

        await scope.Harness.Bus.Publish<ISeparationCompleted>(new
        {
            EmployeeId = empId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            ContractId = (Guid?)null,
            SeparationRecordId = Guid.NewGuid(),
            SchemaVersion = 1
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        var db = scope.GetDb<ITInfraDbContext>();
        db.ChangeTracker.Clear();
        Assert.True(await db.IntegrationMessageReceipts.AnyAsync());
    }

    private static async Task<MassTransitTestScope> CreateAuthScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var conn = new SqliteConnection("Data Source=:memory:");
                conn.Open();
                connections.Add(conn);
                IntegrationTestHelpers.AddSqliteDbContext<AuthDirectoryDbContext>(services, conn);
                services.AddSingleton(Mock.Of<IKeycloakUserProvisioningService>());
                services.AddSingleton(Mock.Of<Microsoft.Extensions.Configuration.IConfiguration>());
            },
            cfg =>
            {
                cfg.AddConsumer<Auth.Directory.Infrastructure.Consumers.EmployeeCreatedConsumer>();
                cfg.AddConsumer<AuthSeparationConsumer>();
            });

        await scope.GetDb<AuthDirectoryDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    private static async Task<MassTransitTestScope> CreateIdCardScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var conn = new SqliteConnection("Data Source=:memory:");
                conn.Open();
                connections.Add(conn);
                IntegrationTestHelpers.AddSqliteDbContext<IDCardDbContext>(services, conn);
            },
            cfg => cfg.AddConsumer<IdCardSeparationConsumer>());

        await scope.GetDb<IDCardDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    private static async Task<MassTransitTestScope> CreateAdminLogisticsScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var conn = new SqliteConnection("Data Source=:memory:");
                conn.Open();
                connections.Add(conn);
                IntegrationTestHelpers.AddSqliteDbContext<AdminLogisticsDbContext>(services, conn);
            },
            cfg => cfg.AddConsumer<AdminSeparationConsumer>());

        await scope.GetDb<AdminLogisticsDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    private static async Task<MassTransitTestScope> CreateItInfraScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var conn = new SqliteConnection("Data Source=:memory:");
                conn.Open();
                connections.Add(conn);
                IntegrationTestHelpers.AddSqliteDbContext<ITInfraDbContext>(services, conn);
            },
            cfg => cfg.AddConsumer<ItSeparationConsumer>(),
            enableInboxFilter: true);

        await scope.GetDb<ITInfraDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }
}
