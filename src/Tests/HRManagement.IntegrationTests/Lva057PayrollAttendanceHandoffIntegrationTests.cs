using System.Data.Common;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Tenancy;
using MassTransit;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Payroll.Application.Features.PayrollRuns;
using Payroll.Application.Features.PayrollRuns.Lifecycle;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Infrastructure.Consumers;
using Payroll.Infrastructure.Data;
using Payroll.Infrastructure.Migrations;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-57 (§4, §5) on PostgreSQL, as the non-superuser application role under forced row security, with Payroll's tenant and audit
/// wiring: a duplicate and a reverse order of versions; two versions of one summary at once on one receipt (the row lock: neither
/// computes its difference from the other's base, with and without a receipt in place); the tenant on the receipt and the row, and a
/// message of another agency refused; the carry past a frozen month and the refund, through real calculations; the gate in
/// <c>Block</c>; the freeze read in every run state; and <c>LVA057_AttendanceHandoff</c> as the migrator would run it.
/// </summary>
[Collection("PgRabbit")]
public sealed class Lva057PayrollAttendanceHandoffIntegrationTests(PgRabbitFixture fixture)
{
    private static readonly Guid Agency = Guid.Parse("57000000-0000-4000-8000-00000000000a");
    private static readonly Guid OtherAgency = Guid.Parse("57000000-0000-4000-8000-00000000000b");
    private static readonly Guid Approver = Guid.Parse("57000000-0000-4000-8000-0000000000c2");

    /// <summary>Holds the first transaction that locked a receipt for a moment, so that a second one really meets the lock.</summary>
    private sealed class HoldAfterLock : DbCommandInterceptor
    {
        private int _held;

        public TimeSpan Hold { get; set; } = TimeSpan.Zero;

