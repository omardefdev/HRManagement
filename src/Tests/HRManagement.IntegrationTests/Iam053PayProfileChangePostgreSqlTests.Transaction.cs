using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.TestSupport.Time;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Integration;
using Payroll.Infrastructure.Repositories;
using Payroll.Infrastructure.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-53 (the plan's four integration cases) on PostgreSQL with the real bus outbox, as the non-superuser application role with the
/// tenant, audit and outbox wiring of Payroll.Web: (1) two concurrent approvals of one request: one wins, the other is a 409; (2) two
/// concurrent requests for one employee: the filtered index refuses the second; (3) a failure inside the applier's save, in the
/// application and in the database: no new profile, no outbox row, the request still pending; (4) never two active profiles. And the
/// audit row of a request is written in the request's own save, which is how the activation job finds its agencies.
/// </summary>
public sealed partial class Iam053PayProfileChangePostgreSqlTests
{
    private static readonly Guid Employee = Guid.Parse("53000000-0000-4000-8000-0000000000e1");
    private static readonly Guid Processor = Guid.Parse("53000000-0000-4000-8000-0000000000c1");
    private static readonly Guid ApproverOne = Guid.Parse("53000000-0000-4000-8000-0000000000c3");
    private static readonly Guid ApproverTwo = Guid.Parse("53000000-0000-4000-8000-0000000000c4");
    private static readonly DateOnly November = new(2026, 11, 1);

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

    private sealed class FailingSave : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            Armed ? throw new InvalidOperationException("planted failure in the applier's save") : ValueTask.FromResult(result);
    }

    private sealed record Host(ServiceProvider Provider, IBusControl Bus, string Admin, string App, SaveBarrier Barrier, FailingSave Failure)
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

        public Task<PayProfileChangeResult> SendAsync(IRequest<PayProfileChangeResult> command) =>
            InScopeAsync(sp => sp.GetRequiredService<IMediator>().Send(command));
    }

    /// <summary>Payroll's wiring on a migrated database: the application role, the tenant and audit interceptors, the bus outbox.</summary>
    private async Task<Host> StartAsync()
    {
        var (admin, app) = await MigratedAsync();
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

        var tenant = new PayrollPostgresTestSupport.FixedTenant(AgencyA);
        var barrier = new SaveBarrier();
        var failure = new FailingSave();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        // 15 November 2026: November is the current month, so a request from 1 November is due when it is approved.
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero)));
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<PayrollDbContext>((sp, o) => o
            .UseNpgsql(app, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                new TenantIdStampInterceptor(tenant),
                new NpgsqlTenantSessionInterceptor(tenant),
                barrier,
                failure));
        services.AddServiceMassTransit(config, configureOutbox: x => x.AddPostgresOutbox<PayrollDbContext>(), enableInboxFilter: false);
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddPayrollApplication();
        services.AddScoped<IPayProfileChangeEventPublisher, PayProfileChangeEventPublisher>();

        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();

        var host = new Host(provider, bus, admin, app, barrier, failure);
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PayrollDbContext>();
            db.EmployeePayProfiles.Add(new EmployeePayProfile
            {
                EmployeeId = Employee,
                BaseSalary = 4500m,
                EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsActive = true,
                OrgUnitId = Guid.Parse("53000000-0000-4000-8000-0000000000a1"),
                RegimeCode = "law50",
            });
            return await db.SaveChangesAsync();
        });
        return host;
    }

    private static SubmitPayProfileChangeCommand Submit(decimal salary, DateOnly? from = null, Guid? actor = null) => new(
        Employee, salary, null, from ?? November, "ترفيع استثنائي", "القرار 112 لعام 2026", PayProfileChangeSource.Manual, null,
        actor ?? Processor, "processor");

    private static DecidePayProfileChangeCommand Approve(Guid id, Guid approver) =>
        new(id, PayProfileChangeDecision.Approved, null, approver, "approver");

    /// <summary>
    /// The committed outbox rows. This host starts the bus but not the outbox delivery service (no hosted services run), so a committed
    /// row stays in the table; the sequence is not used, because a rolled-back insert consumes a sequence value all the same.
    /// </summary>
    private static Task<long> OutboxRowsEverWrittenAsync(string admin) =>
        ScalarAsync<long>(admin, """SELECT count(*) FROM "OutboxMessage" """);

    private static async Task<(int Active, int Total, int History, PayProfileChangeState? State)> SnapshotAsync(Host host, Guid? requestId = null) =>
        await host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PayrollDbContext>();
            return (
                await db.EmployeePayProfiles.CountAsync(p => p.EmployeeId == Employee && p.IsActive),
                await db.EmployeePayProfiles.CountAsync(p => p.EmployeeId == Employee),
                await db.EmployeePayProfileHistories.CountAsync(h => h.EmployeeId == Employee),
                requestId is null ? (PayProfileChangeState?)null : (await db.PayProfileChangeRequests.SingleAsync(r => r.Id == requestId)).State);
        });

    [Trait("Category", "Testcontainers")]
    [SkippableTheory]
    [InlineData(false)]   // due: both approvals would apply
    [InlineData(true)]    // a later month: both approvals would schedule
    public async Task Two_concurrent_approvals_of_one_request_one_wins_and_the_other_is_a_conflict(bool later)
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var submitted = await host.SendAsync(Submit(5000m, later ? new DateOnly(2026, 12, 1) : November));
        Assert.True(submitted.Success, submitted.Error);
        var id = submitted.RequestId!.Value;
        var outboxBefore = await OutboxRowsEverWrittenAsync(host.Admin);
        Assert.Equal(1, outboxBefore);   // the submission's message, committed with the request and still in the table

        // Both approvers read the pending request before either saves.
        host.Barrier.Arm(2);
        var results = await Task.WhenAll(host.SendAsync(Approve(id, ApproverOne)), host.SendAsync(Approve(id, ApproverTwo)));
        host.Barrier.Arm(0);

        var won = Assert.Single(results, r => r.Success);
        var lost = Assert.Single(results, r => !r.Success);
        Assert.Equal(PayProfileChangeFailure.Conflict, lost.Failure);
        Assert.Equal(409, lost.StatusCode);
        Assert.Equal(later ? PayProfileChangeState.Scheduled : PayProfileChangeState.Applied, won.State);

        var (active, total, history, state) = await SnapshotAsync(host, id);
        Assert.Equal(won.State, state);
        Assert.Equal(1, active);                                   // never two active profiles
        Assert.Equal(later ? 1 : 2, total);                        // applied once
        Assert.Equal(later ? 0 : 1, history);
        // Only the winner's messages were written: the decision, and the application when it applied.
        Assert.Equal(outboxBefore + (later ? 1 : 2), await OutboxRowsEverWrittenAsync(host.Admin));

        var request = await host.InScopeAsync(sp => sp.GetRequiredService<PayrollDbContext>().PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.Id == id));
        Assert.Contains(request.DecidedBy!.Value, new[] { ApproverOne, ApproverTwo });
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_concurrent_requests_for_one_employee_the_filtered_index_refuses_the_second()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var outboxBefore = await OutboxRowsEverWrittenAsync(host.Admin);

        // Both pass the «no open request» read; the index decides.
        host.Barrier.Arm(2);
        var results = await Task.WhenAll(
            host.SendAsync(Submit(5000m, actor: Processor)),
            host.SendAsync(Submit(5100m, actor: Guid.Parse("53000000-0000-4000-8000-0000000000c2"))));
        host.Barrier.Arm(0);

        Assert.Single(results, r => r.Success);
        var refused = Assert.Single(results, r => !r.Success);
        Assert.Equal(PayProfileChangeFailure.Conflict, refused.Failure);
        Assert.Equal(PayProfileChangeMessages.OpenRequestExists, refused.Error);
        Assert.Equal(1, await host.InScopeAsync(sp => sp.GetRequiredService<PayrollDbContext>().PayProfileChangeRequests.CountAsync()));
        // One request, one message: the refused one left no outbox row.
        Assert.Equal(outboxBefore + 1, await OutboxRowsEverWrittenAsync(host.Admin));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_failure_in_the_appliers_save_leaves_no_profile_no_outbox_row_and_the_request_pending()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var submitted = await host.SendAsync(Submit(5000m));
        var id = submitted.RequestId!.Value;
        var outboxBefore = await OutboxRowsEverWrittenAsync(host.Admin);
        var auditBefore = await ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "AuditLogs" """);

        async Task AssertNothingChangedAsync()
        {
            var (active, total, history, state) = await SnapshotAsync(host, id);
            Assert.Equal(PayProfileChangeState.PendingApproval, state);
            Assert.Equal((1, 1, 0), (active, total, history));
            Assert.Equal(outboxBefore, await OutboxRowsEverWrittenAsync(host.Admin));
            Assert.Equal(auditBefore, await ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "AuditLogs" """));
            var profile = await host.InScopeAsync(sp => sp.GetRequiredService<PayrollDbContext>().EmployeePayProfiles.AsNoTracking().SingleAsync());
            Assert.Equal((4500m, true, (DateTime?)null), (profile.BaseSalary, profile.IsActive, profile.EffectiveTo));
            var request = await host.InScopeAsync(sp => sp.GetRequiredService<PayrollDbContext>().PayProfileChangeRequests.AsNoTracking().SingleAsync());
            Assert.Null(request.DecidedBy);
            Assert.Null(request.AppliedProfileId);
        }

        // (a) The application fails after the applier created the version and staged both messages, before the save reaches the database.
        host.Failure.Armed = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.SendAsync(Approve(id, ApproverOne)));
        host.Failure.Armed = false;
        await AssertNothingChangedAsync();

        // (b) The database fails in the middle of the save: the new version's INSERT raises after the previous version's UPDATE ran.
        await ScalarAsync<object?>(host.Admin,
            """
            CREATE FUNCTION iam053_refuse_profile() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'planted failure on the new version'; END $$;
            CREATE TRIGGER iam053_refuse_profile BEFORE INSERT ON "EmployeePayProfiles" FOR EACH ROW EXECUTE FUNCTION iam053_refuse_profile();
            SELECT 1
            """);
        var failed = await Assert.ThrowsAsync<DbUpdateException>(() => host.SendAsync(Approve(id, ApproverOne)));
        Assert.Equal("P0001", Assert.IsType<PostgresException>(failed.InnerException).SqlState);
        await AssertNothingChangedAsync();

        // The request really was untouched: once the cause is gone, the same approval applies it, with its two messages.
        await ScalarAsync<object?>(host.Admin, """DROP TRIGGER iam053_refuse_profile ON "EmployeePayProfiles"; SELECT 1""");
        var approved = await host.SendAsync(Approve(id, ApproverOne));
        Assert.True(approved.Success, approved.Error);
        Assert.Equal(PayProfileChangeState.Applied, approved.State);
        Assert.Equal((1, 2, 1, (PayProfileChangeState?)PayProfileChangeState.Applied), await SnapshotAsync(host, id));
        Assert.Equal(outboxBefore + 2, await OutboxRowsEverWrittenAsync(host.Admin));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task After_every_path_the_employee_has_one_active_profile_and_the_database_refuses_a_second()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();

        // Applied, then scheduled and activated by the system, then an automatic change: three new versions.
        var first = await host.SendAsync(Submit(4800m));
        Assert.Equal(PayProfileChangeState.Applied, (await host.SendAsync(Approve(first.RequestId!.Value, ApproverOne))).State);
        Assert.Equal(1, (await SnapshotAsync(host)).Active);

        var automatic = await host.InScopeAsync(sp => sp.GetRequiredService<IPayProfileChangeApplier>().ApplySystemChangeAsync(
            Employee, PayProfileChangeKind.BaseSalary, PayProfileChangeSource.Promotion, "قرار ترفيع 14/2026", 5200m, null, new DateOnly(2026, 12, 1)));
        Assert.True(automatic.Success, automatic.Error);

        var (active, total, history, _) = await SnapshotAsync(host);
        Assert.Equal((1, 3, 2), (active, total, history));

        // The new version carries every field the old upsert dropped, through the real database.
        var current = await host.InScopeAsync(sp => sp.GetRequiredService<PayrollDbContext>().EmployeePayProfiles.AsNoTracking().SingleAsync(p => p.IsActive));
        Assert.Equal((5200m, "law50"), (current.BaseSalary, current.RegimeCode));
        Assert.NotNull(current.OrgUnitId);

        // The unique index is the last line: a second active profile of the employee is refused by the database itself.
        var second = await Assert.ThrowsAsync<DbUpdateException>(() => host.InScopeAsync(async sp =>
        {
            var db = sp.GetRequiredService<PayrollDbContext>();
            db.EmployeePayProfiles.Add(new EmployeePayProfile { EmployeeId = Employee, BaseSalary = 1m, EffectiveFrom = DateTime.UtcNow, IsActive = true });
            return await db.SaveChangesAsync();
        }));
        Assert.Equal("IX_EmployeePayProfiles_OneActive", Assert.IsType<PostgresException>(second.InnerException).ConstraintName);
        Assert.Equal(0L, await ScalarAsync<long>(host.Admin,
            """SELECT count(*) FROM (SELECT "EmployeeId" FROM "EmployeePayProfiles" WHERE "IsActive" GROUP BY "EmployeeId" HAVING count(*) > 1) x"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_audit_row_of_a_request_is_written_in_its_own_save_without_the_amount_and_names_the_agency_for_the_job()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();

        // Before any request: the job finds no agency.
        Assert.Empty(await host.InScopeAsync(sp => PayProfileChangeActivationJob.FindTenantsAsync(sp.GetRequiredService<PayrollDbContext>())));

        var submitted = await host.SendAsync(Submit(5123m));
        Assert.True(submitted.Success, submitted.Error);

        // The interceptor wrote the audit row in the request's save: same agency, the request's id, and no amount (AuditRedact).
        var audit = await ScalarAsync<string>(host.Admin,
            $"""
             SELECT "TenantId"::text || '|' || "Action" || '|' || coalesce("ChangedFields", '')
             FROM "AuditLogs" WHERE "EntityType" = 'PayProfileChangeRequest' AND "EntityId" = '{submitted.RequestId}'
             """);
        Assert.StartsWith($"{AgencyA}|", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("5123", audit, StringComparison.Ordinal);
        Assert.DoesNotContain("4500", audit, StringComparison.Ordinal);

        // The audit log is outside row security: the job reads the agencies with no tenant in its session.
        Assert.Equal(0L, await ScalarAsync<long>(host.App, """SELECT count(*) FROM "PayProfileChangeRequests" """));
        Assert.Equal(1L, await ScalarAsync<long>(host.App, """SELECT count(DISTINCT "TenantId") FROM "AuditLogs" WHERE "EntityType" = 'PayProfileChangeRequest'"""));
        Assert.Equal([AgencyA], await host.InScopeAsync(sp => PayProfileChangeActivationJob.FindTenantsAsync(sp.GetRequiredService<PayrollDbContext>())));
    }
}
