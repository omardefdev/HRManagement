using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Features.Fraud;
using Payroll.Application.Features.PartialDisbursement;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>PLAN-10 AC-24: partial disbursement orchestrator creates batch, arrears, and orders.</summary>
[Collection("IntegrationSerial")]
public class PartialDisbursementOrchestratorTests
{
    private static readonly Guid RuleSetId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public async Task Partial_disbursement_creates_batch_arrears_orders_with_invariant()
    {
        var (root, scope, db, mediator) = await CreateScopeAsync();
        await using var _ = scope;
        await using var __ = root;

        var emp1 = Guid.NewGuid();
        var emp2 = Guid.NewGuid();
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(emp1, 100_000m));
        db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(emp2, 80_000m));
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
        run.SalaryLines.Add(new SalaryLine
        {
            EmployeeId = emp1,
            GrossPay = 100_000m,
            TotalDeductions = 10_000m,
            NetPay = 90_000m
        });
        run.SalaryLines.Add(new SalaryLine
        {
            EmployeeId = emp2,
            GrossPay = 80_000m,
            TotalDeductions = 8_000m,
            NetPay = 72_000m
        });
        db.PayrollRuns.Add(run);

        var decision = new FundingDecision
        {
            PayrollRunId = run.Id,
            RequestedAmount = 162_000m,
            ApprovedAmount = 97_200m,
            FundingRatio = 0.60m,
            PriorityRuleSetId = RuleSetId,
            ApprovedBy = Guid.NewGuid()
        };
        db.FundingDecisions.Add(decision);
        await db.SaveChangesAsync();

        var result = await mediator.Send(new ExecutePartialDisbursementCommand(run.Id, decision.Id));
        Assert.True(result.Success, result.Error);
        Assert.NotNull(result.BatchId);
        Assert.True(result.InvariantOk);
        Assert.Equal(97_200m, result.TotalDisbursed);
        Assert.Equal(64_800m, result.TotalArrears);
        Assert.Equal(2, result.Orders.Count);
        Assert.All(result.Orders, o => Assert.True(o.Amount > 0));

        db.ChangeTracker.Clear();
        var batch = await db.PaymentBatches.FirstAsync(b => b.Id == result.BatchId);
        Assert.True(batch.IsPartial);
        Assert.Equal(decision.Id, batch.FundingDecisionId);

        var orders = await db.PaymentOrders.Where(o => o.PaymentBatchId == batch.Id).ToListAsync();
        Assert.Equal(2, orders.Count);
        Assert.Equal(result.TotalDisbursed, orders.Sum(o => o.Amount));

        var replay = await mediator.Send(new ExecutePartialDisbursementCommand(run.Id, decision.Id));
        Assert.True(replay.Success);
        Assert.True(replay.WasIdempotentReplay);
        Assert.Equal(result.BatchId, replay.BatchId);

        var orderCount = await db.PaymentOrders.CountAsync(o => o.PaymentBatchId == batch.Id);
        Assert.Equal(2, orderCount);
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