        public void Arm(TimeSpan hold)
        {
            Hold = hold;
            _held = 0;
        }

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (Hold > TimeSpan.Zero && command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal) && Interlocked.Exchange(ref _held, 1) == 0)
                await Task.Delay(Hold, cancellationToken);
            return await base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed record Host(ServiceProvider Provider, IBusControl Bus, string Admin, string App, HoldAfterLock Lock) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Bus.StopAsync();
            await Provider.DisposeAsync();
        }

        public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
        {
            await using var scope = Provider.CreateAsyncScope();
            return await work(scope.ServiceProvider);
        }

        public Task<T> SendAsync<T>(IRequest<T> command) => InScopeAsync(sp => sp.GetRequiredService<IMediator>().Send(command));

        public Task<T> DbAsync<T>(Func<PayrollDbContext, Task<T>> read) => InScopeAsync(sp => read(sp.GetRequiredService<PayrollDbContext>()));

        /// <summary>One version received, as the consumer does it: its own unit of work.</summary>
        public Task<AttendanceHandoffOutcome> ReceiveAsync(IAttendanceSummaryPublished message) =>
            DbAsync(db => new AttendanceSummaryHandoff(db).ApplyAsync(message));
    }

    private async Task<Host> StartAsync(PayrollAttendanceGateMode mode = PayrollAttendanceGateMode.Warn)
    {
        var name = TestDatabaseNames.Unique("lva057_pay_");
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
            ["Payroll:Attendance:Gate"] = mode.ToString(),
        }).Build();

        var hold = new HoldAfterLock();
        var services = new ServiceCollection();
        services.AddLogging();
        PayrollIntegrationTestSetup.AddPayrollConsumerServices(services);
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
                hold));
        services.AddServiceMassTransit(config, configureOutbox: x => x.AddPostgresOutbox<PayrollDbContext>(), enableInboxFilter: false);
        services.PostConfigure<PayrollBatchOptions>(o => o.MaxDegreeOfParallelism = 1);

        var provider = services.BuildServiceProvider();
        var bus = provider.GetRequiredService<IBusControl>();
        await bus.StartAsync();
        var host = new Host(provider, bus, admin, app, hold);
        await host.DbAsync(async db =>
        {
            await PayrollPostgresTestSupport.SeedPublishedConfigAsync(db);
            return 0;
        });

        // The control of every test here: the application role is neither superuser nor exempt from row security.
        Assert.False(await ScalarAsync<bool>(app, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));
        Assert.Equal(mode, provider.GetRequiredService<PayrollAttendanceGate>().Mode);
        return host;
    }

    private static async Task<T> ScalarAsync<T>(string connection, string sql)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var command = new NpgsqlCommand(sql, c);
        return (T)Convert.ChangeType((await command.ExecuteScalarAsync())!, typeof(T), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static async Task ExecAsync(string connection, string sql)
    {
        await using var c = new NpgsqlConnection(connection);
        await c.OpenAsync();
        await using var command = new NpgsqlCommand(sql, c);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<Guid> EmployeeAsync(Host host)
    {
        var id = Guid.NewGuid();
        await host.DbAsync(async db =>
        {
            db.EmployeePayProfiles.Add(TestDataFactory.CreateActivePayProfile(id, 900_000m));
            return await db.SaveChangesAsync();
        });
        return id;
    }

    private static Lva057Summary Close(Guid employee, int month, int absentDays) =>
        Lva057Summary.Close(employee, 2028, month, absentDays, tenant: Agency);

    private static async Task<Guid> CalculatedRunAsync(Host host, int month)
    {
        var start = new DateTime(2028, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var (created, id, error) = await host.SendAsync(new CreatePayrollRunCommand(start, start.AddMonths(1).AddDays(-1), start.AddMonths(1)));
        Assert.True(created, error);
        var calculated = await host.SendAsync(new CalculatePayrollRunCommand(id!.Value));
        Assert.True(calculated.Success, calculated.Error);
        return id.Value;
    }

    private static async Task<Guid> UnderReviewRunAsync(Host host, int month)
    {
        var run = await CalculatedRunAsync(host, month);
        var submitted = await host.SendAsync(new SubmitPayrollRunForReviewCommand(run));
        Assert.True(submitted.Success, submitted.Error);
        return run;
    }

    /// <summary>A summary's rows as «version:days:carried», and its receipt as «latest|absent|recorded», read by the superuser.</summary>
    private static async Task<(string Rows, string Receipt)> StateAsync(Host host, Guid summary) =>
        (await ScalarAsync<string>(host.Admin, $"""
            SELECT coalesce(string_agg("SourceVersion" || ':' || "AbsentDays" || ':' || "CarryForward", ',' ORDER BY "SourceVersion"), '')
            FROM "AbsenceDeductions" WHERE "SourceReference" LIKE 'ATT:{summary}:%'
            """),
         await ScalarAsync<string>(host.Admin, $"""
            SELECT coalesce((SELECT "LatestVersion" || '|' || "LatestAbsentDays" || '|' || "RecordedAbsentDays" FROM "AttendanceSummaryReceipts" WHERE "SummaryId" = '{summary}'), '')
            """));

    // ---------------------------------------------------------------- versions

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_duplicate_and_a_reverse_order_of_versions_end_at_the_latest_versions_absence()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var v1 = Close(await EmployeeAsync(host), 1, absentDays: 2);
        var v2 = v1.Next(absentDays: 1);
        var v3 = v2.Next(absentDays: 3);

        Assert.Equal(AttendanceHandoffOutcome.Recorded, await host.ReceiveAsync(v1));
        Assert.Equal(AttendanceHandoffOutcome.Duplicate, await host.ReceiveAsync(v1));
        // v3 arrives before v2: the latest wins, and v2 is ignored when it comes.
        Assert.Equal(AttendanceHandoffOutcome.Recorded, await host.ReceiveAsync(v3));
        Assert.Equal(AttendanceHandoffOutcome.IgnoredOlderVersion, await host.ReceiveAsync(v2));
        Assert.Equal(AttendanceHandoffOutcome.Duplicate, await host.ReceiveAsync(v3));

        Assert.Equal(("1:2:false,3:1:false", "3|3|3"), await StateAsync(host, v1.SummaryId));
        // The unique key is in the database too: a second row of a recorded version cannot be written by any path.
        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecAsync(host.Admin, $"""
            INSERT INTO "AbsenceDeductions" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","PeriodStart","PeriodEnd","AbsentDays","Amount","SourceReference","CarryForward")
            VALUES (gen_random_uuid(),'{Agency}',now(),now(),'{v1.EmployeeId}','2028-01-01','2028-01-31',2,0,'ATT:{v1.SummaryId}:v1',false);
            """));
        Assert.Equal("23505", duplicate.SqlState);
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Two_versions_of_one_summary_at_once_never_compute_from_the_same_base()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var v1 = Close(await EmployeeAsync(host), 1, absentDays: 2);
        await host.ReceiveAsync(v1);
        var v2 = v1.Next(absentDays: 5);
        var v3 = v2.Next(absentDays: 1);

        // v2 locks the receipt and is held; v3 starts meanwhile and waits for the row. Without the lock both would compute against
        // version 1's two days (+3 and −1), and the rows would add up to 4 where the latest version says 1.
        host.Lock.Arm(TimeSpan.FromMilliseconds(1500));
        var first = Task.Run(() => host.ReceiveAsync(v2));
        await Task.Delay(300);
        var second = Task.Run(() => host.ReceiveAsync(v3));
        var outcomes = await Task.WhenAll(first, second);

        // Whichever took the lock first, the rows add up to the latest version's absence: v2 then v3 (+3, −4), or v3 then v2 ignored (−1).
        var (rows, receipt) = await StateAsync(host, v1.SummaryId);
        Assert.Equal("3|1|1", receipt);
        Assert.Contains(rows, new[] { "1:2:false,2:3:false,3:-4:false", "1:2:false,3:-1:false" });
        Assert.Equal(
            rows.Contains("2:3", StringComparison.Ordinal) ? [AttendanceHandoffOutcome.Recorded, AttendanceHandoffOutcome.Recorded] : [AttendanceHandoffOutcome.IgnoredOlderVersion, AttendanceHandoffOutcome.Recorded],
            outcomes);
        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""SELECT sum("AbsentDays") FROM "AbsenceDeductions" WHERE "SourceReference" LIKE 'ATT:{v1.SummaryId}:%'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_first_two_versions_at_once_share_one_receipt()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var v1 = Close(await EmployeeAsync(host), 1, absentDays: 2);
        var v2 = v1.Next(absentDays: 3);

        // No receipt exists yet: both create it (one insert wins, the other finds it) and then take the lock in turn.
        host.Lock.Arm(TimeSpan.FromMilliseconds(1500));
        var first = Task.Run(() => host.ReceiveAsync(v1));
        await Task.Delay(300);
        var second = Task.Run(() => host.ReceiveAsync(v2));
        await Task.WhenAll(first, second);

        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "AttendanceSummaryReceipts" WHERE "SummaryId" = '{v1.SummaryId}'"""));
        var (rows, receipt) = await StateAsync(host, v1.SummaryId);
        Assert.Equal("2|3|3", receipt);
        // Either order of arrival is right: v1 then the difference of v2, or v2 alone with v1 ignored.
        Assert.Contains(rows, new[] { "1:2:false,2:1:false", "2:3:false" });
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_tenant_on_the_receipt_and_the_row_is_the_messages_and_another_agencys_message_writes_nothing()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var mine = Close(Guid.NewGuid(), 1, absentDays: 1);

        await host.ReceiveAsync(mine);

        Assert.Equal(Agency, await ScalarAsync<Guid>(host.Admin, $"""SELECT "TenantId" FROM "AttendanceSummaryReceipts" WHERE "SummaryId" = '{mine.SummaryId}'"""));
        Assert.Equal(Agency, await ScalarAsync<Guid>(host.Admin, $"""SELECT "TenantId" FROM "AbsenceDeductions" WHERE "SourceReference" = 'ATT:{mine.SummaryId}:v1'"""));

        // A message that names another agency, consumed in this agency's session: row security refuses the write, whole.
        var foreign = Lva057Summary.Close(Guid.NewGuid(), 2028, 1, 4, tenant: OtherAgency);
        var refused = await Assert.ThrowsAnyAsync<Exception>(() => host.ReceiveAsync(foreign));
        var states = new List<string>();
        for (Exception? e = refused; e is not null; e = e.InnerException)
        {
            if (e is PostgresException pg)
                states.Add(pg.SqlState);
        }

        // 42501: the insert of the receipt broke the policy's WITH CHECK.
        Assert.Contains("42501", states);
        Assert.Equal(0L, await ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "AttendanceSummaryReceipts" WHERE "SummaryId" = '{foreign.SummaryId}'"""));
        Assert.Equal(0L, await ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "AbsenceDeductions" WHERE "SourceReference" LIKE 'ATT:{foreign.SummaryId}:%'"""));

        // The other agency does not see this agency's receipt, as the application role.
        await using var c = new NpgsqlConnection(host.App);
        await c.OpenAsync();
        await using (var set = new NpgsqlCommand("SELECT set_config('app.current_tenant', @t, false)", c))
        {
            set.Parameters.AddWithValue("t", OtherAgency.ToString());
            await set.ExecuteScalarAsync();
        }

        await using var count = new NpgsqlCommand("""SELECT count(*) FROM "AttendanceSummaryReceipts" """, c);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    // ---------------------------------------------------------------- the carry, the refund, the gate

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task A_difference_after_the_freeze_is_carried_to_the_next_cycle_and_a_negative_one_is_refunded()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var late = await EmployeeAsync(host);
        var refunded = await EmployeeAsync(host);
        var lateV1 = Close(late, 1, absentDays: 0);
        var refundedV1 = Close(refunded, 1, absentDays: 2);
        await host.ReceiveAsync(lateV1);
        await host.ReceiveAsync(refundedV1);
        var january = await UnderReviewRunAsync(host, 1);
        var frozen = await ScalarAsync<string>(host.Admin, $"""
            SELECT md5(string_agg("EmployeeId"::text || '|' || "GrossPay"::text || '|' || "TotalDeductions"::text || '|' || "NetPay"::text, ';' ORDER BY "EmployeeId"))
            FROM "SalaryLines" WHERE "PayrollRunId" = '{january}'
            """);

        // After the freeze: one more day of absence for one, and one of the two absences corrected for the other.
        await host.ReceiveAsync(lateV1.Next(absentDays: 1));
        await host.ReceiveAsync(refundedV1.Next(absentDays: 1));
        Assert.Equal("2:1:true", (await StateAsync(host, lateV1.SummaryId)).Rows);
        Assert.Equal("1:2:false,2:-1:true", (await StateAsync(host, refundedV1.SummaryId)).Rows);

        var february = await CalculatedRunAsync(host, 2);

        // February carries both, each with an item that names January; January's lines did not move.
        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""
            SELECT count(*) FROM "SalaryLineComponents" c JOIN "SalaryLines" l ON l."Id" = c."SalaryLineId"
            WHERE l."PayrollRunId" = '{february}' AND l."EmployeeId" = '{late}' AND c."ComponentName" = 'فرق غياب عن فترة 2028-01' AND c."Amount" < 0
            """));
        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""
            SELECT count(*) FROM "SalaryLineComponents" c JOIN "SalaryLines" l ON l."Id" = c."SalaryLineId"
            WHERE l."PayrollRunId" = '{february}' AND l."EmployeeId" = '{refunded}' AND c."ComponentName" = 'استرداد خصم غياب — فترة 2028-01' AND c."Amount" > 0
            """));
        Assert.Equal(2L, await ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "AbsenceDeductions" WHERE "AppliedInRunId" = '{february}' AND "CarryForward" """));
        Assert.Equal(frozen, await ScalarAsync<string>(host.Admin, $"""
            SELECT md5(string_agg("EmployeeId"::text || '|' || "GrossPay"::text || '|' || "TotalDeductions"::text || '|' || "NetPay"::text, ';' ORDER BY "EmployeeId"))
            FROM "SalaryLines" WHERE "PayrollRunId" = '{january}'
            """));
        // The one who was refunded has a lower total of deductions than the one who was charged, by two days' worth.
        var charged = await ScalarAsync<decimal>(host.Admin, $"""SELECT "TotalDeductions" FROM "SalaryLines" WHERE "PayrollRunId" = '{february}' AND "EmployeeId" = '{late}'""");
        var given = await ScalarAsync<decimal>(host.Admin, $"""SELECT "TotalDeductions" FROM "SalaryLines" WHERE "PayrollRunId" = '{february}' AND "EmployeeId" = '{refunded}'""");
        Assert.True(given < charged);

        var status = await host.InScopeAsync(async sp =>
            await sp.GetRequiredService<PayrollAttendanceGate>().StatusAsync(await sp.GetRequiredService<PayrollDbContext>().PayrollRuns.AsNoTracking().SingleAsync(r => r.Id == february)));
        Assert.Equal((2, 0), (status.CarryForwardApplied, status.CarryForwardPending));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Block_refuses_the_calculation_and_the_run_and_the_database_stay_as_they_were()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync(PayrollAttendanceGateMode.Block);
        var closed = await EmployeeAsync(host);
        var missing = await EmployeeAsync(host);
        await host.ReceiveAsync(Close(closed, 1, absentDays: 1));
        var start = new DateTime(2028, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var (created, id, createError) = await host.SendAsync(new CreatePayrollRunCommand(start, start.AddMonths(1).AddDays(-1), start.AddMonths(1)));
        Assert.True(created, createError);
        var before = await ScalarAsync<string>(host.Admin, $"""SELECT "Status" || '|' || "Revision" || '|' || coalesce("PayrollConfigurationId"::text, '-') || '|' || xmin FROM "PayrollRuns" WHERE "Id" = '{id}'""");

        var refused = await host.SendAsync(new CalculatePayrollRunCommand(id!.Value));

        Assert.False(refused.Success);
        Assert.Equal(PayrollAttendanceGate.BlockMessage(1, new DateOnly(2028, 1, 1)), refused.Error);
        // The run's row was not written at all (the same xmin), and nothing else was: no job, no line, no audit record, no applied row.
        Assert.Equal(before, await ScalarAsync<string>(host.Admin, $"""SELECT "Status" || '|' || "Revision" || '|' || coalesce("PayrollConfigurationId"::text, '-') || '|' || xmin FROM "PayrollRuns" WHERE "Id" = '{id}'"""));
        Assert.Equal(0L, await ScalarAsync<long>(host.Admin, """SELECT (SELECT count(*) FROM "PayrollRunJobs") + (SELECT count(*) FROM "SalaryLines") + (SELECT count(*) FROM "PayrollAuditRecords")"""));
        Assert.Equal(0L, await ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "AbsenceDeductions" WHERE "AppliedInRunId" IS NOT NULL"""));

        // The other employee's attendance is closed: the same run calculates, and takes the absence that was waiting.
        await host.ReceiveAsync(Close(missing, 1, absentDays: 0));
        var calculated = await host.SendAsync(new CalculatePayrollRunCommand(id.Value));
        Assert.True(calculated.Success, calculated.Error);
        Assert.Equal(0L, await ScalarAsync<long>(host.Admin, """SELECT count(*) FROM "PayrollAuditRecords" """));
        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "AbsenceDeductions" WHERE "AppliedInRunId" = '{id}'"""));
    }

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task Warn_writes_one_open_audit_record_with_the_calculation_and_never_a_closed_one()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        var closed = await EmployeeAsync(host);
        var missing = await EmployeeAsync(host);
        await host.ReceiveAsync(Close(closed, 1, absentDays: 0));

        var run = await CalculatedRunAsync(host, 1);

        // AuditStatus.Open = 0; the record is the agency's, written under row security by the calculation's first save.
        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""
            SELECT count(*) FROM "PayrollAuditRecords"
            WHERE "PayrollRunId" = '{run}' AND "TenantId" = '{Agency}' AND "ClosedAt" IS NULL AND "ClosedBy" IS NULL
              AND "Findings" LIKE '{PayrollAttendanceGate.WarningPrefix}: 1 %' AND "Findings" LIKE '%{missing}%' AND "Findings" NOT LIKE '%{closed}%'
            """));
        Assert.Equal(1L, await ScalarAsync<long>(host.Admin, $"""SELECT count(*) FROM "PayrollAuditRecords" WHERE "PayrollRunId" = '{run}'"""));
        var status = await host.DbAsync(db => db.PayrollAuditRecords.AsNoTracking().Where(a => a.PayrollRunId == run).Select(a => a.Status).SingleAsync());
        Assert.Equal(AuditStatus.Open, status);
    }

    // ---------------------------------------------------------------- the freeze in every run state

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_freeze_reads_every_state_of_the_periods_regular_run()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        await using var host = await StartAsync();
        await EmployeeAsync(host);
        var january = new DateOnly(2028, 1, 1);
        var end = new DateOnly(2028, 1, 31);
        async Task<(bool Calculated, bool Frozen)> ReadAsync() => await host.DbAsync(async db =>
            (await PayrollAttendanceFreeze.HasCalculatedRunAsync(db.PayrollRuns, january, end),
             await PayrollAttendanceFreeze.IsFrozenAsync(db.PayrollRuns, january, end)));

        Assert.Equal((false, false), await ReadAsync());
        var run = await CalculatedRunAsync(host, 1);
        var runTypeAndStatus = await ScalarAsync<string>(host.Admin, $"""SELECT pg_typeof("Status")::text || '|' || pg_typeof("RunType")::text FROM "PayrollRuns" WHERE "Id" = '{run}'""");

        // Each state of PLAN-56's machine, written on the run's own row (the acts that reach each are PLAN-56's tests).
        var states = new (PayrollRunStatus Status, bool ResultsFrozen, bool Calculated, bool Frozen)[]
        {
            (PayrollRunStatus.Draft, false, false, false),
            (PayrollRunStatus.Calculating, false, true, false),
            (PayrollRunStatus.Calculated, false, true, false),
            (PayrollRunStatus.Calculated, true, true, true),
            (PayrollRunStatus.Validating, false, true, false),
            (PayrollRunStatus.UnderReview, true, true, true),
            (PayrollRunStatus.Approved, true, true, true),
            (PayrollRunStatus.Posted, true, true, true),
            (PayrollRunStatus.Reversed, true, false, false),
            (PayrollRunStatus.Cancelled, false, false, false),
        };
        foreach (var state in states)
        {
            var stored = runTypeAndStatus.StartsWith("integer", StringComparison.Ordinal) ? ((int)state.Status).ToString() : $"'{state.Status}'";
            await ExecAsync(host.Admin, $"""UPDATE "PayrollRuns" SET "Status" = {stored}, "ResultsFrozen" = {state.ResultsFrozen} WHERE "Id" = '{run}'""");
            Assert.True((state.Calculated, state.Frozen) == await ReadAsync(), $"{state.Status} (frozen results: {state.ResultsFrozen})");
            // The consumer's mark is the same reading.
            var close = Close(Guid.NewGuid(), 1, absentDays: 1);
            await host.ReceiveAsync(close);
            Assert.Equal($"1:1:{state.Calculated.ToString().ToLowerInvariant()}", (await StateAsync(host, close.SummaryId)).Rows);
        }

        // A correction run and a final settlement of the same month decide nothing.
        var typeIsText = runTypeAndStatus.EndsWith("text", StringComparison.Ordinal) || runTypeAndStatus.Contains("character", StringComparison.Ordinal);
        foreach (var type in new[] { PayrollRunType.Correction, PayrollRunType.FinalSettlement })
        {
            var storedType = typeIsText ? $"'{type}'" : ((int)type).ToString();
            var posted = runTypeAndStatus.StartsWith("integer", StringComparison.Ordinal) ? ((int)PayrollRunStatus.Posted).ToString() : $"'{PayrollRunStatus.Posted}'";
            await ExecAsync(host.Admin, $"""UPDATE "PayrollRuns" SET "RunType" = {storedType}, "Status" = {posted}, "ResultsFrozen" = true WHERE "Id" = '{run}'""");
            Assert.Equal((false, false), await ReadAsync());
        }
    }

    // ---------------------------------------------------------------- the migration, as the migrator

    [Trait("Category", "Testcontainers")]
    [SkippableFact]
    public async Task The_migration_adds_the_receipts_under_forced_row_security_and_changes_no_absence_row_as_a_non_superuser_owner()
    {
        Skip.IfNot(RealInfra.IsEnabled);
        const string previous = "20261125055305_PAY056_RunLifecycle";
        const string handoff = "20261125221004_LVA057_AttendanceHandoff";
        var admin = await fixture.Postgres.CreateDatabaseAsync(TestDatabaseNames.Unique("lva057_paymig_"));
        static PayrollDbContext Db(string connection) => new(new DbContextOptionsBuilder<PayrollDbContext>().UseNpgsql(connection).Options);
        await using (var db = Db(admin))
        {
            var migrator = db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
            Assert.Contains(previous, db.Database.GetMigrations());
            await migrator.MigrateAsync(previous);
        }

        var role = $"lva057_owner_{Guid.NewGuid():N}"[..24];
        var login = $"lva057-{Guid.NewGuid():N}";
        var database = new NpgsqlConnectionStringBuilder(admin).Database;
        await ExecAsync(admin, $"""
            CREATE ROLE {role} LOGIN PASSWORD '{login}' NOSUPERUSER NOCREATEDB NOCREATEROLE NOBYPASSRLS;
            GRANT CONNECT ON DATABASE "{database}" TO {role};
            GRANT USAGE, CREATE ON SCHEMA public TO {role};
            DO $do$
            DECLARE t record;
            BEGIN
                FOR t IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
                    EXECUTE format('ALTER TABLE public.%I OWNER TO {role}', t.tablename);
                END LOOP;
                FOR t IN SELECT c.table_name FROM information_schema.columns c
                         JOIN pg_tables p ON p.schemaname = 'public' AND p.tablename = c.table_name
                         WHERE c.table_schema = 'public' AND c.column_name = 'TenantId' LOOP
                    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', t.table_name);
                    EXECUTE format('ALTER TABLE public.%I FORCE ROW LEVEL SECURITY', t.table_name);
                END LOOP;
            END
            $do$;
            """);
        var owner = new NpgsqlConnectionStringBuilder(admin) { Username = role, Password = login }.ConnectionString;

        // Rows of before this plan, in two agencies: the old close's, and an unpaid leave's.
        var oldClose = Guid.NewGuid();
        var unpaidLeave = Guid.NewGuid();
        await ExecAsync(admin, $"""
            INSERT INTO "AbsenceDeductions" ("Id","TenantId","CreatedAt","UpdatedAt","EmployeeId","PeriodStart","PeriodEnd","AbsentDays","Amount","Reason","SourceReference")
            VALUES ('{oldClose}','{Agency}',now(),now(),gen_random_uuid(),'2026-03-01','2026-03-31',2,0,'AttendancePeriodClosed:5f0c',NULL),
                   ('{unpaidLeave}','{OtherAgency}',now(),now(),gen_random_uuid(),'2026-03-01','2026-03-31',3,150000,'إجازة بلا أجر','LeaveRequestId=7c1d');
            """);
        const string checksum = """
            SELECT md5(string_agg("Id"::text || '|' || "TenantId"::text || '|' || "AbsentDays"::text || '|' || "Amount"::text || '|' || coalesce("Reason", '') || '|' || coalesce("SourceReference", ''), ';' ORDER BY "Id"))
            FROM "AbsenceDeductions"
            """;
        var before = await ScalarAsync<string>(admin, checksum);
        Assert.False(await ScalarAsync<bool>(owner, "SELECT rolsuper OR rolbypassrls FROM pg_roles WHERE rolname = current_user"));

        await using (var db = Db(owner))
            await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(handoff);

        // The new table: forced row security with the tenant policy, and the unique summary.
        foreach (var table in LVA057_AttendanceHandoff.NewTenantTables)
        {
            Assert.True(await ScalarAsync<bool>(admin, $"""SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = '{table}'"""), table);
            Assert.Equal(1L, await ScalarAsync<long>(admin,
                $"""SELECT count(*) FROM pg_policies WHERE tablename = '{table}' AND policyname = 'tenant_isolation' AND qual IS NOT NULL AND with_check IS NOT NULL"""));
        }

        Assert.Equal(1L, await ScalarAsync<long>(admin,
            """SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_AttendanceSummaryReceipts_SummaryId' AND indexdef LIKE 'CREATE UNIQUE INDEX%'"""));
        Assert.Equal(1L, await ScalarAsync<long>(admin,
            """SELECT count(*) FROM pg_indexes WHERE indexname = 'IX_AbsenceDeductions_EmployeeId_CarryForward_AppliedInRunId'"""));

        // No existing row changed: the same rows, in their agencies, not carried, with no original period and no version.
        Assert.Equal(before, await ScalarAsync<string>(admin, checksum));
        Assert.Equal(2L, await ScalarAsync<long>(admin,
            """SELECT count(*) FROM "AbsenceDeductions" WHERE NOT "CarryForward" AND "OriginalPeriodStart" IS NULL AND "OriginalPeriodEnd" IS NULL AND "SourceVersion" IS NULL"""));
        Assert.True(await ScalarAsync<bool>(admin, """SELECT relrowsecurity AND relforcerowsecurity FROM pg_class WHERE relname = 'AbsenceDeductions'"""));
    }
}
