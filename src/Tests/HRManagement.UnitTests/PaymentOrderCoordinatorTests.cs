using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Services;

namespace HRManagement.UnitTests;

public class PaymentOrderCoordinatorTests
{
    [Fact]
    public async Task EnsureAndSubmitOrders_advances_draft_orders_to_submitted()
    {
        await using var db = CreateDb();
        var runId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var lineId = Guid.NewGuid();

        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = PayrollRunStatus.Posted
        });
        db.SalaryLines.Add(new SalaryLine
        {
            Id = lineId,
            PayrollRunId = runId,
            EmployeeId = Guid.NewGuid(),
            NetPay = 500m,
            GrossPay = 600m,
            TotalDeductions = 100m
        });
        db.PaymentBatches.Add(new PaymentBatch
        {
            Id = batchId,
            PayrollRunId = runId,
            TotalAmount = 500m,
            Status = PaymentBatchStatus.Submitted
        });
        await db.SaveChangesAsync();

        var orders = new PaymentOrderService(db);
        var coordinator = new PaymentOrderCoordinator(db, orders);
        await coordinator.EnsureAndSubmitOrdersAsync(batchId);

        var order = await db.PaymentOrders.SingleAsync();
        Assert.Equal(PaymentOrderStatus.Submitted, order.Status);
        Assert.NotNull(order.SubmittedAt);
    }

    [Fact]
    public async Task BankReconciliation_matches_settled_order_by_amount()
    {
        await using var db = CreateDb();
        var batchId = Guid.NewGuid();
        var order = new PaymentOrder
        {
            PaymentBatchId = batchId,
            PayrollRunId = Guid.NewGuid(),
            SalaryLineId = Guid.NewGuid(),
            EmployeeId = Guid.NewGuid(),
            Amount = 750m,
            Status = PaymentOrderStatus.Acknowledged,
            IdempotencyKey = "test:1"
        };
        db.PaymentOrders.Add(order);
        await db.SaveChangesAsync();

        var recon = new BankReconciliationService(db);
        await recon.ImportLineAsync("stmt-1", 750m, DateTime.UtcNow);
        var matched = await recon.MatchUnmatchedAsync();

        Assert.Equal(1, matched);
        Assert.Equal(PaymentOrderStatus.Settled, (await db.PaymentOrders.SingleAsync()).Status);
    }

    private static PayrollDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<PayrollDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new PayrollDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
