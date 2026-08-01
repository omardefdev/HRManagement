using HR.Contracts.Infrastructure.Consumers;
using HR.Contracts.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.UnitTests;

public class ContractsTransferExecutedConsumerTests
{
    private sealed class TransferExecutedMessage : ITransferExecuted
    {
        public Guid EmployeeId { get; init; }
        public Guid FromOrgUnitId { get; init; }
        public Guid ToOrgUnitId { get; init; }
        public DateTime ExecutedAt { get; init; } = DateTime.UtcNow;
        public Guid? TenantId { get; init; }
    }

    [Fact]
    public async Task TransferExecuted_Updates_EmployeeOrgUnitSnapshot()
    {
        var employeeId = Guid.NewGuid();
        var fromOrg = Guid.NewGuid();
        var toOrg = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase($"contracts-transfer-{employeeId}")
            .Options;

        await using var db = new ContractsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        db.EmployeeOrgUnitSnapshots.Add(new HRManagement.SharedKernel.Entities.EmployeeOrgUnitSnapshot
        {
            EmployeeId = employeeId,
            TenantId = Guid.NewGuid(),
            PrimaryOrgUnitId = fromOrg
        });
        await db.SaveChangesAsync();

        var consumer = new TransferExecutedConsumer(db, Mock.Of<ILogger<TransferExecutedConsumer>>());
        var message = new TransferExecutedMessage
        {
            EmployeeId = employeeId,
            FromOrgUnitId = fromOrg,
            ToOrgUnitId = toOrg
        };
        var context = Mock.Of<ConsumeContext<ITransferExecuted>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var snap = await db.EmployeeOrgUnitSnapshots.FindAsync(employeeId);
        Assert.NotNull(snap);
        Assert.Equal(toOrg, snap!.PrimaryOrgUnitId);
    }

    [Fact]
    public async Task TransferExecuted_Creates_Snapshot_When_Missing()
    {
        var employeeId = Guid.NewGuid();
        var toOrg = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase($"contracts-transfer-new-{employeeId}")
            .Options;

        await using var db = new ContractsDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var consumer = new TransferExecutedConsumer(db, Mock.Of<ILogger<TransferExecutedConsumer>>());
        var message = new TransferExecutedMessage
        {
            EmployeeId = employeeId,
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = toOrg,
            TenantId = Guid.NewGuid()
        };
        var context = Mock.Of<ConsumeContext<ITransferExecuted>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var snap = await db.EmployeeOrgUnitSnapshots.FindAsync(employeeId);
        Assert.NotNull(snap);
        Assert.Equal(toOrg, snap!.PrimaryOrgUnitId);
    }
}
