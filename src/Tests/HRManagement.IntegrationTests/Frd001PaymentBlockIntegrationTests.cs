using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.Fraud;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-10 AC-25: fraud hold blocks payment batch; four-eyes release allows pay.</summary>
[Collection("IntegrationSerial")]
public class Frd001PaymentBlockIntegrationTests
{
    [Fact]
    public async Task Same_day_hire_hold_blocks_batch_until_four_eyes_release()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var empId = Guid.NewGuid();
        var hireDate = new DateTime(2026, 9, 15, 0, 0, 0, DateTimeKind.Utc);
        var profile = TestDataFactory.CreateActivePayProfile(empId, 12_000m);
        profile.EffectiveFrom = hireDate;
        db.EmployeePayProfiles.Add(profile);
        // PLAN-55: the employee's verified account is its own entity, and the run pins it on its snapshot and its line.
        var account = Pay055TestAccounts.Active(empId);
        db.Add(account);

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 9, 1),
            PeriodEnd = new DateTime(2026, 9, 30),
            RunDate = hireDate,
            Status = PayrollRunStatus.Posted,
            TotalNetPay = 10_800m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true
        };
        run.SalaryLines.Add(new SalaryLine
        {
            EmployeeId = empId,
            GrossPay = 12_000m,
            TotalDeductions = 1_200m,
            NetPay = 10_800m,
            BankAccountId = account.Id
        });
        db.PayrollRuns.Add(run);

        var batch = new PaymentBatch
        {
            PayrollRunId = run.Id,
            TotalAmount = 10_800m,
            DisbursedAmount = 10_800m,
            Status = PaymentBatchStatus.Pending,
            BatchKind = PaymentBatchKind.Disbursement
        };
        db.PaymentBatches.Add(batch);

        var job = new PayrollRunJob
        {
            PayrollRunId = run.Id,
            Status = PayrollRunJobStatus.Completed,
            Phase = PayrollRunJobPhase.Validate,
            ResultsValidated = true
        };
        job.InputSnapshots.Add(new PayrollRunInputSnapshot
        {
            EmployeeId = empId,
            ProfileVersion = "v1",
            InputFingerprint = "test-fingerprint",
            BankAccountId = account.Id,
            SortOrder = 0
        });
        db.PayrollRunJobs.Add(job);
        await db.SaveChangesAsync();

        var scan = await mediator.Send(new RunPayrollFraudScanCommand(run.Id));
        Assert.True(scan.Success, scan.Error);
        Assert.True(scan.HoldsCreated >= 1);

        db.ChangeTracker.Clear();
        var hold = await db.PaymentHolds.FirstAsync(h =>
            h.EmployeeId == empId && h.Status == PaymentHoldStatus.Active);

        var (blocked, blockReason) = await mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Guid.NewGuid()));
        Assert.False(blocked);
        Assert.Contains("حجز", blockReason ?? "", StringComparison.Ordinal);

        // PLAN-58 (§3): the release is a request and a decision by two people. The requester cannot decide it himself.
        var dualControl = scope.ServiceProvider.GetRequiredService<Payroll.Application.Services.PayrollDualControlService>();
        var releaser = Guid.NewGuid();
        var asked = await dualControl.RequestAsync(Payroll.Domain.Enums.PayrollDualControlKind.HoldRelease, hold.Id,
            new Payroll.Application.Services.PayrollDualControlPayload("cleared after review"), releaser, CancellationToken.None);
        Assert.True(asked.Success, asked.Error);
        var solo = await dualControl.DecideAsync(asked.Id!.Value, true, null, releaser, _ => Task.FromResult(true), CancellationToken.None);
        Assert.False(solo.Success);
        Assert.Equal(Payroll.Application.Services.PayrollDualControlMessages.DeciderIsRequester, solo.Error);

        var approver = Guid.NewGuid();
        var released = await dualControl.DecideAsync(asked.Id!.Value, true, "ok", approver, _ => Task.FromResult(true), CancellationToken.None);
        Assert.True(released.Success, released.Error);

        db.ChangeTracker.Clear();
        hold = await db.PaymentHolds.FirstAsync(h => h.Id == hold.Id);
        Assert.Equal(PaymentHoldStatus.Released, hold.Status);

        var (paid, payErr) = await mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Guid.NewGuid()));
        Assert.True(paid, payErr);
    }

    private static async Task<(ServiceProvider Root, AsyncServiceScope Scope, Payroll.Infrastructure.Data.PayrollDbContext Db, IMediator Mediator)> CreateScopeAsync()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLogging();
        services.AddDbContext<Payroll.Infrastructure.Data.PayrollDbContext>(o => o.UseSqlite(connection));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);

        var root = services.BuildServiceProvider();
        var scope = root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Payroll.Infrastructure.Data.PayrollDbContext>();
        await db.Database.EnsureCreatedAsync();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
        return (root, scope, db, mediator);
    }
}
