using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Legal;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport.Integration;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-58 (P7) on PostgreSQL, as the non-superuser application role under forced row security, with the tenant wiring of Payroll.Web:
/// (1) two concurrent <c>process</c> calls of one batch: one sends, the other is a 409, and there is one bank file; (2) two concurrent
/// decisions of one four-eyes request: one decides, the other is a 409, and the act happens once; (3) a failure injected in the posting's
/// save after its orders leaves no batch, no order and the run approved; (4) one employee blocked by HR.Core is calculated and his order
/// held <c>REGIME-BLOCKER</c> by name while the others are sent, and his release is refused until HR.Core answers «not blocked»;
/// (5) P-11: two runs of two months calculating at once over one free deduction — one takes it, the other's calculation stops.
/// </summary>
[Collection("PostgreSql")]
public sealed class Pay058DisbursementPostgreSqlTests(PostgreSqlFixture fixture, ITestOutputHelper output)
{
    private static readonly Guid Agency = Guid.Parse("58000000-0000-4000-8000-00000000000a");
    private static readonly Guid Approver = Pay056RunScope.Approver;
    private static readonly Guid SecondApprover = Pay056RunScope.OtherApprover;
    private static readonly Guid Poster = Pay056RunScope.Poster;

    /// <summary>Holds every save that matches <see cref="When"/> until <see cref="Participants"/> units of work have reached theirs.</summary>
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _waiting;
        private TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Participants { get; private set; }

        public Func<DbContext, bool> When { get; private set; } = _ => false;

        public void Arm(int participants, Func<DbContext, bool> when)
        {
            _waiting = 0;
            _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            When = when;
            Participants = participants;
        }

