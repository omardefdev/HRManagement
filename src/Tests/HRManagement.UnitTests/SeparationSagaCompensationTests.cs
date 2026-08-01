using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Sagas;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace HRManagement.UnitTests;

public class SeparationSagaCompensationTests
{
    [Fact]
    public async Task SeparationSagaFinalized_failure_does_not_archive_transaction()
    {
        var tenantId = Guid.NewGuid();
        await using var db = CreateDb(tenantId);
        var employeeId = Guid.NewGuid();
        var record = new SeparationRecord
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            SeparationType = SeparationType.Resignation,
            EffectiveDate = new DateOnly(2026, 8, 1),
            SeparationEventPublished = false
        };
        db.SeparationRecords.Add(record);
        var txn = new ServiceTransaction
        {
            TenantId = tenantId,
            EmployeeId = employeeId,
            TransactionType = ServiceTransactionType.Separation
        };
        txn.MoveTo(ServiceTransactionState.Submitted, null);
        txn.MoveTo(ServiceTransactionState.UnderReview, null);
        txn.MoveTo(ServiceTransactionState.InProcess, null);
        txn.MoveTo(ServiceTransactionState.PendingApproval, null);
        txn.MoveTo(ServiceTransactionState.Approved, null);
        txn.MoveTo(ServiceTransactionState.Executing, null);
        db.ServiceTransactions.Add(txn);
        await db.SaveChangesAsync();

        var message = Mock.Of<ISeparationSagaFinalized>(m =>
            m.SeparationRecordId == record.Id && m.Success == false);
        var context = Mock.Of<ConsumeContext<ISeparationSagaFinalized>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await new SeparationSagaFinalizedConsumer(db).Consume(context);

        var refreshed = await db.ServiceTransactions.SingleAsync();
        Assert.Equal(ServiceTransactionState.Executing, refreshed.State);
        Assert.False((await db.SeparationRecords.SingleAsync()).SeparationEventPublished);
    }

    private static ContractsDbContext CreateDb(Guid tenantId)
    {
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new ContractsDbContext(options, new FixedTenantContext(tenantId));
    }

    private sealed class FixedTenantContext(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId { get; } = tenantId;
        public bool IsPlatformOperator => false;
        public Guid RequireTenantId() => tenantId;
    }
}
