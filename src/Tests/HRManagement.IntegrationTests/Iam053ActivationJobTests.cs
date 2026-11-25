using HRManagement.TestSupport.PayrollRuns;
using HRManagement.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payroll.Application;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Repositories;
using Payroll.Infrastructure.Services;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-53 (P-7): <see cref="PayProfileChangeActivationJob"/> with a controlled clock, on PostgreSQL as the non-superuser application
/// role with Payroll's tenant wiring (the tenant of each cycle comes from <see cref="TenantIdFlow"/>, as in the service). A scheduled
/// request that is due is applied once, even when the job runs twice; one not yet due is untouched; a blocked one stays scheduled with
/// its reason and the gauge counts it; each agency's request is applied in its own agency only; one agency's failure does not hide the
/// others; and a replica that does not hold the lock does nothing.
/// </summary>
[Collection("PostgreSql")]
public sealed class Iam053ActivationJobTests(PostgreSqlFixture fixture)
{
    private static readonly Guid Processor = Guid.Parse("53000000-0000-4000-8000-0000000000c1");
    private static readonly Guid Approver = Guid.Parse("53000000-0000-4000-8000-0000000000c3");

    private sealed class MovableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class JobHost(ServiceProvider provider, string admin, string app, MovableClock clock) : IAsyncDisposable
    {
        public string Admin { get; } = admin;

        public string App { get; } = app;

        public MovableClock Clock { get; } = clock;

        public RecordingPayProfileChangeEvents Events => provider.GetRequiredService<RecordingPayProfileChangeEvents>();

        public PayProfileChangeActivationJob Job { get; } = ActivatorUtilities.CreateInstance<PayProfileChangeActivationJob>(provider);

        public ValueTask DisposeAsync() => provider.DisposeAsync();

        /// <summary>One unit of work under an agency, as a request of that agency is.</summary>
        public async Task<T> AsAsync<T>(Guid tenant, Func<IServiceProvider, Task<T>> work)
        {
            var previous = TenantIdFlow.Get();
            TenantIdFlow.Set(tenant);
            try
            {
                await using var scope = provider.CreateAsyncScope();
                return await work(scope.ServiceProvider);
            }
            finally
            {
                TenantIdFlow.Set(previous);
            }
        }

        /// <summary>An employee with an active profile from January 2026, and a request approved for <paramref name="from"/>.</summary>
        public async Task<(Guid Employee, Guid Request)> ScheduledAsync(Guid tenant, DateOnly from, decimal salary = 5000m)
        {
            var employee = Guid.NewGuid();
            await AsAsync(tenant, async sp =>
            {
                var db = sp.GetRequiredService<PayrollDbContext>();
                db.EmployeePayProfiles.Add(new EmployeePayProfile
                {
                    EmployeeId = employee,
                    BaseSalary = 4500m,
                    EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    IsActive = true,
                    OrgUnitId = Guid.NewGuid(),
                    RegimeCode = "law50",
                });
                return await db.SaveChangesAsync();
            });
            var submitted = await AsAsync(tenant, sp => sp.GetRequiredService<MediatR.IMediator>().Send(new SubmitPayProfileChangeCommand(
                employee, salary, null, from, "ترفيع من أول الشهر القادم", "القرار 9 لعام 2026", PayProfileChangeSource.Manual, null, Processor, "processor")));
            Assert.True(submitted.Success, submitted.Error);
            var requestId = submitted.RequestId!.Value;
            var approved = await AsAsync(tenant, sp => sp.GetRequiredService<MediatR.IMediator>().Send(new DecidePayProfileChangeCommand(
                requestId, PayProfileChangeDecision.Approved, null, Approver, "approver")));
            Assert.Equal(PayProfileChangeState.Scheduled, approved.State);
            return (employee, requestId);
        }

        public Task<PayProfileChangeRequest> RequestAsync(Guid tenant, Guid id) =>
            AsAsync(tenant, sp => sp.GetRequiredService<PayrollDbContext>().PayProfileChangeRequests.AsNoTracking().SingleAsync(r => r.Id == id));