        public void Disarm() => Participants = 0;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Participants > 0 && When(eventData.Context!))
            {
                if (Interlocked.Increment(ref _waiting) >= Participants)
                    _released.TrySetResult();
                await _released.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    /// <summary>Fails the save that writes payment orders: the posting's batch and orders are in the change tracker by then.</summary>
    private sealed class FailingOrderSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed && eventData.Context!.ChangeTracker.Entries<PaymentOrder>().Any(e => e.State == EntityState.Added)
                ? throw new InvalidOperationException("planted failure after the posting's orders")
                : ValueTask.FromResult(result);
    }

    private sealed record Host(ServiceProvider Provider, string Admin, SaveBarrier Barrier, FailingOrderSave Failure, FakeHrCoreServiceRegimeClient HrCore)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Provider.DisposeAsync();

        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }

        public Task<T> SendAsync<T>(IRequest<T> command) => InScopeAsync(sp => sp.GetRequiredService<IMediator>().Send(command));

        public Task<T> DbAsync<T>(Func<PayrollDbContext, Task<T>> read) => InScopeAsync(sp => read(sp.GetRequiredService<PayrollDbContext>()));

        public Task<T> DualControlAsync<T>(Func<PayrollDualControlService, Task<T>> work) =>
            InScopeAsync(sp => work(sp.GetRequiredService<PayrollDualControlService>()));
    }

    private async Task<Host> StartAsync()
    {
        var name = TestDatabaseNames.Unique("pay058_");
        var admin = await fixture.CreateDatabaseAsync(name);
        await using (var migrate = new PayrollDbContext(new DbContextOptionsBuilder<PayrollDbContext>()
                         .UseNpgsql(admin, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name)).Options))
            await migrate.Database.MigrateAsync();
        var app = await fixture.CreateAppConnectionAsync(name);

        var tenant = new PayrollPostgresTestSupport.FixedTenant(Agency);
        var barrier = new SaveBarrier();
        var failure = new FailingOrderSave();
        var hrCore = new FakeHrCoreServiceRegimeClient { ReasonCode = "REGIME_UNCLASSIFIED" };
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITenantContext>(tenant);
        services.AddDbContext<PayrollDbContext>(o => o
            .UseNpgsql(app, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .AddInterceptors(new TenantIdStampInterceptor(tenant), new NpgsqlTenantSessionInterceptor(tenant), barrier, failure));
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
        services.AddSingleton<RecordedPayrollEvents>();
        services.AddSingleton<Payroll.Application.Abstractions.IPayrollEventPublisher>(sp => sp.GetRequiredService<RecordedPayrollEvents>());
        services.AddSingleton<IHrCoreServiceRegimeClient>(hrCore);
        services.PostConfigure<PayrollBatchOptions>(o => o.MaxDegreeOfParallelism = 1);

        var host = new Host(services.BuildServiceProvider(), admin, barrier, failure, hrCore);
        await host.DbAsync(async db =>
        {
            await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
            return 0;
        });
        Assert.False(await Pol047Pg.ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        return host;
    }

    private static async Task<List<Guid>> EmployeesAsync(Host host, int count)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        await host.DbAsync(async db =>
        {
            foreach (var id in ids)
            {
                db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(id, 900_000m));
                db.EmployeeBankAccounts.Add(Pay055TestAccounts.Active(id, Agency));
            }

            return await db.SaveChangesAsync();
        });
        return ids;
    }

    private static async Task<Guid> CreateRunAsync(Host host, int month)
    {
        var start = new DateTime(2028, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var (created, id, error) = await host.SendAsync(new CreatePayrollRunCommand(start, start.AddMonths(1).AddDays(-1), start.AddMonths(1)));
        Assert.True(created, error);
        return id!.Value;
    }

    private static async Task<Guid> ApprovedRunAsync(Host host, int month)
    {
        var runId = await CreateRunAsync(host, month);
        var calculated = await host.SendAsync(new CalculatePayrollRunCommand(runId));
        Assert.True(calculated.Success, calculated.Error);
        var submitted = await host.SendAsync(new SubmitPayrollRunForReviewCommand(runId));
        Assert.True(submitted.Success, submitted.Error);
        var approved = await host.SendAsync(new ApprovePayrollRunCommand(runId, Approver, "payroll-approver", null));
        Assert.True(approved.Success, approved.Error);
        await host.DbAsync(async db =>
        {
            // The closed review a posting needs (its two auditors are proved in Pay058ApprovalReviewTests).
            db.PayrollAuditRecords.Add(new PayrollAuditRecord
            {
                PayrollRunId = runId,
                AuditorId = Guid.NewGuid(),
                ClosedBy = Guid.NewGuid(),
                ClosedAt = DateTime.UtcNow,
                Status = AuditStatus.Closed,
                Findings = "لا ملاحظات",
            });
            return await db.SaveChangesAsync();
        });
        return runId;
    }

    private static async Task<Guid> PostedBatchAsync(Host host, int month)
    {
        var runId = await ApprovedRunAsync(host, month);
        var (posted, error) = await host.SendAsync(new PostPayrollRunCommand(runId, Poster));
        Assert.True(posted, error);
        return await host.DbAsync(db => db.PaymentBatches.AsNoTracking().Where(b => b.PayrollRunId == runId).Select(b => b.Id).SingleAsync());
    }

    private static Task<Dictionary<Guid, PaymentOrder>> OrdersAsync(Host host, Guid batchId) =>
        host.DbAsync(db => db.PaymentOrders.AsNoTracking().Where(o => o.PaymentBatchId == batchId).ToDictionaryAsync(o => o.EmployeeId));

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_concurrent_sendings_of_one_batch_send_it_once_with_one_file()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeesAsync(host, 3);
        var batchId = await PostedBatchAsync(host, 1);

        // Both requests read the batch Pending and reach their save before either writes.
        host.Barrier.Arm(2, db => db.ChangeTracker.Entries<PaymentBatch>().Any(e => e.State == EntityState.Modified));
        var results = await Task.WhenAll(
            host.SendAsync(new ProcessPaymentBatchCommand(batchId, Poster)),
            host.SendAsync(new ProcessPaymentBatchCommand(batchId, Poster)));
        host.Barrier.Disarm();

        Assert.Single(results, r => r.Success);
        var lost = Assert.Single(results, r => !r.Success);
        Assert.Equal(RunActOutcome.Conflict, lost.Outcome);
        var files = await host.DbAsync(db => db.PaymentBatchFiles.AsNoTracking().Where(f => f.PaymentBatchId == batchId).ToListAsync());
        var file = Assert.Single(files);
        Assert.Equal((1, PaymentBatchFileKind.Initial, 3), (file.Sequence, file.Kind, file.ControlCount));
        Assert.All((await OrdersAsync(host, batchId)).Values, o => Assert.Equal((PaymentOrderStatus.Submitted, (Guid?)file.Id), (o.Status, o.PaymentBatchFileId)));
        output.WriteLine($"concurrent process: one sent, the other {lost.Outcome} «{lost.Error}»; one file.");
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_concurrent_decisions_of_one_request_decide_it_once()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var employee = (await EmployeesAsync(host, 1))[0];
        var holdId = await host.DbAsync(async db =>
        {
            var hold = new PaymentHold { EmployeeId = employee, HoldType = PaymentHoldType.Manual, Reason = "مراجعة", Status = PaymentHoldStatus.Active };
            db.PaymentHolds.Add(hold);
            await db.SaveChangesAsync();
            return hold.Id;
        });
        var asked = await host.DualControlAsync(s => s.RequestAsync(
            PayrollDualControlKind.HoldRelease, holdId, new PayrollDualControlPayload("تحقق من الأدلة"), Approver, default));
        Assert.True(asked.Success, asked.Error);

        host.Barrier.Arm(2, db => db.ChangeTracker.Entries<PayrollDualControlRequest>().Any(e => e.State == EntityState.Modified));
        var decisions = await Task.WhenAll(
            host.DualControlAsync(s => s.DecideAsync(asked.Id!.Value, true, "موافق", SecondApprover, _ => Task.FromResult(true), default)),
            host.DualControlAsync(s => s.DecideAsync(asked.Id!.Value, true, "موافق", Poster, _ => Task.FromResult(true), default)));
        host.Barrier.Disarm();

        Assert.Single(decisions, d => d.Success);
        Assert.Equal(RunActOutcome.Conflict, Assert.Single(decisions, d => !d.Success).Outcome);
        var request = await host.DbAsync(db => db.PayrollDualControlRequests.AsNoTracking().SingleAsync(r => r.Id == asked.Id));
        var hold = await host.DbAsync(db => db.PaymentHolds.AsNoTracking().SingleAsync(h => h.Id == holdId));
        Assert.Equal(PayrollDualControlStatus.Approved, request.Status);
        Assert.Equal((PaymentHoldStatus.Released, (Guid?)Approver, request.DecidedBy), (hold.Status, hold.ReleasedBy, hold.ReleaseApprovedBy));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_failure_after_the_postings_orders_leaves_no_batch_no_order_and_the_run_approved()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeesAsync(host, 2);
        var runId = await ApprovedRunAsync(host, 1);

        host.Failure.Armed = true;
        string? refusal = null;
        try
        {
            var (posted, error) = await host.SendAsync(new PostPayrollRunCommand(runId, Poster));
            Assert.False(posted);
            refusal = error;
        }
        catch (InvalidOperationException ex)
        {
            refusal = ex.Message;
        }
        finally
        {
            host.Failure.Armed = false;
        }

        output.WriteLine($"planted failure: {refusal}");
        var run = await host.DbAsync(db => db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId));
        Assert.Equal((PayrollRunStatus.Approved, (Guid?)null), (run.Status, run.PostedBy));
        Assert.Equal(0, await host.DbAsync(db => db.PaymentBatches.CountAsync(b => b.PayrollRunId == runId)));
        Assert.Equal(0, await host.DbAsync(db => db.PaymentOrders.CountAsync(o => o.PayrollRunId == runId)));

        // The same posting, without the failure, makes one batch with its orders.
        var (again, againError) = await host.SendAsync(new PostPayrollRunCommand(runId, Poster));
        Assert.True(again, againError);
        Assert.Equal(1, await host.DbAsync(db => db.PaymentBatches.CountAsync(b => b.PayrollRunId == runId)));
        Assert.Equal(2, await host.DbAsync(db => db.PaymentOrders.CountAsync(o => o.PayrollRunId == runId)));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_blocked_employee_is_calculated_his_order_held_by_name_and_released_only_once_HR_Core_lifts_the_blocker()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var employees = await EmployeesAsync(host, 3);
        var blocked = employees[0];
        host.HrCore.BlockedEmployeeIds.Add(blocked);

        var batchId = await PostedBatchAsync(host, 1);
        var runId = await host.DbAsync(db => db.PaymentBatches.AsNoTracking().Where(b => b.Id == batchId).Select(b => b.PayrollRunId).SingleAsync());
        Assert.Equal(3, await host.DbAsync(db => db.SalaryLines.CountAsync(l => l.PayrollRunId == runId)));
        var sent = await host.SendAsync(new ProcessPaymentBatchCommand(batchId, Poster));
        Assert.True(sent.Success, sent.Error);

        var orders = await OrdersAsync(host, batchId);
        Assert.Equal((PaymentOrderStatus.Held, PaymentOrderHoldReasons.RegimeBlocker), (orders[blocked].Status, orders[blocked].HoldReason));
        Assert.All(employees.Skip(1), e => Assert.Equal(PaymentOrderStatus.Submitted, orders[e].Status));
        var hold = await host.DbAsync(db => db.PaymentHolds.AsNoTracking().SingleAsync(h => h.EmployeeId == blocked && h.HoldType == PaymentHoldType.RegimeBlocker));
        Assert.Contains("REGIME_UNCLASSIFIED", hold.Reason, StringComparison.Ordinal);

        // Two people agree to release him, but HR.Core still reports the blocker: refused, nothing changes.
        var asked = await host.DualControlAsync(s => s.RequestAsync(
            PayrollDualControlKind.HoldRelease, hold.Id, new PayrollDualControlPayload("صُنّف نظام خدمته"), Approver, default));
        Assert.True(asked.Success, asked.Error);
        var early = await host.DualControlAsync(s => s.DecideAsync(asked.Id!.Value, true, "موافق", SecondApprover, _ => Task.FromResult(true), default));
        Assert.Equal((RunActOutcome.Invalid, PayrollDualControlMessages.RegimeStillBlocked), (early.Outcome, early.Error));
        Assert.Equal(PaymentHoldStatus.Active, (await host.DbAsync(db => db.PaymentHolds.AsNoTracking().SingleAsync(h => h.Id == hold.Id))).Status);

        // HR.Core lifts it: the same request is decided, and the supplementary file sends him alone.
        host.HrCore.BlockedEmployeeIds.Clear();
        var decided = await host.DualControlAsync(s => s.DecideAsync(asked.Id!.Value, true, "موافق", SecondApprover, _ => Task.FromResult(true), default));
        Assert.True(decided.Success, decided.Error);
        var supplementary = await host.SendAsync(new SubmitHeldPaymentOrdersCommand(batchId, Poster));
        Assert.True(supplementary.Success, supplementary.Error);
        var file = await host.DbAsync(db => db.PaymentBatchFiles.AsNoTracking().SingleAsync(f => f.Id == supplementary.Id));
        Assert.Equal((2, PaymentBatchFileKind.HeldRelease, 1), (file.Sequence, file.Kind, file.ControlCount));
        Assert.Equal(PaymentOrderStatus.Submitted, (await OrdersAsync(host, batchId))[blocked].Status);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_runs_calculating_at_once_over_one_free_deduction_take_it_once()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var employee = (await EmployeesAsync(host, 1))[0];
        var deductionId = await host.DbAsync(async db =>
        {
            var deduction = new DeductionRecord { EmployeeId = employee, DeductionType = "Manual", Amount = 25_000m, Reason = "سلفة" };
            db.DeductionRecords.Add(deduction);
            await db.SaveChangesAsync();
            return deduction.Id;
        });
        var january = await CreateRunAsync(host, 1);
        var february = await CreateRunAsync(host, 2);

        // Both calculations read the deduction free and reach the save that marks it before either commits.
        host.Barrier.Arm(2, db => db.ChangeTracker.Entries<DeductionRecord>().Any(e => e.State == EntityState.Modified));
        var results = await Task.WhenAll(
            host.SendAsync(new CalculatePayrollRunCommand(january)),
            host.SendAsync(new CalculatePayrollRunCommand(february)));
        host.Barrier.Disarm();

        var won = Assert.Single(results, r => r.Success);
        var lost = Assert.Single(results, r => !r.Success);
        Assert.Equal(PayrollInputTakenException.TakenMessage, lost.Error);
        var winner = results[0].Success ? january : february;
        var loser = winner == january ? february : january;

        var deduction = await host.DbAsync(db => db.DeductionRecords.AsNoTracking().SingleAsync(d => d.Id == deductionId));
        Assert.Equal(winner, deduction.AppliedInRunId);
        var references = await host.DbAsync(db => (
            from component in db.SalaryLineComponents.AsNoTracking()
            join line in db.SalaryLines.AsNoTracking() on component.SalaryLineId equals line.Id
            where component.ReferenceId == deductionId
            select line.PayrollRunId).ToListAsync());
        Assert.Equal([winner], references);
        Assert.Equal(0, await host.DbAsync(db => db.SalaryLines.CountAsync(l => l.PayrollRunId == loser)));
        output.WriteLine($"P-11: {won.LinesGenerated} line(s) for the winner; the other stopped: «{lost.Error}».");
    }
}
