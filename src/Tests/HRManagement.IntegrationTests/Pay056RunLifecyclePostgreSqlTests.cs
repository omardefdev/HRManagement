using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Features.PayrollRuns.Reversal;
using Payroll.Application.Options;
using Payroll.Domain.Constants;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Integration;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-56 (the plan's four integration cases) on PostgreSQL with the real bus outbox, as the non-superuser application role under
/// forced row security, with the tenant and audit wiring of Payroll.Web: (1) a rejection concurrent with an approval of the same run:
/// one wins, the other loses on the row's version (a 409 at the API); (2) a cancellation while a calculation job runs: it waits for
/// the save in flight, then every input is free and the worker writes no line after it; (3) the run's indexes: a second regular run
/// of a period after the first is reversed, and never a second open correction of one source; (4) <c>IPayrollReversed</c> is in the
/// outbox of the reversal's own transaction, and a reversal that fails leaves no message, no released input and no voided batch.
/// And a duplicate bank reference of a run's batch is a named conflict, never an unhandled error.
/// </summary>
[Collection("PgRabbit")]
public sealed class Pay056RunLifecyclePostgreSqlTests(PgRabbitFixture fixture, ITestOutputHelper output)
{
    private static readonly Guid Agency = Guid.Parse("56000000-0000-4000-8000-00000000000a");
    private static readonly Guid Processor = Guid.Parse("56000000-0000-4000-8000-0000000000c1");
    private static readonly Guid ApproverOne = Guid.Parse("56000000-0000-4000-8000-0000000000c2");
    private static readonly Guid ApproverTwo = Guid.Parse("56000000-0000-4000-8000-0000000000c5");
    private static readonly Guid Poster = Guid.Parse("56000000-0000-4000-8000-0000000000c3");
    private static readonly Guid Reverser = Guid.Parse("56000000-0000-4000-8000-0000000000c4");
    private const string Note = "بدل النقل لم يُحتسب؛ يُصحَّح ثم يُعاد الإرسال.";

    /// <summary>Holds every save until <see cref="Participants"/> units of work have reached theirs, so both read before either writes.</summary>
    private sealed class SaveBarrier : SaveChangesInterceptor
    {
        private int _waiting;
        private TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Participants { get; private set; }

        public void Arm(int participants)
        {
            _waiting = 0;
            _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Participants = participants;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Participants > 0)
            {
                if (Interlocked.Increment(ref _waiting) >= Participants)
                    _released.TrySetResult();
                await _released.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }

            return result;
        }
    }

    /// <summary>
    /// Stops a calculation worker inside its save: the save that writes the <see cref="PauseAt"/>-th salary line waits here, with its
    /// transaction open, until the test lets it go.
    /// </summary>
    private sealed class LineSaveGate : SaveChangesInterceptor
    {
        private int _lineSaves;
        private TaskCompletionSource _go = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int PauseAt { get; private set; }

        public TaskCompletionSource Reached { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Arm(int pauseAt)
        {
            _lineSaves = 0;
            _go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            PauseAt = pauseAt;
        }

        public void Release() => _go.TrySetResult();

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (PauseAt > 0
                && eventData.Context!.ChangeTracker.Entries<SalaryLine>().Any(e => e.State == EntityState.Added)
                && Interlocked.Increment(ref _lineSaves) == PauseAt)
            {
                Reached.TrySetResult();
                await _go.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            }

            return result;
        }
    }

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed ? throw new InvalidOperationException("planted failure in the reversal's save") : ValueTask.FromResult(result);
    }

    private sealed record Host(ServiceProvider Provider, IBusControl Bus, string Admin, SaveBarrier Barrier, LineSaveGate Gate, FailingSave Failure)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Bus.StopAsync();
            await Provider.DisposeAsync();
        }

        /// <summary>One unit of work, as a request is: its own scope and context.</summary>
        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }

        public Task<T> SendAsync<T>(IRequest<T> command) => InScopeAsync(sp => sp.GetRequiredService<IMediator>().Send(command));

        public Task<T> DbAsync<T>(Func<PayrollDbContext, Task<T>> read) => InScopeAsync(sp => read(sp.GetRequiredService<PayrollDbContext>()));
    }

    /// <summary>Payroll's wiring on a migrated database: the application role, the tenant and audit interceptors, the bus outbox.</summary>
    private async Task<Host> StartAsync(int bulkWriteSize = 25)
    {
        var name = TestDatabaseNames.Unique("pay056_");
        var admin = await fixture.Postgres.CreateDatabaseAsync(name);
        var tenant = new PayrollPostgresTestSupport.FixedTenant(Agency);
        await using (var migrate = new PayrollDbContext(new DbContextOptionsBuilder<PayrollDbContext>()
                         .UseNpgsql(admin, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name)).Options))
            await migrate.Database.MigrateAsync();
        var app = await fixture.Postgres.CreateAppConnectionAsync(name);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0",
        }).Build();

        var barrier = new SaveBarrier();
        var gate = new LineSaveGate();
        var failure = new FailingSave();
        var services = new ServiceCollection();
        services.AddLogging();
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
        // After the shared set-up, so that these win: the bus's configuration and the real publisher (the outbox is what is proved).
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<PayrollDbContext>((sp, o) => o
            .UseNpgsql(app, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                new TenantIdStampInterceptor(tenant),
                new NpgsqlTenantSessionInterceptor(tenant),
                barrier,
                gate,
                failure));
        services.AddServiceMassTransit(config, configureOutbox: x => x.AddPostgresOutbox<PayrollDbContext>(), enableInboxFilter: false);
        services.AddScoped<IPayrollEventPublisher, PayrollEventPublisher>();
        services.PostConfigure<PayrollBatchOptions>(o =>
        {
            o.MaxDegreeOfParallelism = 1;
            o.BulkWriteSize = bulkWriteSize;
        });

        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        var host = new Host(provider, bus, admin, barrier, gate, failure);
        await host.DbAsync(async db =>
        {
            await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
            return 0;
        });

        // The control of every test here: the application role is neither superuser nor exempt from row security.
        Assert.False(await ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        return host;
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var command = new NpgsqlCommand(sql, c);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>The message types of the committed outbox rows (this host starts the bus but no delivery service, so a row stays).</summary>
    private static async Task<List<string>> OutboxAsync(string admin)
    {
        var types = new List<string>();
        await using var c = new NpgsqlConnection(admin);
        await c.OpenAsync();
        await using var command = new NpgsqlCommand("""SELECT "MessageType" || ' ' || "Body" FROM "OutboxMessage" ORDER BY "SequenceNumber" """, c);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            types.Add(reader.GetString(0));
        return types;
    }

    private static async Task<List<Guid>> EmployeesAsync(Host host, int count, bool withAdjustment = false)
    {
        var ids = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        await host.DbAsync(async db =>
        {
            foreach (var id in ids)
            {
                db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(id, 900_000m));
                if (withAdjustment)
                    db.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = id, Amount = 10_000m, AdjustmentReason = "فرق نقل" });
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

    private static async Task<Guid> UnderReviewRunAsync(Host host, int month)
    {
        var runId = await CreateRunAsync(host, month);
        var calculated = await host.SendAsync(new CalculatePayrollRunCommand(runId));
        Assert.True(calculated.Success, calculated.Error);
        var submitted = await host.SendAsync(new SubmitPayrollRunForReviewCommand(runId));
        Assert.True(submitted.Success, submitted.Error);
        return runId;
    }

    private static async Task<Guid> ApprovedRunAsync(Host host, int month)
    {
        var runId = await UnderReviewRunAsync(host, month);
        var approved = await host.SendAsync(new ApprovePayrollRunCommand(runId, ApproverOne, "payroll-approver", null));
        Assert.True(approved.Success, approved.Error);
        await host.DbAsync(async db =>
        {
            // The closed audit record a posting needs: a test-only precondition (no persona closes one before PLAN-58, G-PAY-05).
            db.PayrollAuditRecords.Add(new PayrollAuditRecord
            {
                PayrollRunId = runId,
                ClosedBy = Guid.NewGuid(),
                ClosedAt = DateTime.UtcNow,
                Status = AuditStatus.Closed,
                Findings = "لا ملاحظات",
            });
            return await db.SaveChangesAsync();
        });
        return runId;
    }

    private static async Task<Guid> PostedRunAsync(Host host, int month)
    {
        var runId = await ApprovedRunAsync(host, month);
        var posted = await host.SendAsync(new PostPayrollRunCommand(runId, Poster));
        Assert.True(posted.Success, posted.Error);
        return runId;
    }

    private static Task<PayrollRun> RunAsync(Host host, Guid runId) =>
        host.DbAsync(db => db.PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == runId));

    /// <summary>The result of a command, or the exception it threw (a lost race is an exception the web layer answers 409).</summary>
    private static async Task<(T? Result, Exception? Thrown)> TryAsync<T>(Task<T> work)
    {
        try
        {
            return (await work, null);
        }
        catch (Exception ex)
        {
            return (default, ex);
        }
    }

    // --- (1) a rejection concurrent with an approval ---

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_rejection_concurrent_with_an_approval_of_the_same_run_one_wins_and_the_other_is_a_version_conflict()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeesAsync(host, 3, withAdjustment: true);
        var runId = await UnderReviewRunAsync(host, 1);

        // Both reviewers read the run under review before either saves.
        host.Barrier.Arm(2);
        var approving = TryAsync(host.SendAsync(new ApprovePayrollRunCommand(runId, ApproverOne, "payroll-approver", null)));
        var rejecting = TryAsync(host.SendAsync(new RejectPayrollRunCommand(runId, ApproverTwo, Note)));
        var (approval, approvalThrown) = await approving;
        var (rejection, rejectionThrown) = await rejecting;
        host.Barrier.Arm(0);

        var approved = approvalThrown is null && approval.Success;
        var rejected = rejectionThrown is null && rejection!.Success;
        Assert.True(approved ^ rejected, $"approved={approved} ({approvalThrown?.GetType().Name} {approval.Error}); rejected={rejected} ({rejectionThrown?.GetType().Name} {rejection?.Error})");
        // The loser lost on the run's row version: the exception the conflict middleware answers 409.
        var lost = approved ? rejectionThrown : approvalThrown;
        Assert.NotNull(lost);
        Assert.True(ConcurrencyExtensions.IsConcurrencyConflict(lost!), lost!.ToString());

        var run = await RunAsync(host, runId);
        var lines = await host.DbAsync(db => db.SalaryLines.CountAsync(l => l.PayrollRunId == runId));
        var attached = await host.DbAsync(db => db.PayrollAdjustments.CountAsync(a => a.PayrollRunId == runId));
        var revisions = await host.DbAsync(db => db.PayrollRunRevisions.CountAsync(r => r.PayrollRunId == runId));
        var decisions = await host.DbAsync(db => db.PayrollRunApprovals.AsNoTracking().Where(a => a.PayrollRunId == runId).ToListAsync());
        var decision = Assert.Single(decisions);
        if (approved)
        {
            // The approval won: the run is approved as it was reviewed; the rejection deleted nothing and released nothing.
            Assert.Equal((PayrollRunStatus.Approved, 1, true), (run.Status, run.Revision, run.ResultsFrozen));
            Assert.Equal((3, 3, 0), (lines, attached, revisions));
            Assert.Equal((ApprovalDecision.Approved, ApproverOne), (decision.Decision, decision.ApproverId));
        }
        else
        {
            // The rejection won: the run reopened; the approval changed nothing.
            Assert.Equal((PayrollRunStatus.Draft, 2, false), (run.Status, run.Revision, run.ResultsFrozen));
            Assert.Equal((0, 0, 1), (lines, attached, revisions));
            Assert.Equal((ApprovalDecision.Rejected, ApproverTwo, Note), (decision.Decision, decision.ApproverId, decision.Notes));
        }

        output.WriteLine($"winner: {(approved ? "approval" : "rejection")}; loser: {lost.GetType().Name}");
    }

    // --- (2) a cancellation while a job runs ---

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_cancellation_while_a_job_runs_waits_for_the_save_in_flight_frees_every_input_and_the_worker_writes_no_line_after_it()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        // One employee per save, so the calculation can be stopped between two employees.
        await using var host = await StartAsync(bulkWriteSize: 1);
        await EmployeesAsync(host, 6, withAdjustment: true);
        var runId = await CreateRunAsync(host, 1);

        // The worker stops inside the save of its third line: its transaction is open and it holds the job's row.
        host.Gate.Arm(pauseAt: 3);
        var calculating = host.SendAsync(new CalculatePayrollRunCommand(runId));
        await host.Gate.Reached.Task.WaitAsync(TimeSpan.FromSeconds(60));

        var cancelling = host.SendAsync(new CancelPayrollRunCommand(runId, Processor, "أُنشئ التشغيل قبل اعتماد التسويات", true, false));
        // The cancellation waits for the save in flight (it takes the job's row first): it has not finished.
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.False(cancelling.IsCompleted, "the cancellation did not wait for the worker's save in flight");

        host.Gate.Release();
        var cancelled = await cancelling.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.True(cancelled.Success, cancelled.Error);
        var linesAtCancellation = await host.DbAsync(db => db.SalaryLines.CountAsync(l => l.PayrollRunId == runId));

        var calculated = await calculating.WaitAsync(TimeSpan.FromSeconds(60));
        Assert.False(calculated.Success);
        Assert.Contains("أُلغيت", calculated.Error);

        var run = await RunAsync(host, runId);
        Assert.Equal((PayrollRunStatus.Cancelled, (Guid?)Processor), (run.Status, run.CancelledBy));
        var job = await host.DbAsync(db => db.PayrollRunJobs.AsNoTracking().Include(j => j.Partitions).SingleAsync(j => j.PayrollRunId == runId));
        Assert.Equal(PayrollRunJobStatus.Cancelled, job.Status);
        Assert.DoesNotContain(job.Partitions, p => p.Status is PayrollRunPartitionStatus.Running or PayrollRunPartitionStatus.Pending);

        // Every input is free: the two the worker had attached before, and the one of the save in flight the cancellation waited for.
        Assert.Equal(0, await host.DbAsync(db => db.PayrollAdjustments.CountAsync(a => a.PayrollRunId != null)));
        Assert.Equal(6, await host.DbAsync(db => db.PayrollAdjustments.CountAsync()));
        // No orphan line: the worker wrote nothing after the cancellation. The lines it had written before stay as the run's record.
        var linesAfter = await host.DbAsync(db => db.SalaryLines.CountAsync(l => l.PayrollRunId == runId));
        Assert.Equal(3, linesAtCancellation);
        Assert.Equal(linesAtCancellation, linesAfter);

        // The period is free, and a new run takes all six inputs.
        var again = await CreateRunAsync(host, 1);
        var recalculated = await host.SendAsync(new CalculatePayrollRunCommand(again));
        Assert.True(recalculated.Success, recalculated.Error);
        Assert.Equal(6, await host.DbAsync(db => db.PayrollAdjustments.CountAsync(a => a.PayrollRunId == again)));
        output.WriteLine($"lines written before the cancellation: {linesAtCancellation}; after the worker finished: {linesAfter}; the new run took 6 inputs");
    }

    // --- (3) the indexes ---

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_second_regular_run_of_a_period_is_refused_while_the_first_is_live_and_made_after_it_is_reversed()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeesAsync(host, 2, withAdjustment: true);
        var first = await PostedRunAsync(host, 1);
        var start = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var sameMonth = new CreatePayrollRunCommand(start, start.AddMonths(1).AddDays(-1), start.AddMonths(1));

        var whileLive = await host.SendAsync(sameMonth);
        Assert.False(whileLive.Success);
        Assert.Contains("تشغيل رواتب حي", whileLive.Error);

        var request = await host.SendAsync(new RequestPayrollReversalCommand(first, Reverser, "رُحّل قبل اعتماد التسويات"));
        Assert.True(request.Success, request.Error);
        var decided = await host.SendAsync(new DecidePayrollReversalCommand(request.Id!.Value, ApproverTwo, true, null));
        Assert.True(decided.Success, decided.Error);

        var afterReversal = await host.SendAsync(sameMonth);
        Assert.True(afterReversal.Success, afterReversal.Error);
        var calculated = await host.SendAsync(new CalculatePayrollRunCommand(afterReversal.Id!.Value));
        Assert.True(calculated.Success, calculated.Error);
        // The released inputs are in the new run.
        Assert.Equal(2, await host.DbAsync(db => db.PayrollAdjustments.CountAsync(a => a.PayrollRunId == afterReversal.Id)));

        // The index's own word: a regular run of the same period, inserted around the handler, is refused by name.
        var duplicate = await TryAsync(host.DbAsync(async db =>
        {
            db.PayrollRuns.Add(new PayrollRun { PeriodStart = start, PeriodEnd = start.AddMonths(1).AddDays(-1), RunDate = start.AddMonths(1), Status = PayrollRunStatus.Draft });
            return await db.SaveChangesAsync();
        }));
        var violation = Assert.IsType<DbUpdateException>(duplicate.Thrown);
        Assert.True(ConcurrencyExtensions.TryGetUniqueConstraintName(violation, out var constraint));
        Assert.Equal(PayrollRunIndexNames.LivePeriod, constraint);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_corrections_of_one_source_at_the_same_moment_the_index_lets_one_in()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var employees = await EmployeesAsync(host, 2);
        var source = await PostedRunAsync(host, 1);
        await host.DbAsync(async db =>
        {
            foreach (var employee in employees)
                db.PayrollAdjustments.Add(new PayrollAdjustment { EmployeeId = employee, Amount = 5_000m, AdjustmentReason = "فرق ترفيع", TargetSourceRunId = source });
            return await db.SaveChangesAsync();
        });
        var command = new CreateCorrectionRunCommand(source, new DateTime(2028, 3, 5, 0, 0, 0, DateTimeKind.Utc));

        // Both find no open correction before either inserts its run.
        host.Barrier.Arm(2);
        var results = await Task.WhenAll(host.SendAsync(command), host.SendAsync(command));
        host.Barrier.Arm(0);

        var won = Assert.Single(results, r => r.Success);
        var lost = Assert.Single(results, r => !r.Success);
        Assert.Equal(RunActOutcome.Conflict, lost.Result.Outcome);
        Assert.Equal(CorrectionRunMessages.OpenCorrectionExists, lost.Error);
        var corrections = await host.DbAsync(db => db.PayrollRuns.AsNoTracking().Where(r => r.RunType == PayrollRunType.Correction).ToListAsync());
        var correction = Assert.Single(corrections);
        Assert.Equal((won.CorrectionRunId, PayrollRunStatus.Calculated, true), (correction.Id, correction.Status, correction.ValidatedAtCalculate));
        Assert.Equal(2, await host.DbAsync(db => db.SalaryLines.CountAsync(l => l.PayrollRunId == correction.Id)));
        // A correction and its source share a period: the live index (regular runs only) does not stand between them.
        Assert.Equal((await RunAsync(host, source)).PeriodStart, correction.PeriodStart);
    }

    // --- (4) the event in the reversal's own transaction ---

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_reversals_event_is_in_the_outbox_of_its_own_transaction_and_a_reversal_that_fails_leaves_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeesAsync(host, 2, withAdjustment: true);
        var january = await PostedRunAsync(host, 1);
        var total = (await RunAsync(host, january)).TotalNetPay;
        var before = await OutboxAsync(host.Admin);
        Assert.Single(before, m => m.Contains("IPayrollProcessed", StringComparison.Ordinal));
        Assert.DoesNotContain(before, m => m.Contains("IPayrollReversed", StringComparison.Ordinal));

        // A reversal whose save fails: the transaction takes everything back with it.
        var request = await host.SendAsync(new RequestPayrollReversalCommand(january, Reverser, "رُحّل قبل اعتماد التسويات"));
        Assert.True(request.Success, request.Error);
        host.Failure.Armed = true;
        var failed = await TryAsync(host.SendAsync(new DecidePayrollReversalCommand(request.Id!.Value, ApproverTwo, true, null)));
        host.Failure.Armed = false;
        Assert.IsType<InvalidOperationException>(failed.Thrown);
        Assert.Equal(before.Count, (await OutboxAsync(host.Admin)).Count);
        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, january)).Status);
        Assert.Equal(PaymentBatchStatus.Pending, (await host.DbAsync(db => db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == january))).Status);
        // The inputs the failed reversal had released inside its transaction are attached again.
        Assert.Equal(2, await host.DbAsync(db => db.PayrollAdjustments.CountAsync(a => a.PayrollRunId == january)));
        Assert.Equal(PayrollReversalRequestStatus.Pending,
            (await host.DbAsync(db => db.PayrollReversalRequests.AsNoTracking().SingleAsync(r => r.Id == request.Id))).Status);

        // The same decision again: it commits, and its message is committed with it.
        var decided = await host.SendAsync(new DecidePayrollReversalCommand(request.Id!.Value, ApproverTwo, true, "لم تُرسل الدفعة"));
        Assert.True(decided.Success, decided.Error);
        var after = await OutboxAsync(host.Admin);
        Assert.Equal(before.Count + 1, after.Count);
        var message = Assert.Single(after, m => m.Contains("IPayrollReversed", StringComparison.Ordinal));
        Assert.Contains(january.ToString(), message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(request.Id!.Value.ToString(), message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PayrollRunStatus.Reversed, (await RunAsync(host, january)).Status);
        Assert.Equal(PaymentBatchStatus.Voided, (await host.DbAsync(db => db.PaymentBatches.AsNoTracking().SingleAsync(b => b.PayrollRunId == january))).Status);
        Assert.Equal(0, await host.DbAsync(db => db.PayrollAdjustments.CountAsync(a => a.PayrollRunId == january)));
        Assert.Equal(total, (await RunAsync(host, january)).TotalNetPay);

        // A decision refused by D-PAY-16 writes no message either.
        await EmployeesAsync(host, 1);
        var february = await PostedRunAsync(host, 2);
        await host.DbAsync(db => db.PaymentBatches.Where(b => b.PayrollRunId == february)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.Status, PaymentBatchStatus.Submitted)));
        var second = await host.SendAsync(new RequestPayrollReversalCommand(february, Reverser, "طلب عكس بعد الإرسال"));
        var refused = await host.SendAsync(new DecidePayrollReversalCommand(second.Id!.Value, ApproverTwo, true, null));
        Assert.Equal(PayrollReversalMessages.MoneyHasLeft, refused.Error);
        Assert.Single(await OutboxAsync(host.Admin), m => m.Contains("IPayrollReversed", StringComparison.Ordinal));
        Assert.Equal(PayrollRunStatus.Posted, (await RunAsync(host, february)).Status);
    }

    // --- a duplicate bank reference ---

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_duplicate_bank_reference_of_a_runs_batch_is_a_named_conflict_and_the_posting_changes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeesAsync(host, 1);
        var runId = await ApprovedRunAsync(host, 1);
        var run = await RunAsync(host, runId);
        // A batch with the reference the posting writes is there already (a posting that another request is making).
        await host.DbAsync(async db =>
        {
            db.PaymentBatches.Add(new PaymentBatch
            {
                PayrollRunId = runId,
                TotalAmount = run.TotalNetPay,
                Status = PaymentBatchStatus.Pending,
                BankTransferReference = $"PAY-{run.PeriodStart:yyyyMM}-{runId:N}"[..32],
            });
            return await db.SaveChangesAsync();
        });
        var outboxBefore = (await OutboxAsync(host.Admin)).Count;

        var posting = await TryAsync(host.SendAsync(new PostPayrollRunCommand(runId, Poster)));

        var violation = Assert.IsType<DbUpdateException>(posting.Thrown);
        Assert.True(ConcurrencyExtensions.IsUniqueViolation(violation));
        Assert.True(ConcurrencyExtensions.TryGetUniqueConstraintName(violation, out var constraint));
        Assert.Equal("IX_PaymentBatches_Run_BankRef", constraint);
        // What the conflict middleware answers with: 409 and this message.
        Assert.Equal(UniqueConstraintMessageRegistry.MessageForPaymentBatchReference, UniqueConstraintMessageRegistry.Resolve(violation).Message);
        Assert.Equal(PayrollRunStatus.Approved, (await RunAsync(host, runId)).Status);
        Assert.Equal(1, await host.DbAsync(db => db.PaymentBatches.CountAsync(b => b.PayrollRunId == runId)));
        Assert.Equal(outboxBefore, (await OutboxAsync(host.Admin)).Count);
    }
}
