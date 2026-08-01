using HRManagement.MessageContracts.Events;
using MassTransit;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Payroll.Domain.Entities;
using Payroll.Infrastructure;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;

namespace HRManagement.UnitTests;

public class DisciplinaryActionConsumerTests
{
    private sealed class DisciplinaryMessage : IDisciplinaryActionTaken
    {
        public Guid EmployeeId { get; init; }
        public string ActionType { get; init; } = "";
        public DateTime IssuedAt { get; init; }
        public string? Notes { get; init; }
        public Guid? ComplaintId { get; init; }
        public Guid? CorrelationId { get; init; }
        public string SourceSystem { get; init; } = "Test";
        public int PenaltyDays { get; init; }
        public Guid ActionId { get; init; }
    }

    [Fact]
    public async Task Same_MessageId_Twice_Creates_Single_Adjustment()
    {
        var employeeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();
        var messageId = NewId.NextGuid();
        var dbName = $"payroll-disc-{actionId}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddPayrollInfrastructure();
        await using var sp = services.BuildServiceProvider();
        await using var scope = sp.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();

        db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = employeeId,
            BaseSalary = 30000,
            EffectiveFrom = DateTime.UtcNow.AddYears(-1),
            IsActive = true
        });
        await db.SaveChangesAsync();

        var consumer = new DisciplinaryActionTakenConsumer(
            scope.ServiceProvider.GetRequiredService<IMediator>(), db);

        var message = new DisciplinaryMessage
        {
            EmployeeId = employeeId,
            ActionType = "Suspension",
            ActionId = actionId,
            PenaltyDays = 2,
            Notes = "test",
            IssuedAt = DateTime.UtcNow
        };

        for (var i = 0; i < 2; i++)
        {
            var ctx = new Mock<ConsumeContext<IDisciplinaryActionTaken>>();
            ctx.SetupGet(c => c.Message).Returns(message);
            ctx.SetupGet(c => c.CancellationToken).Returns(CancellationToken.None);
            ctx.SetupGet(c => c.MessageId).Returns(messageId);
            await consumer.Consume(ctx.Object);
        }

        var adjustments = await db.PayrollAdjustments.Where(a => a.EmployeeId == employeeId).ToListAsync();
        Assert.Single(adjustments);
        Assert.Single(db.DeductionRecords.Where(d => d.DeductionType == "DisciplinaryDeduction"));
    }
}
