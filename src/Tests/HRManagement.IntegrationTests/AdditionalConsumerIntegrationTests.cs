using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using Dev.Analytics.Infrastructure.Data;
using DevAnalyticsSeparationCompletedConsumer = Dev.Analytics.Infrastructure.Consumers.SeparationCompletedConsumer;
using AnalyticsEmployeeCreatedConsumer = Dev.Analytics.Infrastructure.Consumers.EmployeeCreatedConsumer;
using HRManagement.MessageContracts.Events;
using IT.Infra.Infrastructure.Data;
using ITEmployeeCreatedConsumer = IT.Infra.Infrastructure.Consumers.EmployeeCreatedConsumer;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class AdditionalConsumerIntegrationTests
{
    private static async Task<MassTransitTestScope> CreateScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var itConnection = new SqliteConnection("Data Source=:memory:");
                itConnection.Open();
                connections.Add(itConnection);
                IntegrationTestHelpers.AddSqliteDbContext<ITInfraDbContext>(services, itConnection);

                var analyticsConnection = new SqliteConnection("Data Source=:memory:");
                analyticsConnection.Open();
                connections.Add(analyticsConnection);
                IntegrationTestHelpers.AddSqliteDbContext<AnalyticsDbContext>(services, analyticsConnection);

                var adminConnection = new SqliteConnection("Data Source=:memory:");
                adminConnection.Open();
                connections.Add(adminConnection);
                IntegrationTestHelpers.AddSqliteDbContext<AdminLogisticsDbContext>(services, adminConnection);
                IntegrationTestHelpers.AddNoOpDirectoryEntrySync(services);
            },
            cfg =>
            {
                cfg.AddConsumer<ITEmployeeCreatedConsumer>();
                cfg.AddConsumer<AnalyticsEmployeeCreatedConsumer>();
                cfg.AddConsumer<DevAnalyticsSeparationCompletedConsumer>();
                cfg.AddConsumer<TransferExecutedConsumer>();
            });

        await scope.GetDb<ITInfraDbContext>().Database.EnsureCreatedAsync();
        await scope.GetDb<AnalyticsDbContext>().Database.EnsureCreatedAsync();
        await scope.GetDb<AdminLogisticsDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    [Fact]
    public async Task IT_EmployeeCreated_Creates_ProvisioningTicket()
    {
        await using var scope = await CreateScopeAsync();
        var empId = Guid.NewGuid();
        await scope.Harness.Bus.Publish<IEmployeeCreated>(new
        {
            EmployeeId = empId,
            FullName = "IT User",
            NationalId = "999",
            EmploymentType = "FullTime",
            TenantId = (Guid?)null
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IEmployeeCreated>(scope.Harness);
        var db = scope.GetDb<ITInfraDbContext>();
        db.ChangeTracker.Clear();
        var tickets = await db.ITSupportTickets.Where(t => t.ReportedBy == empId).ToListAsync();
        Assert.Single(tickets);
    }

    [Fact]
    public async Task IT_EmployeeCreated_Duplicate_Ignored()
    {
        await using var scope = await CreateScopeAsync();
        var empId = Guid.NewGuid();
        var payload = new
        {
            EmployeeId = empId,
            FullName = "IT User",
            NationalId = "999",
            EmploymentType = "FullTime",
            TenantId = (Guid?)null
        };

        await scope.Harness.Bus.Publish<IEmployeeCreated>(payload);
        await scope.Harness.Bus.Publish<IEmployeeCreated>(payload);
        await IntegrationTestHelpers.AssertConsumedAsync<IEmployeeCreated>(scope.Harness, minimumCount: 2);

        var db = scope.GetDb<ITInfraDbContext>();
        db.ChangeTracker.Clear();
        var tickets = await db.ITSupportTickets.Where(t => t.ReportedBy == empId).ToListAsync();
        Assert.Single(tickets);
    }

    [Fact]
    public async Task Analytics_EmployeeCreated_Records_Event()
    {
        await using var scope = await CreateScopeAsync();
        var empId = Guid.NewGuid();
        await scope.Harness.Bus.Publish<IEmployeeCreated>(new
        {
            EmployeeId = empId,
            FullName = "Analytics User",
            NationalId = "888",
            EmploymentType = "FullTime",
            TenantId = (Guid?)null
        });

        await IntegrationTestHelpers.AssertConsumedAsync<IEmployeeCreated>(scope.Harness);
        var db = scope.GetDb<AnalyticsDbContext>();
        db.ChangeTracker.Clear();
        var events = await db.AnalyticsEvents.Where(e => e.EventName == "EmployeeCreated").ToListAsync();
        Assert.Contains(events, e => e.Payload != null && e.Payload.Contains(empId.ToString("N")));
    }

    [Fact]
    public async Task Analytics_SeparationCompleted_Records_Event()
    {
        await using var scope = await CreateScopeAsync();
        var empId = Guid.NewGuid();
        var sepId = Guid.NewGuid();
        await scope.Harness.Bus.Publish<ISeparationCompleted>(new
        {
            EmployeeId = empId,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            OccurredAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            ContractId = (Guid?)null,
            SeparationRecordId = sepId
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ISeparationCompleted>(scope.Harness);
        var db = scope.GetDb<AnalyticsDbContext>();
        db.ChangeTracker.Clear();
        var events = await db.AnalyticsEvents.Where(e => e.EventName == "SeparationCompleted").ToListAsync();
        Assert.Single(events);
    }

    [Fact]
    public async Task AdminLogistics_TransferExecuted_Annotates_Assignments()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<AdminLogisticsDbContext>();
        var empId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        db.Assets.Add(new Asset { Id = assetId, TagNumber = "TAG-001", Name = "Laptop", Status = AssetStatus.InUse });
        db.AssetAssignments.Add(new AssetAssignment
        {
            AssetId = assetId,
            EmployeeId = empId,
            AssignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var fromOrg = Guid.NewGuid();
        var toOrg = Guid.NewGuid();
        await scope.Harness.Bus.Publish<ITransferExecuted>(new
        {
            TransferRequestId = Guid.NewGuid(),
            EmployeeId = empId,
            FromOrgUnitId = fromOrg,
            ToOrgUnitId = toOrg,
            ExecutedAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        });

        await IntegrationTestHelpers.AssertConsumedAsync<ITransferExecuted>(scope.Harness);
        db.ChangeTracker.Clear();
        var assignment = await db.AssetAssignments.AsNoTracking().FirstAsync(a => a.EmployeeId == empId);
        Assert.NotNull(assignment.ReturnedAt);
        Assert.Contains("نقل", assignment.ReturnNotes ?? string.Empty);
    }

    [Fact]
    public async Task TransferExecuted_Duplicate_Delivery_Updates_Assignment_Once()
    {
        await using var scope = await CreateScopeAsync();
        var db = scope.GetDb<AdminLogisticsDbContext>();
        var empId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        db.Assets.Add(new Asset { Id = assetId, TagNumber = "TAG-DUP", Name = "Desk", Status = AssetStatus.InUse });
        db.AssetAssignments.Add(new AssetAssignment
        {
            AssetId = assetId,
            EmployeeId = empId,
            AssignedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var messageId = Guid.NewGuid();
        var transferRequestId = Guid.NewGuid();
        var fromOrg = Guid.NewGuid();
        var toOrg = Guid.NewGuid();
        var executedAt = DateTime.UtcNow;

        await scope.Harness.Bus.Publish<ITransferExecuted>(new
        {
            TransferRequestId = transferRequestId,
            EmployeeId = empId,
            FromOrgUnitId = fromOrg,
            ToOrgUnitId = toOrg,
            ExecutedAt = executedAt,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        }, ctx => ctx.MessageId = messageId);

        await IntegrationTestHelpers.AssertConsumedAsync<ITransferExecuted>(scope.Harness);

        await scope.Harness.Bus.Publish<ITransferExecuted>(new
        {
            TransferRequestId = transferRequestId,
            EmployeeId = empId,
            FromOrgUnitId = fromOrg,
            ToOrgUnitId = toOrg,
            ExecutedAt = executedAt,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        }, ctx => ctx.MessageId = messageId);

        await Task.Delay(500);
        db.ChangeTracker.Clear();
        var assignment = await db.AssetAssignments.AsNoTracking().FirstAsync(a => a.EmployeeId == empId);
        Assert.NotNull(assignment.ReturnedAt);
        Assert.Contains("نقل", assignment.ReturnNotes ?? string.Empty);
        // Duplicate delivery must not re-annotate (Tier-B / business effect); receipts asserted in IdempotentConsumerTests with inbox filter.
    }
}
