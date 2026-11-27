using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-10 AC-24, moved to PLAN-58's path (D-PAY-2): a partial disbursement is a funding decision on the posting's <c>Pending</c> batch,
/// then <c>process</c> by another person. The meaning is kept: 60 % is paid in priority order, the rest is arrears, the orders and the
/// arrears add up to the run's net, and asking again makes nothing twice (a second decision 409, a second sending 409).
/// </summary>
[Collection("IntegrationSerial")]
public class PartialDisbursementOrchestratorTests
{
    private static readonly Guid RuleSetId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task A_funding_decision_spreads_the_posted_orders_records_arrears_and_process_sends_the_funded_part()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(emp1, 100_000m));
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(emp2, 80_000m));
        var (account1, account2) = (Guid.NewGuid(), Guid.NewGuid());
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = account1, EmployeeId = emp1, AccountNumber = "11001", Status = BankAccountStatus.Active });
        db.EmployeeBankAccounts.Add(new EmployeeBankAccount { Id = account2, EmployeeId = emp2, AccountNumber = "11002", Status = BankAccountStatus.Active });
        SeedRules(db);

        var run = new PayrollRun
        {
            PeriodStart = new DateTime(2026, 8, 1),
            PeriodEnd = new DateTime(2026, 8, 31),
            RunDate = DateTime.UtcNow,
            Status = PayrollRunStatus.Posted,
            TotalNetPay = 162_000m,
            ValidatedAtCalculate = true,
            ResultsFrozen = true
        };
        run.SalaryLines.Add(new SalaryLine { EmployeeId = emp1, GrossPay = 100_000m, TotalDeductions = 10_000m, NetPay = 90_000m, BankAccountId = account1 });
        run.SalaryLines.Add(new SalaryLine { EmployeeId = emp2, GrossPay = 80_000m, TotalDeductions = 8_000m, NetPay = 72_000m, BankAccountId = account2 });
        db.PayrollRuns.Add(run);
        // The run was calculated: its job holds a snapshot per employee (the fraud scan of the sending reads them).
        var job = new PayrollRunJob { PayrollRunId = run.Id, Status = PayrollRunJobStatus.Completed, Phase = PayrollRunJobPhase.Validate, ResultsValidated = true };
        job.InputSnapshots.Add(new PayrollRunInputSnapshot { EmployeeId = emp1, ProfileVersion = "v1", InputFingerprint = "f1", BankAccountId = account1, SortOrder = 0 });
        job.InputSnapshots.Add(new PayrollRunInputSnapshot { EmployeeId = emp2, ProfileVersion = "v1", InputFingerprint = "f2", BankAccountId = account2, SortOrder = 1 });
        db.PayrollRunJobs.Add(job);
        await db.SaveChangesAsync();
        var batch = await scope.ServiceProvider.GetRequiredService<PaymentBatchIssuer>()
            .IssueAsync(run, run.SalaryLines.ToList(), PaymentBatchSource.PayrollRunPosted, CancellationToken.None);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var decider = Guid.NewGuid();
        var decided = await mediator.Send(new CreateFundingDecisionCommand(batch.Id, 97_200m, 0.60m, RuleSetId, decider));
        Assert.True(decided.Success, decided.Error);

        db.ChangeTracker.Clear();
        var funded = await db.PaymentBatches.FirstAsync(b => b.Id == batch.Id);
        Assert.True(funded.IsPartial);
        Assert.Equal(decided.Id, funded.FundingDecisionId);
        Assert.Equal(64_800m, funded.ArrearsAmount);
        var orders = await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal(97_200m, orders.Where(o => o.Status != PaymentOrderStatus.Cancelled).Sum(o => o.Amount));
        var arrears = await db.ArrearsLedgerEntries.Where(a => a.PayrollRunId == run.Id).ToListAsync();
        Assert.Equal(64_800m, arrears.Sum(a => a.ArrearsAmount));
        Assert.Equal(162_000m, orders.Where(o => o.Status != PaymentOrderStatus.Cancelled).Sum(o => o.Amount) + arrears.Sum(a => a.ArrearsAmount));

        var again = await mediator.Send(new CreateFundingDecisionCommand(batch.Id, 97_200m, 0.60m, RuleSetId, decider));
        Assert.Equal(RunActOutcome.Conflict, again.Outcome);

        // The decider does not send; another person does, and the funded part leaves.
        Assert.Equal(PaymentBatchMessages.ExecutorIsFundingDecider, (await mediator.Send(new ProcessPaymentBatchCommand(batch.Id, decider))).Error);
        var sent = await mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Guid.NewGuid()));
        Assert.True(sent.Success, sent.Error);
        db.ChangeTracker.Clear();
        Assert.Equal(97_200m, await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id && o.Status == PaymentOrderStatus.Submitted).SumAsync(o => o.Amount));
        Assert.Equal(RunActOutcome.Conflict, (await mediator.Send(new ProcessPaymentBatchCommand(batch.Id, Guid.NewGuid()))).Outcome);
    }

    private static void SeedRules(Payroll.Infrastructure.Data.PayrollDbContext db)
    {
        db.DisbursementPriorityRules.AddRange(
            new DisbursementPriorityRule
            {
                RuleSetId = RuleSetId,
                Category = "RegularPayroll",
                Priority = 1,
                MinimumAmount = 0,
                LegalSource = "Treasury",
                IsActive = true
            });
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
