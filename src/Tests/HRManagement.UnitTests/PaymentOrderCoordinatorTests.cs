using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Services;

namespace HRManagement.UnitTests;

public class PaymentOrderCoordinatorTests
{
    /// <summary>
    /// PLAN-58: the one sending. A batch older than PLAN-58 gets its orders by the issuer's rule; an employee the gate holds keeps his order
    /// held with the gate's reason, a line with no pinned account is held NO-ACCOUNT, and the others are submitted. Nothing is saved by the
    /// coordinator (the handler saves once).
    /// </summary>
    [Fact]
    public async Task EnsureAndSubmitOrders_holds_the_held_and_the_accountless_and_submits_the_others()
    {
        await using var db = CreateDb();
        var runId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var (paid, held, noAccount) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (paidAccount, heldAccount) = (Guid.NewGuid(), Guid.NewGuid());

        db.PayrollRuns.Add(new PayrollRun
        {
            Id = runId,
            PeriodStart = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc),
            Status = PayrollRunStatus.Posted
        });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = paidAccount, EmployeeId = paid, AccountNumber = "1001", Status = BankAccountStatus.Active });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = heldAccount, EmployeeId = held, AccountNumber = "1002", Status = BankAccountStatus.Active });
        db.SalaryLines.Add(new SalaryLine { PayrollRunId = runId, EmployeeId = paid, BankAccountId = paidAccount, NetPay = 500m, GrossPay = 600m, TotalDeductions = 100m });
        db.SalaryLines.Add(new SalaryLine { PayrollRunId = runId, EmployeeId = held, BankAccountId = heldAccount, NetPay = 400m, GrossPay = 400m });
        db.SalaryLines.Add(new SalaryLine { PayrollRunId = runId, EmployeeId = noAccount, NetPay = 300m, GrossPay = 300m });
        db.PaymentBatches.Add(new PaymentBatch
        {
            Id = batchId,
            PayrollRunId = runId,
            TotalAmount = 1200m,
            Status = PaymentBatchStatus.Pending
        });
        await db.SaveChangesAsync();

        var batch = await db.PaymentBatches.SingleAsync();
        var coordinator = new PaymentOrderCoordinator(db);
        var gate = Payroll.Application.Abstractions.DisbursementGateResult.HoldOnly(
            [new Payroll.Application.Abstractions.HeldEmployee(held, PaymentOrderHoldReasons.PaymentHold, Guid.NewGuid())]);
        var sent = await coordinator.EnsureAndSubmitOrdersAsync(batch, gate, Payroll.Application.Abstractions.PaymentSending.Initial);

        var order = Assert.Single(sent);
        Assert.Equal(paid, order.EmployeeId);
        Assert.Equal(PaymentOrderStatus.Submitted, order.Status);
        Assert.NotNull(order.SubmittedAt);
        await db.SaveChangesAsync();
        var orders = await db.PaymentOrders.AsNoTracking().ToListAsync();
        Assert.Equal(3, orders.Count);
        Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.PaymentHold), orders.Where(o => o.EmployeeId == held).Select(o => (o.Status, o.HoldReason)).Single());
        Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.NoAccount), orders.Where(o => o.EmployeeId == noAccount).Select(o => (o.Status, o.HoldReason)).Single());
        Assert.Null(orders.Single(o => o.EmployeeId == noAccount).BeneficiaryIban);
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
