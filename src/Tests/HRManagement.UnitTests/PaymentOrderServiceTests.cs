using Microsoft.EntityFrameworkCore;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Services;

namespace HRManagement.UnitTests;

public class PaymentOrderServiceTests
{
    [Fact]
    public async Task EnsureOrdersForBatch_is_idempotent_per_salary_line()
    {
        await using var db = CreateDb();
        var runId = Guid.NewGuid();
        var batchId = Guid.NewGuid();
        var lineId = Guid.NewGuid();
        var employeeId = Guid.NewGuid();

        db.PayrollRuns.Add(new Payroll.Domain.Entities.PayrollRun
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
            EmployeeId = employeeId,
            NetPay = 1000m,
            GrossPay = 1200m,
            TotalDeductions = 200m
        });
        db.PaymentBatches.Add(new PaymentBatch
        {
            Id = batchId,
            PayrollRunId = runId,
            TotalAmount = 1000m,
            Status = PaymentBatchStatus.Pending
        });
        await db.SaveChangesAsync();

        var service = new PaymentOrderService(db);
        var first = await service.EnsureOrdersForBatchAsync(batchId);
        var second = await service.EnsureOrdersForBatchAsync(batchId);

        Assert.Single(first);
        Assert.Empty(second);
        Assert.Equal(1, await db.PaymentOrders.CountAsync());
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
