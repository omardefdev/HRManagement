using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Data;

namespace HRManagement.IntegrationTests;

/// <summary>G4: full sandbox payment cycle through coordinators (no treasury live).</summary>
[Collection("IntegrationSerial")]
public class G4SandboxPaymentCycleIntegrationTests
{
    [Fact]
    public async Task PostedRun_Process_Ack_Settle_ReconcilesOrders()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var empId = Guid.NewGuid();
        var profile = TestDataFactory.CreateActivePayProfile(empId, 5000m);
        db.EmployeePayProfiles.Add(profile);
        var config = PayrollRegimeSeed.CreateLaw50Configuration(isActive: true);
        config.ContentHash = PayrollConfigurationHashService.ComputeContentHash(config);
        config.EffectiveFrom = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        db.PayrollConfigurations.Add(config);

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 6, 1),
            PeriodEnd = new DateTime(2026, 6, 30),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Posted,
            TotalNetPay = 4500m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true
        };
        run.SalaryLines.Add(new SalaryLine
        {
            PayrollRunId = run.Id,
            EmployeeId = empId,
            GrossPay = 5000m,
            TotalDeductions = 500m,
            NetPay = 4500m
        });
        db.PayrollRuns.Add(run);

        var job = new PayrollRunJob
        {
            PayrollRunId = run.Id,
            Status = PayrollRunJobStatus.Completed,
            Phase = PayrollRunJobPhase.Validate,
            ResultsValidated = true,
            TotalEmployeeCount = 1,
            ProcessedEmployeeCount = 1
        };
        job.InputSnapshots.Add(new PayrollRunInputSnapshot
        {
            EmployeeId = empId,
            EmployeePayProfileId = profile.Id,
            ProfileVersion = "v1",
            InputFingerprint = "sandbox-fingerprint",
            SortOrder = 0
        });
        db.PayrollRunJobs.Add(job);

        var batch = new PaymentBatch
        {
            PayrollRunId = run.Id,
            TotalAmount = 4500m,
            Status = PaymentBatchStatus.Pending,
            BatchKind = PaymentBatchKind.Disbursement,
            BankTransferReference = "SANDBOX-EXPORT-001"
        };
        db.PaymentBatches.Add(batch);
        await db.SaveChangesAsync();

        const string treasuryAck = "TREASURY-SANDBOX-ACK-001";
        const string settlementRef = "BANK-SETTLE-001";

        var (submitted, submitErr) = await mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Guid.NewGuid()));
        Assert.True(submitted, submitErr);

        db.ChangeTracker.Clear();
        batch = await db.PaymentBatches.FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(PaymentBatchStatus.Submitted, batch.Status);
        Assert.NotNull(batch.SubmittedAt);

        var orders = await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id).ToListAsync();
        Assert.Single(orders);
        Assert.Equal(PaymentOrderStatus.Submitted, orders[0].Status);

        var (acked, ackErr) = await mediator.Send(new AckPaymentBatchCommand(batch.Id, treasuryAck));
        Assert.True(acked, ackErr);

        db.ChangeTracker.Clear();
        batch = await db.PaymentBatches.FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(PaymentBatchStatus.Acked, batch.Status);
        Assert.Equal(treasuryAck, batch.BankTransferReference);
        orders = await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id).ToListAsync();
        Assert.All(orders, o => Assert.Equal(PaymentOrderStatus.Acknowledged, o.Status));

        var (settled, settleErr) = await mediator.Send(new SettlePaymentBatchCommand(
            batch.Id, settlementRef, batch.TotalAmount));
        Assert.True(settled, settleErr);

        db.ChangeTracker.Clear();
        batch = await db.PaymentBatches.FirstAsync(b => b.Id == batch.Id);
        Assert.Equal(PaymentBatchStatus.Settled, batch.Status);
        Assert.Equal(settlementRef, batch.SettlementReference);

        orders = await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id).ToListAsync();
        Assert.All(orders, o => Assert.Equal(PaymentOrderStatus.Settled, o.Status));

        var matchedLines = await db.BankStatementLines.CountAsync();
        Assert.True(matchedLines >= 1);
    }

    private static async Task<(ServiceProvider Root, AsyncServiceScope Scope, PayrollDbContext Db, IMediator Mediator)> CreateScopeAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<PayrollDbContext>(o => o.UseSqlite(connection));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return (root, scope, db, mediator);
    }
}
