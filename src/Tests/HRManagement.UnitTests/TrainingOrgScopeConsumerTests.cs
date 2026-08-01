using HR.Training.Infrastructure.Consumers;
using HR.Training.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace HRManagement.UnitTests;

public class TrainingOrgScopeConsumerTests
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
    public async Task Training_TransferExecuted_Updates_EmployeeOrgUnitSnapshot()
    {
        var employeeId = Guid.NewGuid();
        var toOrg = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<TrainingDbContext>()
            .UseInMemoryDatabase($"training-transfer-{employeeId}")
            .Options;

        await using var db = new TrainingDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var consumer = new TransferExecutedConsumer(db, Mock.Of<ILogger<TransferExecutedConsumer>>());
        var message = new TransferExecutedMessage
        {
            EmployeeId = employeeId,
            FromOrgUnitId = Guid.NewGuid(),
            ToOrgUnitId = toOrg
        };
        var context = Mock.Of<ConsumeContext<ITransferExecuted>>(c =>
            c.Message == message && c.CancellationToken == CancellationToken.None);

        await consumer.Consume(context);

        var snap = await db.EmployeeOrgUnitSnapshots.FindAsync(employeeId);
        Assert.NotNull(snap);
        Assert.Equal(toOrg, snap!.PrimaryOrgUnitId);
    }
}