        public Task<List<EmployeePayProfile>> ProfilesAsync(Guid tenant, Guid employee) =>
            AsAsync(tenant, sp => sp.GetRequiredService<PayrollDbContext>().EmployeePayProfiles.AsNoTracking()
                .Where(p => p.EmployeeId == employee).OrderBy(p => p.EffectiveFrom).ToListAsync());
    }

    private async Task<JobHost> StartAsync()
    {
        var name = TestDatabaseNames.Unique("iam053job_");
        var admin = await fixture.CreateDatabaseAsync(name);
        await using (var migrate = new PayrollDbContext(
                         new DbContextOptionsBuilder<PayrollDbContext>()
                             .UseNpgsql(admin, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name)).Options,
                         new PayrollPostgresTestSupport.FixedTenant(Guid.NewGuid())))
            await migrate.Database.MigrateAsync();
        var app = await fixture.CreateAppConnectionAsync(name);

        // 15 November 2026: a request for 1 December is scheduled.
        var clock = new MovableClock(new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddHttpContextAccessor();
        services.AddTenantContext();   // the real tenant context: no request here, so the tenant is TenantIdFlow's, as in the job
        services.AddSingleton<TimeProvider>(clock);
        services.AddScoped<AuditSaveChangesInterceptor>();
        services.AddDbContext<PayrollDbContext>((sp, o) => o
            .UseNpgsql(app, n => n.MigrationsAssembly(typeof(PayrollDbContext).Assembly.GetName().Name))
            .AddInterceptors(
                sp.GetRequiredService<AuditSaveChangesInterceptor>(),
                sp.GetRequiredService<TenantIdStampInterceptor>(),
                sp.GetRequiredService<NpgsqlTenantSessionInterceptor>()));
        services.AddScoped<IPayrollRepository, PayrollRepository>();
        services.AddPayrollApplication();
        services.AddSingleton<RecordingPayProfileChangeEvents>();
        services.AddScoped<IPayProfileChangeEventPublisher>(sp => sp.GetRequiredService<RecordingPayProfileChangeEvents>());
        return new JobHost(services.BuildServiceProvider(), admin, app, clock);
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var command = new NpgsqlCommand(sql, c);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private static readonly DateOnly December = new(2026, 12, 1);
    private static readonly DateTimeOffset FirstOfDecemberInDamascus = new(2026, 11, 30, 21, 5, 0, TimeSpan.Zero);

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_due_request_is_applied_once_though_the_job_runs_twice_and_one_not_due_is_untouched()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        await using var host = await StartAsync();
        var tenant = Guid.NewGuid();
        var (dueEmployee, due) = await host.ScheduledAsync(tenant, December);
        var (laterEmployee, later) = await host.ScheduledAsync(tenant, new DateOnly(2027, 1, 1));
        host.Events.Published.Clear();

        // Before the date: nothing is due.
        var early = await host.Job.RunOnceAsync();
        Assert.Equal(0, early[tenant].Due);
        Assert.Equal(PayProfileChangeState.Scheduled, (await host.RequestAsync(tenant, due)).State);

        // 1 December 00:05 in Damascus (still 30 November in UTC): December's request is due, January's is not.
        host.Clock.Now = FirstOfDecemberInDamascus;
        var first = await host.Job.RunOnceAsync();
        var second = await host.Job.RunOnceAsync();

        Assert.Equal((1, 1, 0, 0), (first[tenant].Due, first[tenant].Applied, first[tenant].Blocked, first[tenant].BlockedTotal));
        Assert.Equal((0, 0), (second[tenant].Due, second[tenant].Applied));

        var applied = await host.RequestAsync(tenant, due);
        Assert.Equal(PayProfileChangeState.Applied, applied.State);
        Assert.Equal(Approver, applied.DecidedBy);
        Assert.Equal(host.Clock.Now.UtcDateTime, applied.AppliedAt);
        var profiles = await host.ProfilesAsync(tenant, dueEmployee);
        Assert.Equal(2, profiles.Count);                                             // once
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), profiles[0].EffectiveTo);
        Assert.Equal((5000m, true, "law50"), (profiles[1].BaseSalary, profiles[1].IsActive, profiles[1].RegimeCode));
        Assert.Equal(applied.AppliedProfileId, profiles[1].Id);
        Assert.IsType<PayProfileChangeAppliedEvent>(Assert.Single(host.Events.Published));

        Assert.Equal(PayProfileChangeState.Scheduled, (await host.RequestAsync(tenant, later)).State);
        Assert.Single(await host.ProfilesAsync(tenant, laterEmployee));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_blocked_request_stays_scheduled_with_its_reason_and_the_gauge_counts_it()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        await using var host = await StartAsync();
        var tenant = Guid.NewGuid();
        var (employee, request) = await host.ScheduledAsync(tenant, December);
        // December's Regular run was calculated and frozen before the change could be applied.
        var run = await host.AsAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<PayrollDbContext>();
            var frozen = new PayrollRun
            {
                PeriodStart = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                PeriodEnd = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                RunDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                RunType = PayrollRunType.Regular,
                Status = PayrollRunStatus.Calculated,
                ResultsFrozen = true,
            };
            db.PayrollRuns.Add(frozen);
            await db.SaveChangesAsync();
            return frozen.Id;
        });
        host.Clock.Now = new DateTimeOffset(2026, 12, 2, 6, 0, 0, TimeSpan.Zero);

        var summaries = await host.Job.RunOnceAsync();

        Assert.Equal((1, 0, 1, 1), (summaries[tenant].Due, summaries[tenant].Applied, summaries[tenant].Blocked, summaries[tenant].BlockedTotal));
        var blocked = await host.RequestAsync(tenant, request);
        Assert.Equal(PayProfileChangeState.Scheduled, blocked.State);
        Assert.Contains("2026/12", blocked.LastApplyError!, StringComparison.Ordinal);
        Assert.Matches(@"\p{IsArabic}", blocked.LastApplyError!);
        Assert.Single(await host.ProfilesAsync(tenant, employee));
        Assert.Equal(1, PayProfileChangeMetrics.BlockedOf(tenant));
        Assert.True(PayProfileChangeMetrics.BlockedCount >= 1);

        // The run is reversed: the next cycle applies the request, and the gauge of the agency returns to zero.
        await host.AsAsync(tenant, async sp =>
        {
            var db = sp.GetRequiredService<PayrollDbContext>();
            ForTesting.SetStatus(await db.PayrollRuns.SingleAsync(r => r.Id == run), PayrollRunStatus.Reversed);
            return await db.SaveChangesAsync();
        });
        var after = await host.Job.RunOnceAsync();
        Assert.Equal((1, 0), (after[tenant].Applied, after[tenant].BlockedTotal));
        Assert.Equal(0, PayProfileChangeMetrics.BlockedOf(tenant));
        var applied = await host.RequestAsync(tenant, request);
        Assert.Equal((PayProfileChangeState.Applied, (string?)null), (applied.State, applied.LastApplyError));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Each_agencys_request_is_applied_in_its_own_agency_only_and_one_agencys_failure_does_not_hide_the_others()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        await using var host = await StartAsync();
        var (agencyA, agencyB, agencyC) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var (employeeA, requestA) = await host.ScheduledAsync(agencyA, December, 5100m);
        var (employeeB, requestB) = await host.ScheduledAsync(agencyB, December, 5200m);
        var (employeeC, requestC) = await host.ScheduledAsync(agencyC, December, 5300m);

        // The job finds the three agencies in the audit log, which every request's save wrote into.
        var tenants = await host.AsAsync(agencyA, sp => PayProfileChangeActivationJob.FindTenantsAsync(sp.GetRequiredService<PayrollDbContext>()));
        Assert.Equal(new[] { agencyA, agencyB, agencyC }.Order(), tenants.Order());

        // Agency B's new version cannot be written (a planted failure in the database, for that agency only).
        await ScalarAsync<object?>(host.Admin,
            $$"""
              CREATE FUNCTION iam053_refuse_b() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN
                IF NEW."TenantId" = '{{agencyB}}' THEN RAISE EXCEPTION 'planted failure for one agency'; END IF; RETURN NEW; END $f$;
              CREATE TRIGGER iam053_refuse_b BEFORE INSERT ON "EmployeePayProfiles" FOR EACH ROW EXECUTE FUNCTION iam053_refuse_b();
              SELECT 1
              """);
        host.Clock.Now = FirstOfDecemberInDamascus;

        var summaries = await host.Job.RunOnceAsync();

        // A and C were applied, each in its own agency: the new version carries the agency of its request.
        foreach (var (tenant, employee, request, salary) in new[] { (agencyA, employeeA, requestA, 5100m), (agencyC, employeeC, requestC, 5300m) })
        {
            Assert.Equal((1, 1), (summaries[tenant].Due, summaries[tenant].Applied));
            Assert.Equal(PayProfileChangeState.Applied, (await host.RequestAsync(tenant, request)).State);
            var profiles = await host.ProfilesAsync(tenant, employee);
            Assert.Equal(2, profiles.Count);
            Assert.All(profiles, p => Assert.Equal(tenant, p.TenantId));
            Assert.Equal(salary, profiles[1].BaseSalary);
        }

        // B stayed scheduled, with a reason that names no amount and no internal detail.
        Assert.Equal((1, 0, 1), (summaries[agencyB].Due, summaries[agencyB].Applied, summaries[agencyB].Blocked));
        var failed = await host.RequestAsync(agencyB, requestB);
        Assert.Equal(PayProfileChangeState.Scheduled, failed.State);
        Assert.Equal(PayProfileChangeMessages.ApplyFailed, failed.LastApplyError);
        Assert.Single(await host.ProfilesAsync(agencyB, employeeB));

        // Row security holds across the cycle: agency A sees neither B's request nor C's profiles.
        Assert.Equal(1, await host.AsAsync(agencyA, sp => sp.GetRequiredService<PayrollDbContext>().PayProfileChangeRequests.IgnoreQueryFilters().CountAsync()));
        Assert.Equal(2, await host.AsAsync(agencyA, sp => sp.GetRequiredService<PayrollDbContext>().EmployeePayProfiles.IgnoreQueryFilters().CountAsync()));
        Assert.Empty(await host.ProfilesAsync(agencyA, employeeC));

        // The cause is removed: the next cycle applies B's request too.
        await ScalarAsync<object?>(host.Admin, """DROP TRIGGER iam053_refuse_b ON "EmployeePayProfiles"; SELECT 1""");
        var retry = await host.Job.RunOnceAsync();
        Assert.Equal(1, retry[agencyB].Applied);
        Assert.Equal((0, 0), (retry[agencyA].Due, retry[agencyC].Due));
        Assert.Equal(PayProfileChangeState.Applied, (await host.RequestAsync(agencyB, requestB)).State);
        Assert.All(await host.ProfilesAsync(agencyB, employeeB), p => Assert.Equal(agencyB, p.TenantId));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_replica_that_does_not_hold_the_lock_runs_no_cycle()
    {
        Skip.IfNot(PostgreSqlFixture.IsEnabled, "Set RUN_TESTCONTAINERS=true and start Docker.");
        await using var host = await StartAsync();
        var tenant = Guid.NewGuid();
        var (employee, request) = await host.ScheduledAsync(tenant, December);
        host.Clock.Now = FirstOfDecemberInDamascus;

        // Another replica holds the cycle's advisory lock.
        await using (var other = new NpgsqlConnection(host.App))
        {
            await other.OpenAsync();
            await using (var take = new NpgsqlCommand($"SELECT pg_advisory_lock({AdvisoryLockKeys.PayProfileChangeActivation})", other))
                await take.ExecuteScalarAsync();

            Assert.Empty(await host.Job.RunOnceAsync());
            Assert.Equal(PayProfileChangeState.Scheduled, (await host.RequestAsync(tenant, request)).State);
            Assert.Single(await host.ProfilesAsync(tenant, employee));

            // Released explicitly: a pooled connection keeps its session, and with it a session-level lock, after it is disposed.
            await using var release = new NpgsqlCommand($"SELECT pg_advisory_unlock({AdvisoryLockKeys.PayProfileChangeActivation})", other);
            Assert.True((bool)(await release.ExecuteScalarAsync())!);
        }

        // The lock is free again: this replica runs the cycle.
        Assert.Equal(1, (await host.Job.RunOnceAsync())[tenant].Applied);
        Assert.Equal(4_001_013, AdvisoryLockKeys.PayProfileChangeActivation);
    }
}
