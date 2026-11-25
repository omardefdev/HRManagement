using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Services;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using H = HRManagement.UnitTests.Iam053SalaryChangeHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53 (§3, D-PAY-6): an automatic source is recorded in the same entity, applied at once, for every automatic source; an open
/// request of the employee is superseded. And P-6: «today» and «the current month» are Damascus dates, and a run blocks a change only
/// when it settles the month for this employee.
/// </summary>
public sealed class Iam053SystemChangeAndCalendarTests
{
    public static TheoryData<PayProfileChangeSource> AutomaticSources() => new()
    {
        PayProfileChangeSource.ContractActivated, PayProfileChangeSource.Promotion, PayProfileChangeSource.Transfer, PayProfileChangeSource.Correction,
    };

    // ------------------------------------------------------------------ ApplySystemChangeAsync

    [Theory]
    [MemberData(nameof(AutomaticSources))]
    public async Task An_automatic_change_is_recorded_applied_at_once_with_no_decider(PayProfileChangeSource source)
    {
        var h = await new H().SeedAsync();

        var result = await h.SystemChangeAsync(source, salary: 5200m, reference: "عقد 2026/441");

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.Applied, result.State);
        var request = await h.RequestAsync(result.RequestId!.Value);
        Assert.Equal(PayProfileChangeState.Applied, request.State);
        Assert.Equal(source, request.Source);
        Assert.Equal("عقد 2026/441", request.SourceReference);        // the original decision
        Assert.Null(request.DecidedBy);                               // nobody approved it here
        Assert.Null(request.DecidedAt);
        Assert.Equal(Guid.Empty, request.RequestedBy);                // the system
        Assert.Equal(PayProfileChangeRequest.SystemActorName, request.RequestedByName);
        Assert.Equal(h.Clock.Now.UtcDateTime, request.AppliedAt);
        Assert.Equal((H.Salary, (Guid?)h.GradeId, (Guid?)h.ProfileId), (request.CurrentBaseSalary!.Value, request.CurrentPayGradeId, request.CurrentProfileId));
        Assert.Equal(H.Unit, request.OrgUnitId);

