using System.Diagnostics;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Attendance;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-57 (§1 rules 6 and 7, §3 rule 5) on PostgreSQL, as the application role under forced row security, with HR.Core's real bus
/// outbox (the delivery service is not started, so what a save staged stays in <c>OutboxMessage</c> to be counted):
/// <list type="bullet">
/// <item>a close writes its summaries, its closure and one outbox message per summary in one save;</item>
/// <item>a failure injected after the events are staged leaves no summary, no period, no closure and no outbox message;</item>
/// <item>two concurrent closes meet on the summary's unique index, and the loser writes nothing;</item>
/// <item>two approvals for one summary at once: the second meets the summary's <c>xmin</c>, starts again and writes the next version;</item>
/// <item>the close of 2,000 employees over a month of 31 days takes under 10 seconds.</item>
/// </list>
/// </summary>
[Collection("PgRabbit")]
public sealed class Lva057AttendanceCloseIntegrationTests(PgRabbitFixture fixture, ITestOutputHelper output)
{
    private static readonly Guid Tenant = Guid.Parse("a0000057-0000-4000-8000-0000000000c1");
    private static readonly DateOnly March = new(2025, 3, 1);
    private static readonly DateOnly MarchEnd = new(2025, 3, 31);
    private const string Reason = "كان في مهمة رسمية موثقة بكتاب الإدارة";

