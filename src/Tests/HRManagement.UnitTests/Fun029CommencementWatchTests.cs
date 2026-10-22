using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Services;
using HRManagement.SharedKernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using static HRManagement.UnitTests.Fun029TestSupport;

namespace HRManagement.UnitTests;

/// <summary>FUN-029 (J): overdue commencement is flagged once and opens an intervention (detection only).</summary>
public class Fun029CommencementWatchTests
{
    private static readonly DateOnly Today = new(2027, 2, 15);

    private static async Task<ServiceTransaction> SeedAppointmentAsync(
        ContractsDbContext db,
        DateOnly? expectedStart,
        ServiceTransactionState state = ServiceTransactionState.InProcess)
    {
        var txn = new ServiceTransaction
        {
            EmployeeId = Guid.NewGuid(),
            TransactionType = ServiceTransactionType.Appointment,
            ExpectedStartDate = expectedStart
        };
        txn.MoveTo(ServiceTransactionState.Submitted, null);
        txn.MoveTo(ServiceTransactionState.UnderReview, null);
        if (state == ServiceTransactionState.Cancelled)
            txn.MoveTo(ServiceTransactionState.Cancelled, null);
        else
            txn.MoveTo(ServiceTransactionState.InProcess, null);
        db.ServiceTransactions.Add(txn);
        await db.SaveChangesAsync();
        return txn;
    }

    private static HireCommencementWatcher Watcher(ContractsDbContext db, IEventPublisher publisher, int deadlineDays = 30) =>
        new(db, publisher,
            Microsoft.Extensions.Options.Options.Create(new HireCommencementOptions { CommencementDeadlineDays = deadlineDays }),
            NullLogger<HireCommencementWatcher>.Instance);

    [Fact]
    public async Task Overdue_appointment_is_flagged_once_with_an_intervention()
    {
        await using var db = CreateDb();
        var overdue = await SeedAppointmentAsync(db, Today.AddDays(-31));
        var publisher = new Mock<IEventPublisher>();

        var first = await Watcher(db, publisher.Object).FlagOverdueAsync(Today);
        var second = await Watcher(db, publisher.Object).FlagOverdueAsync(Today.AddDays(5));

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.NotNull((await db.ServiceTransactions.SingleAsync(t => t.Id == overdue.Id)).CommencementOverdueAt);
        Assert.Equal("تأخر المباشرة عن المهلة", HireContractMessages.CommencementOverdue);
        publisher.Verify(p => p.PublishProcessInterventionRequestedAsync(
            SagaTypeNames.Hire, overdue.Id, HireContractMessages.CommencementOverdue, "HR.Contracts", It.IsAny<Guid?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Commenced_employee_is_ignored()
    {
        await using var db = CreateDb();
        var txn = await SeedAppointmentAsync(db, Today.AddDays(-60));
        db.CommencementRecords.Add(new CommencementRecord { EmployeeId = txn.EmployeeId, StartDate = Today.AddDays(-55) });
        await db.SaveChangesAsync();
        var publisher = new Mock<IEventPublisher>();

        Assert.Equal(0, await Watcher(db, publisher.Object).FlagOverdueAsync(Today));
        Assert.Null((await db.ServiceTransactions.SingleAsync(t => t.Id == txn.Id)).CommencementOverdueAt);
        publisher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Start_within_the_deadline_or_without_a_date_is_ignored()
    {
        await using var db = CreateDb();
        var future = await SeedAppointmentAsync(db, Today.AddDays(10));
        var onDeadline = await SeedAppointmentAsync(db, Today.AddDays(-30));
        var noDate = await SeedAppointmentAsync(db, null);
        var cancelled = await SeedAppointmentAsync(db, Today.AddDays(-90), ServiceTransactionState.Cancelled);

        Assert.Equal(0, await Watcher(db, Mock.Of<IEventPublisher>()).FlagOverdueAsync(Today));
        Assert.All(
            await db.ServiceTransactions.Where(t => new[] { future.Id, onDeadline.Id, noDate.Id, cancelled.Id }.Contains(t.Id)).ToListAsync(),
            t => Assert.Null(t.CommencementOverdueAt));
    }

    [Fact]
    public async Task Deadline_is_configurable()
    {
        await using var db = CreateDb();
        await SeedAppointmentAsync(db, Today.AddDays(-8));

        Assert.Equal(0, await Watcher(db, Mock.Of<IEventPublisher>(), deadlineDays: 30).FlagOverdueAsync(Today));
        Assert.Equal(1, await Watcher(db, Mock.Of<IEventPublisher>(), deadlineDays: 7).FlagOverdueAsync(Today));
    }
}
