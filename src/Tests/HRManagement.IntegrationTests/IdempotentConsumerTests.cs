using Admin.Logistics.Domain.Entities;
using Admin.Logistics.Domain.Enums;
using Admin.Logistics.Infrastructure.Consumers;
using Admin.Logistics.Infrastructure.Data;
using HRManagement.MessageContracts.Commands;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application;
using AdminLogisticsTransferConsumer = Admin.Logistics.Infrastructure.Consumers.TransferExecutedConsumer;
using PayrollContractActivatedConsumer = Payroll.Infrastructure.Consumers.ContractActivatedConsumer;
using PayrollSeparationPartyConsumer = Payroll.Infrastructure.Consumers.SeparationPartyConsumer;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

[Collection("IntegrationSerial")]
public class IdempotentConsumerTests
{
    private static readonly Guid TenantId = TenantDefaults.DefaultTenantId;

    [Fact]
    public async Task TransferExecuted_DuplicateMessageId_IsIdempotent()
    {
        await using var scope = await CreateAdminScopeAsync();
        var db = scope.GetDb<AdminLogisticsDbContext>();
        var empId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var messageId = NewId.NextGuid();

        db.Assets.Add(new Asset
        {
            Id = assetId,
            TagNumber = "TAG-INBOX",
            Name = "Chair",
            Status = AssetStatus.InUse,
            TenantId = TenantId
        });
        db.AssetAssignments.Add(new AssetAssignment
        {
            AssetId = assetId,
            EmployeeId = empId,
            AssignedAt = DateTime.UtcNow,
            TenantId = TenantId
        });
        await db.SaveChangesAsync();

        var payload = new
        {
            TransferRequestId = Guid.NewGuid(),
            EmployeeId = empId,
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = Guid.NewGuid(),
            ExecutedAt = DateTime.UtcNow,
            CorrelationId = (string?)null,
            SourceSystem = "Test",
            SchemaVersion = 1
        };

        await PublishWithTenantAsync(scope, payload, messageId);
        await IntegrationTestHelpers.AssertConsumedAsync<ITransferExecuted>(scope.Harness);
        db.ChangeTracker.Clear();
        var assignment = await db.AssetAssignments.AsNoTracking().FirstAsync(a => a.EmployeeId == empId);
        var firstAnnotation = assignment.ConditionOnAssign;
        Assert.False(string.IsNullOrEmpty(firstAnnotation));

        await PublishWithTenantAsync(scope, payload, messageId);
        await Task.Delay(500);
        db.ChangeTracker.Clear();
        assignment = await db.AssetAssignments.AsNoTracking().FirstAsync(a => a.EmployeeId == empId);
        Assert.Equal(firstAnnotation, assignment.ConditionOnAssign);
        Assert.Equal(1, await db.IntegrationMessageReceipts.CountAsync());
    }

    [Fact]
    public async Task ContractActivated_DuplicateDelivery_CreatesSingleProfile()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var db = scope.GetDb<PayrollDbContext>();
        var empId = Guid.NewGuid();
        var contractId = Guid.NewGuid();
        var messageId = NewId.NextGuid();

        var payload = new
        {
            ContractId = contractId,
            EmployeeId = empId,
            ContractType = "FullTime",
            StartDate = DateTime.UtcNow,
            EndDate = (DateTime?)null,
            BaseSalary = 5000m,
            PayGradeCode = (string?)null,
            CorrelationId = (string?)null,
            SourceSystem = "Test"
        };

        await PublishPayrollAsync(scope, payload, messageId);
        await IntegrationTestHelpers.AssertConsumedAsync<IContractActivated>(scope.Harness);

        await PublishPayrollAsync(scope, payload, messageId);
        await Task.Delay(500);

        db.ChangeTracker.Clear();
        var profiles = await db.EmployeePayProfiles.Where(p => p.EmployeeId == empId).ToListAsync();
        Assert.Single(profiles);
    }

    [Fact]
    public async Task SeparationCompleted_DuplicateDelivery_DeactivatesProfileOnce()
    {
        await using var scope = await CreatePayrollScopeAsync();
        var db = scope.GetDb<PayrollDbContext>();
        var empId = Guid.NewGuid();
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(empId, 9000m));
        await db.SaveChangesAsync();

        var messageId = NewId.NextGuid();
        var sepId = Guid.NewGuid();
        var payload = new
        {
            SeparationRecordId = sepId,
            EmployeeId = empId,
            Party = ProcessParties.Payroll,
            SeparationType = "Resignation",
            EffectiveDate = DateTime.UtcNow,
            ContractId = (Guid?)null,
            CorrelationId = (string?)null,
            TenantId = TenantId,
            Compensate = false,
            RemainingLeaveDays = 0
        };

        await PublishSeparationAsync(scope, payload, messageId);
        await IntegrationTestHelpers.AssertConsumedAsync<IExecuteSeparationParty>(scope.Harness);

        await PublishSeparationAsync(scope, payload, messageId);
        await Task.Delay(500);

        db.ChangeTracker.Clear();
        Assert.Equal(1, await db.EndOfServiceSettlements.CountAsync(s => s.EmployeeId == empId));
        var profile = await db.EmployeePayProfiles.FirstAsync(p => p.EmployeeId == empId);
        Assert.False(profile.IsActive);
    }

    private static Task PublishWithTenantAsync(MassTransitTestScope scope, object payload, Guid messageId) =>
        scope.Harness.Bus.Publish<ITransferExecuted>(payload, ctx =>
        {
            ctx.MessageId = messageId;
            ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantId.ToString("D"));
        });

    private static Task PublishPayrollAsync(MassTransitTestScope scope, object payload, Guid messageId) =>
        scope.Harness.Bus.Publish<IContractActivated>(payload, ctx =>
        {
            ctx.MessageId = messageId;
            ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantId.ToString("D"));
        });

    private static Task PublishSeparationAsync(MassTransitTestScope scope, object payload, Guid messageId) =>
        scope.Harness.Bus.Publish<IExecuteSeparationParty>(payload, ctx =>
        {
            ctx.MessageId = messageId;
            ctx.Headers.Set(TenantIdMiddleware.HeaderName, TenantId.ToString("D"));
        });

    private static async Task<MassTransitTestScope> CreateAdminScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                connections.Add(connection);
                IntegrationTestHelpers.AddSqliteDbContext<AdminLogisticsDbContext>(services, connection);
            },
            cfg => cfg.AddConsumer<AdminLogisticsTransferConsumer>(),
            enableInboxFilter: true);

        await scope.GetDb<AdminLogisticsDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }

    private static async Task<MassTransitTestScope> CreatePayrollScopeAsync()
    {
        var scope = await MassTransitTestScope.CreateAsync(
            (services, connections) =>
            {
                var connection = new SqliteConnection("Data Source=:memory:");
                connection.Open();
                connections.Add(connection);
                IntegrationTestHelpers.AddSqliteDbContext<PayrollDbContext>(services, connection);
                PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
            },
            cfg =>
            {
                cfg.AddConsumer<PayrollContractActivatedConsumer>();
                cfg.AddConsumer<PayrollSeparationPartyConsumer>();
            },
            enableInboxFilter: true);

        await scope.GetDb<PayrollDbContext>().Database.EnsureCreatedAsync();
        return scope;
    }
}
