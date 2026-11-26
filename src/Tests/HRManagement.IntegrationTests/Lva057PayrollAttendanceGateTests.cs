using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payroll.Application.Options;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;

namespace HRManagement.IntegrationTests;

/// <summary>
/// PLAN-57 (§4 rule 7, §5): the attendance gate before a regular run's calculation, through the real start of a calculation
/// (SQLite). <c>Warn</c> calculates and leaves one <b>open</b> audit record with the count and the list, never a closed one (a closed
/// record is what lets a run be posted); <c>Block</c> refuses with the count and leaves the run as it was; <c>Off</c> checks nothing;
/// a correction run and a final settlement are exempt. «Not closed» is an employee with an active pay profile — who the run gives a
/// line to — and no receipt whose period overlaps the run's. The warning is not a review: closed, it lets nothing be posted.
/// </summary>
public sealed class Lva057PayrollAttendanceGateTests
{
    private static Action<IServiceCollection> Mode(PayrollAttendanceGateMode mode) =>
        services => services.Configure<PayrollAttendanceOptions>(o => o.Gate = mode);

    private static async Task<List<PayrollAuditRecord>> AuditAsync(Pay056RunScope scope, Guid run)
    {
        scope.Db.ChangeTracker.Clear();
        return await scope.Db.PayrollAuditRecords.AsNoTracking().Where(a => a.PayrollRunId == run).OrderBy(a => a.CreatedAt).ToListAsync();
    }