        var profiles = await h.ProfilesAsync();
        Assert.Equal(2, profiles.Count);
        Assert.False(profiles[0].IsActive);
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), profiles[0].EffectiveTo);
        Assert.Equal(request.AppliedProfileId, profiles[1].Id);
        Assert.Equal((5200m, (Guid?)H.Unit, "law50"), (profiles[1].BaseSalary, profiles[1].OrgUnitId, profiles[1].RegimeCode));
        Assert.Equal(1, await h.HistoryCountAsync());

        // One event: the application. No submission and no decision were made.
        var applied = Assert.IsType<PayProfileChangeAppliedEvent>(Assert.Single(h.Events.Published));
        Assert.Equal((source.ToString(), "عقد 2026/441", 5200m, (decimal?)H.Salary), (applied.Source, applied.SourceReference, applied.BaseSalary, applied.PreviousBaseSalary));
    }

    [Theory]
    [MemberData(nameof(AutomaticSources))]
    public async Task An_open_request_of_the_employee_is_superseded_by_the_automatic_change(PayProfileChangeSource source)
    {
        foreach (var scheduled in new[] { false, true })
        {
            var h = await new H().SeedAsync();
            var open = (await h.SubmitAsync(effectiveFrom: scheduled ? new DateOnly(2026, 12, 1) : null)).RequestId!.Value;
            if (scheduled)
                Assert.Equal(PayProfileChangeState.Scheduled, (await h.ApproveAsync(open)).State);

            var result = await h.SystemChangeAsync(source);

            Assert.True(result.Success, result.Error);
            var superseded = await h.RequestAsync(open);
            Assert.Equal(PayProfileChangeState.Superseded, superseded.State);
            Assert.Equal(result.RequestId, superseded.SupersededByRequestId);
            Assert.Null(superseded.AppliedProfileId);
            // Superseded is closed: it is never applied later, and the employee may get a new request.
            h.Clock.Now = h.Clock.Now.AddMonths(2);
            Assert.Equal(0, (await h.ActivateAsync()).Applied);
            Assert.Equal(2, (await h.ProfilesAsync()).Count);
            Assert.True((await h.SubmitAsync(salary: 5500m, effectiveFrom: h.Calendar.FirstDayOfCurrentMonth)).Success);
        }
    }

    [Theory]
    [InlineData(PayProfileChangeSource.Manual)]
    [InlineData(PayProfileChangeSource.Api)]
    public async Task A_manual_source_never_takes_the_automatic_path(PayProfileChangeSource source)
    {
        var h = await new H().SeedAsync();

        var result = await h.SystemChangeAsync(source);

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.Invalid, result.Failure);
        Assert.Equal(PayProfileChangeMessages.AutomaticSourceOnly, result.Error);
        Assert.Equal(0, await h.RequestCountAsync());
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task A_frozen_month_does_not_refuse_an_automatic_change_and_out_of_bounds_does_not_either()
    {
        var h = await new H().SeedAsync();
        await h.AddRunAsync(new DateOnly(2026, 11, 1), PayrollRunType.Regular, PayrollRunStatus.Posted, frozen: true);

        // A manual request for the same month is refused at approval (see the run kinds below); the decision of record is not.
        var result = await h.SystemChangeAsync(PayProfileChangeSource.Promotion, salary: H.GradeMax + 900);

        Assert.True(result.Success, result.Error);
        Assert.Equal(H.GradeMax + 900, (await h.ActiveAsync()).BaseSalary);
    }

    [Fact]
    public async Task The_kind_says_which_part_of_an_automatic_change_counts()
    {
        var grade = await new H().SeedAsync();
        Assert.True((await grade.SystemChangeAsync(PayProfileChangeSource.Promotion, PayProfileChangeKind.PayGrade, salary: 9999m, grade: grade.HigherGradeId)).Success);
        var afterGrade = await grade.ActiveAsync();
        Assert.Equal((H.Salary, (Guid?)grade.HigherGradeId), (afterGrade.BaseSalary, afterGrade.PayGradeId));   // the salary was not part of it

        var both = await new H().SeedAsync();
        Assert.True((await both.SystemChangeAsync(PayProfileChangeSource.Promotion, PayProfileChangeKind.BaseSalaryAndGrade, salary: 6500m, grade: both.HigherGradeId)).Success);
        var afterBoth = await both.ActiveAsync();
        Assert.Equal((6500m, (Guid?)both.HigherGradeId), (afterBoth.BaseSalary, afterBoth.PayGradeId));

        var missing = await new H().SeedAsync();
        Assert.Equal(PayProfileChangeFailure.Invalid, (await missing.SystemChangeAsync(PayProfileChangeSource.Promotion, PayProfileChangeKind.PayGrade, grade: null)).Failure);
        Assert.Equal(PayProfileChangeFailure.Invalid, (await missing.SystemChangeAsync(PayProfileChangeSource.Promotion, PayProfileChangeKind.BaseSalary, salary: null)).Failure);
        Assert.Equal(PayProfileChangeFailure.Invalid, (await missing.SystemChangeAsync(PayProfileChangeSource.Promotion, PayProfileChangeKind.PayGrade, grade: Guid.NewGuid())).Failure);
        Assert.Equal(PayProfileChangeFailure.Invalid, (await missing.SystemChangeAsync(PayProfileChangeSource.Promotion, salary: 0m)).Failure);
        Assert.Equal(0, await missing.RequestCountAsync());
        await missing.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task An_automatic_change_cannot_start_before_the_version_it_replaces_nor_without_an_active_profile()
    {
        var h = await new H().SeedAsync();

        var early = await h.SystemChangeAsync(PayProfileChangeSource.Correction, effectiveFrom: new DateOnly(2025, 12, 1));
        Assert.Equal(PayProfileChangeFailure.Invalid, early.Failure);
        Assert.Equal(PayProfileChangeMessages.BeforeCurrentVersion, early.Error);

        await using (var db = h.Db())
        {
            db.EmployeePayProfiles.Single().IsActive = false;
            await db.SaveChangesAsync();
        }

        var none = await h.SystemChangeAsync(PayProfileChangeSource.Correction);
        Assert.Equal(PayProfileChangeMessages.NoActiveProfile, none.Error);
        Assert.Equal(0, await h.RequestCountAsync());
        Assert.Empty(h.Events.Published);
    }

    // ------------------------------------------------------------------ P-6: Damascus dates

    [Theory]
    // 30 November 2026, 20:59 UTC: still 30 November in Damascus (23:59).
    [InlineData("2026-11-30T20:59:00Z", 2026, 11, 30)]
    // 30 November 2026, 21:00 UTC: already 1 December in Damascus (00:00).
    [InlineData("2026-11-30T21:00:00Z", 2026, 12, 1)]
    // The first of a month at 00:30 Damascus time is the last day of the previous month in UTC.
    [InlineData("2026-11-30T21:30:00Z", 2026, 12, 1)]
    // December to January.
    [InlineData("2026-12-31T21:30:00Z", 2027, 1, 1)]
    [InlineData("2026-12-31T20:30:00Z", 2026, 12, 31)]
    // February to March.
    [InlineData("2027-02-28T21:15:00Z", 2027, 3, 1)]
    [InlineData("2027-02-28T12:00:00Z", 2027, 2, 28)]
    public void Today_is_the_date_in_damascus_not_in_utc(string utc, int year, int month, int day)
    {
        var calendar = new PayrollCalendar(new Iam053Clock(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Equal(new DateOnly(year, month, day), calendar.Today);
        Assert.Equal(new DateOnly(year, month, 1), calendar.FirstDayOfCurrentMonth);
    }

    [Fact]
    public async Task On_the_last_evening_of_a_month_in_utc_the_current_month_is_already_the_next_one()
    {
        // 30 November 21:30 UTC = 1 December 00:30 in Damascus.
        var h = await new H(new DateTimeOffset(2026, 11, 30, 21, 30, 0, TimeSpan.Zero)).SeedAsync();

        // November is over: a request for it is before the current month (UTC alone would still accept it).
        var november = await h.SubmitAsync(effectiveFrom: new DateOnly(2026, 11, 1));
        Assert.Equal(PayProfileChangeFailure.Invalid, november.Failure);
        Assert.Equal(PayProfileChangeMessages.BeforeCurrentMonth, november.Error);

        // December has begun: an approved December request is due, so it is applied, not scheduled.
        var december = await h.SubmitAsync(effectiveFrom: new DateOnly(2026, 12, 1));
        Assert.True(december.Success, december.Error);
        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync(december.RequestId!.Value)).State);
        var profiles = await h.ProfilesAsync();
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), profiles[0].EffectiveTo);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), profiles[1].EffectiveFrom);
    }

    [Fact]
    public async Task One_minute_before_midnight_in_damascus_the_next_month_is_still_scheduled()
    {
        // 30 November 20:59 UTC = 23:59 in Damascus.
        var h = await new H(new DateTimeOffset(2026, 11, 30, 20, 59, 0, TimeSpan.Zero)).SeedAsync();
        var december = await h.SubmitAsync(effectiveFrom: new DateOnly(2026, 12, 1));

        Assert.Equal(PayProfileChangeState.Scheduled, (await h.ApproveAsync(december.RequestId!.Value)).State);
        Assert.Equal(0, (await h.ActivateAsync()).Due);

        // Two minutes later it is 1 December in Damascus: the activation applies it.
        h.Clock.Now = new DateTimeOffset(2026, 11, 30, 21, 1, 0, TimeSpan.Zero);
        Assert.Equal(1, (await h.ActivateAsync()).Applied);
    }

    [Theory]
    [InlineData("2026-12-31T21:30:00Z", 2027, 1, 1, 2026, 12, 31)]   // December to January
    [InlineData("2027-02-28T21:30:00Z", 2027, 3, 1, 2027, 2, 28)]    // February
    [InlineData("2028-02-29T21:30:00Z", 2028, 3, 1, 2028, 2, 29)]    // a leap February
    public async Task The_previous_version_ends_on_the_last_day_of_the_month_before(string utc, int y, int m, int d, int cy, int cm, int cd)
    {
        var h = await new H(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)).SeedAsync();
        var id = (await h.SubmitAsync(effectiveFrom: new DateOnly(y, m, d))).RequestId!.Value;

        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync(id)).State);

        var profiles = await h.ProfilesAsync();
        Assert.Equal(new DateTime(cy, cm, cd, 0, 0, 0, DateTimeKind.Utc), profiles[0].EffectiveTo);
        Assert.Equal(new DateTime(y, m, d, 0, 0, 0, DateTimeKind.Utc), profiles[1].EffectiveFrom);
    }

    // ------------------------------------------------------------------ P-6: which run blocks a change

    public static TheoryData<string, PayrollRunType, PayrollRunStatus, bool, bool, bool> RunKinds() => new()
    {
        // label, type, status, frozen, the employee's line, blocks
        { "regular frozen", PayrollRunType.Regular, PayrollRunStatus.Calculated, true, false, true },
        { "regular under review, frozen", PayrollRunType.Regular, PayrollRunStatus.UnderReview, true, true, true },
        { "regular approved", PayrollRunType.Regular, PayrollRunStatus.Approved, false, false, true },
        { "regular posted", PayrollRunType.Regular, PayrollRunStatus.Posted, false, false, true },
        { "regular calculating", PayrollRunType.Regular, PayrollRunStatus.Calculating, false, false, true },
        { "regular validating", PayrollRunType.Regular, PayrollRunStatus.Validating, false, false, true },
        { "regular draft", PayrollRunType.Regular, PayrollRunStatus.Draft, false, false, false },
        { "regular calculated, not frozen", PayrollRunType.Regular, PayrollRunStatus.Calculated, false, true, false },
        { "regular reversed", PayrollRunType.Regular, PayrollRunStatus.Reversed, true, true, false },
        { "regular cancelled", PayrollRunType.Regular, PayrollRunStatus.Cancelled, true, true, false },
        { "correction frozen with his line", PayrollRunType.Correction, PayrollRunStatus.Approved, true, true, true },
        { "correction frozen without his line", PayrollRunType.Correction, PayrollRunStatus.Approved, true, false, false },
        { "final settlement under review with his line", PayrollRunType.FinalSettlement, PayrollRunStatus.UnderReview, true, true, true },
        { "final settlement under review without his line", PayrollRunType.FinalSettlement, PayrollRunStatus.UnderReview, true, false, false },
        { "final settlement posted without his line", PayrollRunType.FinalSettlement, PayrollRunStatus.Posted, true, false, false },
        { "correction calculating with his line", PayrollRunType.Correction, PayrollRunStatus.Calculating, false, true, false },
        { "correction reversed with his line", PayrollRunType.Correction, PayrollRunStatus.Reversed, true, true, false },
    };

    [Theory]
    [MemberData(nameof(RunKinds))]
    public async Task A_run_blocks_a_change_only_when_it_settles_the_month_for_this_employee(
        string label, PayrollRunType type, PayrollRunStatus status, bool frozen, bool line, bool blocks)
    {
        var h = await new H().SeedAsync();
        var id = (await h.SubmitAsync()).RequestId!.Value;   // from 1 November 2026, submitted before the run
        await h.AddRunAsync(new DateOnly(2026, 11, 1), type, status, frozen, line);
        h.Events.Published.Clear();

        var result = await h.ApproveAsync(id);

        if (!blocks)
        {
            Assert.True(result.Success, $"{label}: {result.Error}");
            Assert.Equal(PayProfileChangeState.Applied, result.State);
            return;
        }

        Assert.False(result.Success, label);
        Assert.Equal(PayProfileChangeFailure.Conflict, result.Failure);
        Assert.Equal(409, result.StatusCode);
        Assert.Contains("2026/11", result.Error!, StringComparison.Ordinal);    // the message names the month
        Assert.Matches(@"\p{IsArabic}", result.Error!);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);
        Assert.Null((await h.RequestAsync(id)).DecidedBy);
        Assert.Empty(h.Events.Published);
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task A_run_of_another_month_does_not_block_and_a_frozen_month_does_not_refuse_the_request_itself()
    {
        var h = await new H().SeedAsync();
        await h.AddRunAsync(new DateOnly(2026, 10, 1), PayrollRunType.Regular, PayrollRunStatus.Posted, frozen: true);
        await h.AddRunAsync(new DateOnly(2026, 12, 1), PayrollRunType.Regular, PayrollRunStatus.Posted, frozen: true);

        // October and December are settled; November is free.
        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync((await h.SubmitAsync()).RequestId!.Value)).State);

        // D-53-1: the frozen month refuses the approval and the activation, not the request.
        var december = await h.SubmitAsync(salary: 5300m, effectiveFrom: new DateOnly(2026, 12, 1));
        Assert.True(december.Success, december.Error);
        Assert.Equal(PayProfileChangeFailure.Conflict, (await h.ApproveAsync(december.RequestId!.Value)).Failure);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(december.RequestId.Value)).State);
    }

    [Fact]
    public async Task The_seeded_runs_with_no_lines_do_not_refuse_a_change_of_their_month()
    {
        // The acceptance seed holds a FinalSettlement run under review with frozen results and an approved Correction run, both with no
        // salary lines: a rule on the month alone would refuse every change of those months.
        var h = await new H().SeedAsync();
        await h.AddRunAsync(new DateOnly(2026, 11, 1), PayrollRunType.FinalSettlement, PayrollRunStatus.UnderReview, frozen: true);
        await h.AddRunAsync(new DateOnly(2026, 11, 1), PayrollRunType.Correction, PayrollRunStatus.Approved, frozen: true);

        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync((await h.SubmitAsync()).RequestId!.Value)).State);
    }
}
