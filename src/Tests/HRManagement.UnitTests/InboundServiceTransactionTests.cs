using HR.Contracts.Application.Features.InboundEvents;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Infrastructure.Data;
using HR.Contracts.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace HRManagement.UnitTests;

public class InboundServiceTransactionTests
{
    [Fact]
    public async Task ProcessLeaveRequested_creates_linked_service_transaction()
    {
        await using var db = CreateDb();
        var repo = new ContractsRepository(db);
        var handler = new ProcessLeaveRequestedHandler(repo);
        var requestId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await handler.Handle(new ProcessLeaveRequestedCommand(
            requestId,
            employeeId,
            LeaveType.Annual,
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 9, 5),
            5,
            "ESS leave"), CancellationToken.None);

        var leave = await db.LeaveRequests.FirstAsync(x => x.Id == requestId);
        Assert.NotNull(leave.ServiceTransactionId);
        var tx = await db.ServiceTransactions.FindAsync(leave.ServiceTransactionId);
        Assert.NotNull(tx);
        Assert.Equal(ServiceTransactionType.LeaveAdvance, tx!.TransactionType);
    }

    [Fact]
    public async Task ProcessAdvanceRequested_creates_linked_service_transaction()
    {
        await using var db = CreateDb();
        var repo = new ContractsRepository(db);
        var handler = new ProcessAdvanceRequestedHandler(repo);
        var requestId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await handler.Handle(new ProcessAdvanceRequestedCommand(
            requestId,
            employeeId,
            5000m,
            "ESS advance"), CancellationToken.None);

        var advance = await db.AdvanceRequests.FirstAsync(x => x.Id == requestId);
        Assert.NotNull(advance.ServiceTransactionId);
        var tx = await db.ServiceTransactions.FindAsync(advance.ServiceTransactionId);
        Assert.NotNull(tx);
        Assert.Equal(ServiceTransactionType.LeaveAdvance, tx!.TransactionType);
    }

    [Fact]
    public async Task ProcessJobChangeRequested_creates_service_transaction_with_server_assigned_issuer()
    {
        await using var db = CreateDb();
        var repo = new ContractsRepository(db);
        var handler = new ProcessJobChangeRequestedHandler(repo);
        var requestId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        await handler.Handle(new ProcessJobChangeRequestedCommand(
            requestId,
            employeeId,
            "محاسب",
            "رئيس محاسبين",
            new DateOnly(2026, 10, 1),
            "ESS promotion"), CancellationToken.None);

        var decision = await db.AdministrativeDecisions.FirstAsync(x => x.Id == requestId);
        Assert.Equal(HRManagement.ServiceDefaults.ServerAssignedIdentity.InboundSystemActor, decision.IssuedBy);

        var tx = await db.ServiceTransactions
            .FirstAsync(x => x.EmployeeId == employeeId && x.TransactionType == ServiceTransactionType.JobUpdate);
        Assert.Equal("Inbound ESS job change request", tx.Notes);
    }

    private static ContractsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<ContractsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ContractsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