    private sealed record Host(string Admin, ServiceProvider Provider, IBusControl Bus, MeetBeforeSave Meeting, Guid Unit) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Bus.StopAsync();
            await Provider.DisposeAsync();
        }
    }

    /// <summary>Holds the saves that write a summary until the expected number of them have arrived, so that they really meet.</summary>
    private sealed class MeetBeforeSave : SaveChangesInterceptor
    {
        private TaskCompletionSource _all = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;
        private int _parties;

        public void Expect(int parties)
        {
            _all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _arrived = 0;
            _parties = parties;
        }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var writesASummary = eventData.Context!.ChangeTracker.Entries<AttendanceEmployeeSummary>()
                .Any(e => e.State is EntityState.Added or EntityState.Modified);
            if (_parties > 0 && writesASummary)
            {
                if (Interlocked.Increment(ref _arrived) >= _parties)
                    _all.TrySetResult();
                // A save that arrives after the meeting (a retry) passes at once; a missing party is not waited for for ever.
                await Task.WhenAny(_all.Task, Task.Delay(TimeSpan.FromSeconds(15), cancellationToken));
            }

            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private async Task<Host> StartAsync()
    {
        var admin = await Pol047Pg.MigratedAsync(fixture.Postgres, "lva057");
        var app = await Pol047Pg.ForcedAppAsync(fixture.Postgres, admin);
        var unit = Guid.NewGuid();
        await using (var seed = Pol047Pg.AdminContext(admin))
        {
            seed.OrgUnits.Add(new OrgUnit { Id = unit, TenantId = Tenant, Code = "LVA57", NameAr = "وحدة الإغلاق", MaterializedPath = $"/{unit:D}/" });
            await seed.SaveChangesAsync();
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = fixture.Rabbit.Hostname,
            ["RabbitMQ:Port"] = fixture.Rabbit.Port.ToString(),
            ["RabbitMQ:VirtualHost"] = "/",
            ["RabbitMQ:Username"] = RabbitMqFixture.Username,
            ["RabbitMQ:Password"] = RabbitMqFixture.Password,
            ["RabbitMQ:UseQuorumQueues"] = "false",
            ["RabbitMQ:PublisherConfirmation"] = "false",
            ["MassTransit:RetryCount"] = "0"
        }).Build();

        var meeting = new MeetBeforeSave();
        var tenant = new Msg040FixedTenant(Tenant);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(config);
        services.AddSingleton<ITenantContext>(tenant);
        services.AddDbContext<HRCoreDbContext>(o => o
            .UseNpgsql(app, n => n.MigrationsAssembly(typeof(HRCoreDbContext).Assembly.GetName().Name))
            .AddInterceptors(new TenantIdStampInterceptor(tenant), new NpgsqlTenantSessionInterceptor(tenant), meeting));
        services.AddHrAttendance(config);
        services.AddServiceMassTransit(config, configureOutbox: x => x.AddPostgresOutbox<HRCoreDbContext>(), enableInboxFilter: false);

        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        return new Host(admin, provider, bus, meeting, unit);
    }

    private static AttendanceActor Central(Guid? user = null) => new(user ?? Guid.NewGuid(), OrgAccessScope.Unrestricted(), "مستخدم مركزي");

    /// <summary>Employees of the unit, each with a record on every day of March 2025 (rest days included, as the old bulk entry wrote).</summary>
    private static async Task<List<Guid>> SeedEmployeesWithAMonthAsync(Host host, int count, int absentOnFourth = 0)
    {
        var ids = new List<Guid>(count);
        await using (var seed = Pol047Pg.AdminContext(host.Admin))
        {
            for (var i = 0; i < count; i++)
            {
                var id = Guid.NewGuid();
                ids.Add(id);
                seed.Employees.Add(new Employee
                {
                    Id = id,
                    TenantId = Tenant,
                    FirstName = "موظف",
                    LastName = $"إغلاق {i}",
                    FullName = $"موظف إغلاق {i}",
                    NationalId = "N" + id.ToString("N")[..10],
                    BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    Gender = Gender.Male,
                    CurrentStatus = EmploymentStatus.Active,
                    PrimaryOrgUnitId = host.Unit,
                    HireDate = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                });
            }

            await seed.SaveChangesAsync();
        }

        await Pol047Pg.ExecAsync(host.Admin, $"""
            INSERT INTO "DailyAttendanceRecords" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","AttendanceDate","Status","Source")
            SELECT gen_random_uuid(), '{Tenant}', now(), now(), e."Id", d::date, 0, 1
            FROM "Employees" e CROSS JOIN generate_series('2025-03-01'::date, '2025-03-31'::date, interval '1 day') d
            WHERE e."TenantId" = '{Tenant}';
            """);
        if (absentOnFourth > 0)
        {
            var absent = string.Join(",", ids.Take(absentOnFourth).Select(id => $"'{id}'"));
            await Pol047Pg.ExecAsync(host.Admin,
                $"""UPDATE "DailyAttendanceRecords" SET "Status" = 1 WHERE "AttendanceDate" = '2025-03-04' AND "EmployeeId" IN ({absent});""");
        }

        return ids;
    }

    private static Task<long> CountAsync(Host host, string table) =>
        Pol047Pg.ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "{table}" """);

    private static Task<long> OutboxAsync(Host host) =>
        Pol047Pg.ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "OutboxMessage" WHERE "MessageType" LIKE '%IAttendanceSummaryPublished%'""");

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_close_writes_its_summaries_its_closure_and_one_outbox_message_each_in_one_save_under_row_security()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var employees = await SeedEmployeesWithAMonthAsync(host, 3, absentOnFourth: 1);

        AttendanceCloseResult result;
        await using (var scope = host.Provider.CreateAsyncScope())
            result = await scope.ServiceProvider.GetRequiredService<AttendancePeriodService>().CloseAsync(March, host.Unit, Central());

        Assert.True(result.Ok, result.Error);
        Assert.Equal((3, 0, AttendancePeriodStatus.Closed), (result.ClosedEmployees, result.SkippedAlreadyClosed, result.Status!.Value));
        Assert.Equal(1, await CountAsync(host, "AttendancePeriods"));
        Assert.Equal(3, await CountAsync(host, "AttendanceEmployeeSummaries"));
        Assert.Equal(1, await CountAsync(host, "AttendancePeriodClosures"));
        Assert.Equal(3, await OutboxAsync(host));

        // Working days only: 31 records each, 22 working days; the employee with an absence on Tuesday 4 March has one.
        Assert.Equal(3, await Pol047Pg.ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "AttendanceEmployeeSummaries" WHERE "WorkingDays" = 22 AND "Version" = 1"""));
        Assert.Equal(1, await Pol047Pg.ScalarAsync<long>(host.Admin,
            $"""SELECT count(*) FROM "AttendanceEmployeeSummaries" WHERE "EmployeeId" = '{employees[0]}' AND "AbsentDays" = 1 AND "PresentDays" = 21"""));
        // The tenant is on every row, and on the message itself.
        Assert.Equal(3, await Pol047Pg.ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "AttendanceEmployeeSummaries" WHERE "TenantId" = '{Tenant}'"""));
        output.WriteLine("LVA057-BODY " + await Pol047Pg.ScalarAsync<string>(host.Admin,
            """SELECT "Body" FROM "OutboxMessage" WHERE "MessageType" LIKE '%IAttendanceSummaryPublished%' ORDER BY "SequenceNumber" LIMIT 1"""));
        Assert.Equal(3, await Pol047Pg.ScalarAsync<long>(host.Admin,
            $"""SELECT count(*) FROM "OutboxMessage" WHERE "MessageType" LIKE '%IAttendanceSummaryPublished%' AND "Body" ILIKE '%{Tenant}%'"""));
        Assert.Equal(3, await Pol047Pg.ScalarAsync<long>(host.Admin,
            """SELECT count(*) FROM "OutboxMessage" WHERE "MessageType" LIKE '%IAttendanceSummaryPublished%' AND "Body" ~* '"version"\s*:\s*1[,}\s]'"""));

        // A second close of the same scope writes nothing.
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var again = await scope.ServiceProvider.GetRequiredService<AttendancePeriodService>().CloseAsync(March, host.Unit, Central());
            Assert.Equal((true, 0, 3), (again.Ok, again.ClosedEmployees, again.SkippedAlreadyClosed));
        }

        Assert.Equal(1, await CountAsync(host, "AttendancePeriodClosures"));
        Assert.Equal(3, await OutboxAsync(host));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_failure_after_the_events_are_staged_leaves_no_summary_no_period_no_closure_and_no_outbox_message()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await SeedEmployeesWithAMonthAsync(host, 3);
        // The failure is inside the save itself, after the summaries and their messages were sent to the database: the closure row,
        // written in the same save, is refused by a trigger.
        await Pol047Pg.ExecAsync(host.Admin, """
            CREATE FUNCTION lva057_refuse_closure() RETURNS trigger AS $$ BEGIN RAISE EXCEPTION 'lva057: injected failure'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER lva057_refuse_closure BEFORE INSERT ON "AttendancePeriodClosures" FOR EACH ROW EXECUTE FUNCTION lva057_refuse_closure();
            """);

        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<AttendancePeriodService>();
            var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() => service.CloseAsync(March, host.Unit, Central()));
            Assert.Contains("injected failure", failure.InnerException?.Message ?? failure.Message);
        }

        Assert.Equal(0, await CountAsync(host, "AttendanceEmployeeSummaries"));
        Assert.Equal(0, await CountAsync(host, "AttendancePeriods"));
        Assert.Equal(0, await CountAsync(host, "AttendancePeriodClosures"));
        Assert.Equal(0, await CountAsync(host, "OutboxMessage"));

        // With the failure gone, the same close does the whole work.
        await Pol047Pg.ExecAsync(host.Admin, """DROP TRIGGER lva057_refuse_closure ON "AttendancePeriodClosures";""");
        await using (var scope = host.Provider.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<AttendancePeriodService>().CloseAsync(March, host.Unit, Central())).Ok);
        Assert.Equal(3, await CountAsync(host, "AttendanceEmployeeSummaries"));
        Assert.Equal(3, await OutboxAsync(host));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_concurrent_closes_meet_on_the_summarys_unique_index_and_the_loser_writes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await SeedEmployeesWithAMonthAsync(host, 6);
        host.Meeting.Expect(2);

        async Task<AttendanceCloseResult> CloseAsync()
        {
            await using var scope = host.Provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AttendancePeriodService>().CloseAsync(March, host.Unit, Central());
        }

        // Both read «no summary yet», both reach their save, and only then are let through.
        var results = await Task.WhenAll(Task.Run(CloseAsync), Task.Run(CloseAsync));

        var winner = Assert.Single(results, r => r.Ok);
        var loser = Assert.Single(results, r => !r.Ok);
        Assert.Equal(6, winner.ClosedEmployees);
        Assert.Equal((AttendanceCloseOutcome.ConcurrentClose, AttendancePeriodMessages.Concurrent), (loser.Outcome, loser.Error));
        // No partial effect: one period, one closure, one summary and one message per employee.
        Assert.Equal(1, await CountAsync(host, "AttendancePeriods"));
        Assert.Equal(6, await CountAsync(host, "AttendanceEmployeeSummaries"));
        Assert.Equal(1, await CountAsync(host, "AttendancePeriodClosures"));
        Assert.Equal(6, await OutboxAsync(host));
        Assert.Equal(6, await Pol047Pg.ScalarAsync<long>(host.Admin, """SELECT count(DISTINCT "EmployeeId") FROM "AttendanceEmployeeSummaries" """));

        // The loser's retry finds everyone closed.
        host.Meeting.Expect(0);
        var retry = await CloseAsync();
        Assert.Equal((true, 0, 6), (retry.Ok, retry.ClosedEmployees, retry.SkippedAlreadyClosed));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_approvals_for_one_summary_at_once_write_two_versions_through_the_xmin_retry()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var employee = (await SeedEmployeesWithAMonthAsync(host, 1, absentOnFourth: 1)).Single();
        await using (var scope = host.Provider.CreateAsyncScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<AttendancePeriodService>().CloseAsync(March, host.Unit, Central())).Ok);

        // Two requests for two days of the same employee and month, by one requester.
        var requester = Central();
        var ids = new List<Guid>();
        foreach (var (day, status) in new[] { (new DateOnly(2025, 3, 4), DailyAttendanceStatus.Present), (new DateOnly(2025, 3, 5), DailyAttendanceStatus.Absent) })
        {
            await using var scope = host.Provider.CreateAsyncScope();
            var requested = await scope.ServiceProvider.GetRequiredService<AttendanceCorrectionService>().RequestAsync(requester, employee, day, status, Reason);
            Assert.True(requested.Ok, requested.Error);
            ids.Add(requested.Correction!.Id);
        }

        host.Meeting.Expect(2);
        async Task<AttendanceCorrectionResult> ApproveAsync(Guid id)
        {
            await using var scope = host.Provider.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<AttendanceCorrectionService>().ApproveAsync(Central(), id, "معتمد");
        }

        // Both read version 1 and reach their save together: one writes version 2; the other meets the summary's xmin, starts again
        // from what is stored, and writes version 3.
        var results = await Task.WhenAll(Task.Run(() => ApproveAsync(ids[0])), Task.Run(() => ApproveAsync(ids[1])));

        Assert.All(results, r => Assert.True(r.Ok, r.Error));
        Assert.Equal(new int?[] { 2, 3 }, results.Select(r => r.Correction!.ResultingVersion).Order().ToArray());
        // The final summary is what both corrections give together: 4 March present again, 5 March absent.
        Assert.Equal(1, await Pol047Pg.ScalarAsync<long>(host.Admin,
            $"""SELECT count(*) FROM "AttendanceEmployeeSummaries" WHERE "EmployeeId" = '{employee}' AND "Version" = 3 AND "AbsentDays" = 1 AND "PresentDays" = 21"""));
        Assert.Equal(2, await Pol047Pg.ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "AttendanceCorrections" WHERE "Status" = 1"""));
        Assert.Equal("2,3", await Pol047Pg.ScalarAsync<string>(host.Admin,
            """SELECT string_agg("ResultingVersion"::text, ',' ORDER BY "ResultingVersion") FROM "AttendanceCorrections" """));
        // One message for the close and one for each version, no more: the attempt that lost wrote none.
        Assert.Equal(3, await OutboxAsync(host));
        Assert.Equal(1, await Pol047Pg.ScalarAsync<long>(host.Admin,
            $"""SELECT count(*) FROM "DailyAttendanceRecords" WHERE "EmployeeId" = '{employee}' AND "AttendanceDate" = '2025-03-04' AND "Status" = 0 AND "Source" = 3"""));
        Assert.Equal(1, await Pol047Pg.ScalarAsync<long>(host.Admin,
            $"""SELECT count(*) FROM "DailyAttendanceRecords" WHERE "EmployeeId" = '{employee}' AND "AttendanceDate" = '2025-03-05' AND "Status" = 1 AND "Source" = 3"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_close_of_2000_employees_over_31_days_takes_under_10_seconds()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await SeedEmployeesWithAMonthAsync(host, 2_000, absentOnFourth: 200);
        Assert.Equal(62_000, await CountAsync(host, "DailyAttendanceRecords"));

        AttendanceCloseResult result;
        var watch = Stopwatch.StartNew();
        await using (var scope = host.Provider.CreateAsyncScope())
            result = await scope.ServiceProvider.GetRequiredService<AttendancePeriodService>().CloseAsync(March, null, Central());
        watch.Stop();

        output.WriteLine(FormattableString.Invariant($"LVA057-PERF close of 2,000 employees x 31 days: {watch.Elapsed.TotalSeconds:0.00} s"));
        Assert.True(result.Ok, result.Error);
        Assert.Equal((2_000, AttendancePeriodStatus.Closed), (result.ClosedEmployees, result.Status!.Value));
        Assert.Equal(2_000, await CountAsync(host, "AttendanceEmployeeSummaries"));
        Assert.Equal(2_000, await OutboxAsync(host));
        Assert.Equal(200, await Pol047Pg.ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "AttendanceEmployeeSummaries" WHERE "AbsentDays" = 1"""));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), FormattableString.Invariant($"the close took {watch.Elapsed.TotalSeconds:0.00} s"));

        // The lock of a month for 2,000 employees is one query too.
        var lockWatch = Stopwatch.StartNew();
        await using (var scope = host.Provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HRCoreDbContext>();
            var ids = await db.Employees.AsNoTracking().Select(e => e.Id).ToListAsync();
            var locked = await scope.ServiceProvider.GetRequiredService<AttendanceLockService>().GetLockedDaysAsync(ids, March, MarchEnd);
            Assert.True(locked.IsLocked(ids[0], new DateOnly(2025, 3, 15)));
            Assert.False(locked.IsLocked(ids[0], new DateOnly(2025, 4, 1)));
        }

        lockWatch.Stop();
        output.WriteLine(FormattableString.Invariant($"LVA057-PERF lock lookup for 2,000 employees: {lockWatch.Elapsed.TotalSeconds:0.00} s"));
    }
}