    [Fact]
    public async Task Warn_is_the_default_the_run_calculates_and_one_open_record_lists_who_is_missing()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var closed = await scope.EmployeeAsync();
        var missingOne = await scope.EmployeeAsync();
        var missingTwo = await scope.EmployeeAsync();
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(closed, 2028, 1, absentDays: 0));
        Assert.Equal(PayrollAttendanceGateMode.Warn, scope.Services.GetRequiredService<PayrollAttendanceGate>().Mode);

        var run = await scope.CalculatedRunAsync(2028, 1);

        Assert.Equal(3, (await scope.LinesAsync(run)).Count);
        var warning = Assert.Single(await AuditAsync(scope, run));
        // Open, never closed: a closed record is what the posting reads as «the audit was done».
        Assert.Equal(AuditStatus.Open, warning.Status);
        Assert.Null(warning.ClosedAt);
        Assert.Null(warning.ClosedBy);
        Assert.StartsWith(PayrollAttendanceGate.WarningPrefix + ": 2 ", warning.Findings);
        Assert.Contains("2028-01", warning.Findings);
        Assert.Equal(new[] { missingOne, missingTwo }.Order(), PayrollAttendanceGate.ParseFindings(warning.Findings).Order());

        // The run's reads say the same.
        var status = await scope.Services.GetRequiredService<PayrollAttendanceGate>().StatusAsync(await scope.RunAsync(run));
        Assert.Equal((PayrollAttendanceGateMode.Warn, true, 3), (status.GateMode, status.Applies, status.EmployeesInScope));
        Assert.Equal(new[] { missingOne, missingTwo }.Order(), status.MissingAttendance);
    }

    [Fact]
    public async Task The_warning_does_not_let_a_run_be_posted_and_does_not_stop_a_closed_review_from_doing_so()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        await scope.EmployeeAsync();
        var run = await scope.CalculatedRunAsync(2028, 1);
        await scope.SubmitAsync(run);
        await scope.ApproveAsync(run);
        Assert.Equal(AuditStatus.Open, Assert.Single(await AuditAsync(scope, run)).Status);

        // The open warning is not the closed audit a posting needs.
        var (posted, error) = await scope.PostAsync(run);
        Assert.False(posted);
        Assert.NotNull(error);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(run)).Status);

        // Even closed, the warning is not a review (the screens refuse to close it; here it is closed as stored data): the posting
        // looks for a closed record that is not the gate's.
        await using (var request = scope.NewRequest())
        {
            var db = request.ServiceProvider.GetRequiredService<Payroll.Infrastructure.Data.PayrollDbContext>();
            var warning = await db.PayrollAuditRecords.SingleAsync(a => a.PayrollRunId == run);
            Assert.True(PayrollAttendanceGate.IsWarning(warning));
            (warning.Status, warning.ClosedAt, warning.ClosedBy) = (AuditStatus.Closed, DateTime.UtcNow, Guid.NewGuid());
            await db.SaveChangesAsync();
        }

        var (postedOnAClosedWarning, _) = await scope.PostAsync(run);
        Assert.False(postedOnAClosedWarning);
        Assert.Equal(PayrollRunStatus.Approved, (await scope.RunAsync(run)).Status);
        await using (var request = scope.NewRequest())
        {
            var db = request.ServiceProvider.GetRequiredService<Payroll.Infrastructure.Data.PayrollDbContext>();
            var warning = await db.PayrollAuditRecords.SingleAsync(a => a.PayrollRunId == run);
            (warning.Status, warning.ClosedAt, warning.ClosedBy) = (AuditStatus.Open, null, null);
            await db.SaveChangesAsync();
        }

        // The auditor's closed review still allows it, beside the warning.
        await scope.CloseAuditAsync(run);
        var (postedAfter, errorAfter) = await scope.PostAsync(run);
        Assert.True(postedAfter, errorAfter);
        var records = await AuditAsync(scope, run);
        Assert.Equal(1, records.Count(a => a.Status == AuditStatus.Open && a.Findings!.StartsWith(PayrollAttendanceGate.WarningPrefix)));
        Assert.Equal(1, records.Count(a => a.Status == AuditStatus.Closed));
    }

    [Fact]
    public async Task A_run_whose_employees_are_all_closed_gets_no_warning_record()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var first = await scope.EmployeeAsync();
        var second = await scope.EmployeeAsync();
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(first, 2028, 1, absentDays: 0));
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(second, 2028, 1, absentDays: 1));

        var run = await scope.CalculatedRunAsync(2028, 1);

        Assert.Empty(await AuditAsync(scope, run));
        var status = await scope.Services.GetRequiredService<PayrollAttendanceGate>().StatusAsync(await scope.RunAsync(run));
        Assert.Empty(status.MissingAttendance);
    }

    [Fact]
    public async Task A_receipt_counts_only_when_its_period_overlaps_the_runs()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var otherMonth = await scope.EmployeeAsync();
        var overlapping = await scope.EmployeeAsync();
        // December's summary says nothing of January; a custom period from 16 January to 15 February does.
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(otherMonth, 2027, 12, absentDays: 0));
        await Lva057Payroll.ReceiveAsync(scope.Db, new Lva057Summary(Guid.NewGuid(), 1, overlapping, new DateOnly(2028, 1, 16), new DateOnly(2028, 2, 15), 0));
        var run = await scope.CreateRunAsync(2028, 1);
        var gate = scope.Services.GetRequiredService<PayrollAttendanceGate>();

        var (inScope, missing) = await gate.MissingAsync(await scope.Db.PayrollRuns.SingleAsync(r => r.Id == run));

        Assert.Equal(2, inScope);
        Assert.Equal([otherMonth], missing);
    }

    [Fact]
    public async Task The_gates_scope_is_who_the_run_gives_a_line_to()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var closed = await scope.EmployeeAsync();
        // A pay profile that begins after January 2028. The run gives its employee a line all the same (it takes every active
        // profile), so he is in the gate's scope: an employee payroll pays for a month HR.Core closed no attendance for is what the
        // warning is there to show, also when HR.Core never will (it holds him as hired later).
        var startsLater = Guid.NewGuid();
        scope.Db.EmployeePayProfiles.Add(new EmployeePayProfile
        {
            EmployeeId = startsLater,
            BaseSalary = 900_000m,
            EffectiveFrom = new DateTime(2028, 3, 1, 0, 0, 0, DateTimeKind.Utc),
            IsActive = true
        });
        await scope.Db.SaveChangesAsync();
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(closed, 2028, 1, absentDays: 0));

        var run = await scope.CalculatedRunAsync(2028, 1);

        var lines = (await scope.LinesAsync(run)).Select(l => l.EmployeeId).Order().ToList();
        var status = await scope.Services.GetRequiredService<PayrollAttendanceGate>().StatusAsync(await scope.RunAsync(run));
        Assert.Equal(new[] { closed, startsLater }.Order(), lines);
        Assert.Equal(lines.Count, status.EmployeesInScope);
        Assert.Equal([startsLater], status.MissingAttendance);
        Assert.Equal([startsLater], PayrollAttendanceGate.ParseFindings(Assert.Single(await AuditAsync(scope, run)).Findings));
    }

    [Fact]
    public async Task One_warning_record_per_run_is_refreshed_by_the_next_evaluation()
    {
        await using var scope = await Pay056RunScope.CreateAsync();
        var first = await scope.EmployeeAsync();
        var second = await scope.EmployeeAsync();
        var run = await scope.CalculatedRunAsync(2028, 1);
        Assert.Equal(2, PayrollAttendanceGate.ParseFindings(Assert.Single(await AuditAsync(scope, run)).Findings).Count);

        // Attendance is closed for one of them, and the run is evaluated again (as a restart does).
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(first, 2028, 1, absentDays: 0));
        scope.Db.ChangeTracker.Clear();
        var gate = scope.Services.GetRequiredService<PayrollAttendanceGate>();
        var decision = await gate.EvaluateAsync(await scope.Db.PayrollRuns.SingleAsync(r => r.Id == run));
        Assert.False(decision.WarningIsNew);
        await scope.Db.SaveChangesAsync();

        var refreshed = Assert.Single(await AuditAsync(scope, run));
        Assert.Equal([second], PayrollAttendanceGate.ParseFindings(refreshed.Findings));
        Assert.Equal(AuditStatus.Open, refreshed.Status);

        // The other's attendance is closed too: the next evaluation has nobody to list, and hands the old record back to be removed
        // (the start of a calculation removes it with its first save), not closed.
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(second, 2028, 1, absentDays: 0));
        scope.Db.ChangeTracker.Clear();
        var none = await gate.EvaluateAsync(await scope.Db.PayrollRuns.SingleAsync(r => r.Id == run));
        Assert.Equal((0, null as PayrollAuditRecord), (none.Missing, none.Warning));
        Assert.Equal(refreshed.Id, none.StaleWarning?.Id);
        Assert.Equal("تحذير بوابة الحضور: 1 موظف في نطاق التشغيل لم يُغلق حضورهم لفترة 2028-01. حُسب التشغيل بلا خصم غياب لهم (الإعداد Payroll:Attendance:Gate=Warn).",
            PayrollAttendanceGate.Headline(refreshed.Findings));
    }

    [Fact]
    public async Task Block_refuses_the_start_with_the_count_and_leaves_the_run_as_it_was()
    {
        await using var scope = await Pay056RunScope.CreateAsync(Mode(PayrollAttendanceGateMode.Block));
        var closed = await scope.EmployeeAsync();
        await scope.EmployeeAsync();
        await scope.EmployeeAsync();
        await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(closed, 2028, 1, absentDays: 0));
        var run = await scope.CreateRunAsync(2028, 1);
        var before = await scope.RunAsync(run);

        var (ok, lines, error) = await scope.CalculateAsync(run);

        Assert.False(ok);
        Assert.Equal(0, lines);
        Assert.Equal(PayrollAttendanceGate.BlockMessage(2, new DateOnly(2028, 1, 1)), error);
        // The gate is before the run moves: still a draft, no pinned framework, no job, no line, no audit record.
        var after = await scope.RunAsync(run);
        Assert.Equal(PayrollRunStatus.Draft, after.Status);
        Assert.Equal((before.Revision, before.PayrollConfigurationId, before.ConfigContentHash, before.ResultsFrozen),
            (after.Revision, after.PayrollConfigurationId, after.ConfigContentHash, after.ResultsFrozen));
        Assert.Null(after.PayrollConfigurationId);
        Assert.Empty(await scope.LinesAsync(run));
        Assert.Equal(0, await scope.Db.PayrollRunJobs.CountAsync(j => j.PayrollRunId == run));
        Assert.Empty(await AuditAsync(scope, run));

        // Once everyone is closed, the same run calculates.
        scope.Db.ChangeTracker.Clear();
        foreach (var employee in await scope.Db.EmployeePayProfiles.AsNoTracking().Where(p => p.EmployeeId != closed).Select(p => p.EmployeeId).ToListAsync())
            await Lva057Payroll.ReceiveAsync(scope.Db, Lva057Summary.Close(employee, 2028, 1, absentDays: 0));
        var (okAfter, linesAfter, errorAfter) = await scope.CalculateAsync(run);
        Assert.True(okAfter, errorAfter);
        Assert.Equal(3, linesAfter);
        Assert.Empty(await AuditAsync(scope, run));
    }

    [Fact]
    public async Task Off_checks_nothing()
    {
        await using var scope = await Pay056RunScope.CreateAsync(Mode(PayrollAttendanceGateMode.Off));
        await scope.EmployeeAsync();

        var run = await scope.CalculatedRunAsync(2028, 1);

        Assert.Single(await scope.LinesAsync(run));
        Assert.Empty(await AuditAsync(scope, run));
    }

    [Theory]
    [InlineData(PayrollRunType.Correction)]
    [InlineData(PayrollRunType.FinalSettlement)]
    public async Task A_correction_run_and_a_final_settlement_are_exempt_even_under_Block(PayrollRunType type)
    {
        await using var scope = await Pay056RunScope.CreateAsync(Mode(PayrollAttendanceGateMode.Block));
        await scope.EmployeeAsync();
        var gate = scope.Services.GetRequiredService<PayrollAttendanceGate>();
        var regularId = await scope.CreateRunAsync(2028, 1);
        var regular = await scope.Db.PayrollRuns.SingleAsync(r => r.Id == regularId);
        Assert.NotNull((await gate.EvaluateAsync(regular)).Refusal);

        var exempt = new PayrollRun
        {
            RunType = type,
            PeriodStart = regular.PeriodStart,
            PeriodEnd = regular.PeriodEnd,
            RunDate = regular.RunDate
        };

        var decision = await gate.EvaluateAsync(exempt);

        Assert.False(PayrollAttendanceGate.AppliesTo(exempt));
        Assert.Null(decision.Refusal);
        Assert.Null(decision.Warning);
        Assert.False((await gate.StatusAsync(exempt)).Applies);
    }
}
