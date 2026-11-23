using Microsoft.EntityFrameworkCore;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using H = HRManagement.UnitTests.Iam053SalaryChangeHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53, the independent review's findings: a person's request is applied only over the pay profile it was written against; a
/// calculated, unfrozen run of the month earns a warning; a version replaced on its own first day holds no day; the activation meets
/// every due request once a cycle, however many are refused before it; a planned end is kept and never extended; and a profile that
/// is paid its grade's base is checked with that salary.
/// </summary>
public sealed class Iam053ReviewFixTests
{
    private static DateTime Utc(int y, int m, int d) => new(y, m, d, 0, 0, 0, DateTimeKind.Utc);

    private static async Task ChangeActiveProfileAsync(H h, Action<EmployeePayProfile> change)
    {
        await using var db = h.Db();
        var profile = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == H.Employee && p.IsActive);
        change(profile);
        await db.SaveChangesAsync();
    }

    // ------------------------------------------------------------------ a request and the profile it was written against

    public static TheoryData<string> ConsumerChanges() => new() { "salary in place", "grade in place", "unit in place", "version replaced" };

    [Theory]
    [MemberData(nameof(ConsumerChanges))]
    public async Task A_pending_request_is_not_approved_over_a_profile_a_consumer_changed_since(string change)
    {
        var h = await new H().SeedAsync();
        var id = (await h.SubmitAsync(salary: 5000m)).RequestId!.Value;
        await ConsumerChangeAsync(h, change);
        var profilesBefore = (await h.ProfilesAsync()).Select(p => (p.Id, p.BaseSalary, p.IsActive, p.EffectiveTo)).ToList();
        h.Events.Published.Clear();

        var result = await h.ApproveAsync(id);

        Assert.False(result.Success, change);
        Assert.Equal(PayProfileChangeFailure.Conflict, result.Failure);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(PayProfileChangeMessages.ProfileChangedSinceRequest, result.Error);
        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.PendingApproval, request.State);
        Assert.Null(request.DecidedBy);
        Assert.Null(request.AppliedProfileId);
        Assert.Empty(h.Events.Published);
        Assert.Equal(profilesBefore, (await h.ProfilesAsync()).Select(p => (p.Id, p.BaseSalary, p.IsActive, p.EffectiveTo)).ToList());
    }

    [Fact]
    public async Task A_scheduled_request_is_not_activated_over_a_profile_a_consumer_changed_since_and_says_why()
    {
        var h = await new H().SeedAsync();
        var id = (await h.SubmitAsync(salary: 5000m, effectiveFrom: new DateOnly(2026, 12, 1))).RequestId!.Value;
        Assert.Equal(PayProfileChangeState.Scheduled, (await h.ApproveAsync(id)).State);
        // A transfer's consumer raises the salary in place before December.
        await ChangeActiveProfileAsync(h, p => p.BaseSalary = 5600m);
        h.Clock.Now = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);

        var first = await h.ActivateAsync();
        var second = await h.ActivateAsync();

        Assert.Equal((1, 0, 1, 1), (first.Due, first.Applied, first.Blocked, first.BlockedTotal));
        Assert.Equal((1, 0, 1, 1), (second.Due, second.Applied, second.Blocked, second.BlockedTotal));
        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Scheduled, request.State);
        Assert.Equal(PayProfileChangeMessages.ProfileChangedSinceRequest, request.LastApplyError);
        var active = Assert.Single(await h.ProfilesAsync());
        Assert.Equal(5600m, active.BaseSalary);   // the consumer's salary, not the request's 5000
    }

    [Fact]
    public async Task A_request_written_against_the_profile_as_it_is_now_is_still_applied()
    {
        var h = await new H().SeedAsync();
        var stale = (await h.SubmitAsync(salary: 5000m)).RequestId!.Value;
        await ChangeActiveProfileAsync(h, p => p.BaseSalary = 4700m);
        Assert.Equal(PayProfileChangeFailure.Conflict, (await h.ApproveAsync(stale)).Failure);

        // The message says what to do: cancel, and request again on the current profile.
        Assert.True((await h.CancelAsync(stale)).Success);
        var fresh = await h.SubmitAsync(salary: 5000m);
        Assert.True(fresh.Success, fresh.Error);
        Assert.Equal(4700m, (await h.RequestAsync(fresh.RequestId!.Value)).CurrentBaseSalary);
        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync(fresh.RequestId.Value)).State);
        Assert.Equal(5000m, (await h.ActiveAsync()).BaseSalary);
    }

    private static async Task ConsumerChangeAsync(H h, string change)
    {
        switch (change)
        {
            case "salary in place":
                await ChangeActiveProfileAsync(h, p => p.BaseSalary += 300m);
                break;
            case "grade in place":
                await ChangeActiveProfileAsync(h, p => p.PayGradeId = h.HigherGradeId);
                break;
            case "unit in place":
                await ChangeActiveProfileAsync(h, p => p.OrgUnitId = H.ForeignUnit);
                break;
            default:
                // A contract's consumer: the active version is closed and another one is the active one, with the same values.
                await using (var db = h.Db())
                {
                    var old = await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == H.Employee && p.IsActive);
                    old.IsActive = false;
                    old.EffectiveTo = Utc(2026, 9, 30);
                    await db.SaveChangesAsync();
                    db.EmployeePayProfiles.Add(new EmployeePayProfile
                    {
                        EmployeeId = H.Employee,
                        PayGradeId = old.PayGradeId,
                        BaseSalary = old.BaseSalary,
                        OrgUnitId = old.OrgUnitId,
                        EffectiveFrom = Utc(2026, 10, 1),
                        IsActive = true,
                    });
                    await db.SaveChangesAsync();
                }

                break;
        }
    }

    // ------------------------------------------------------------------ the calculated, unfrozen run of the month

    [Fact]
    public async Task An_approval_into_a_month_whose_regular_run_is_calculated_and_unfrozen_is_applied_with_a_warning()
    {
        var h = await new H().SeedAsync();
        var id = (await h.SubmitAsync()).RequestId!.Value;
        await h.AddRunAsync(new DateOnly(2026, 11, 1), PayrollRunType.Regular, PayrollRunStatus.Calculated, frozen: false, withEmployeeLine: true);

        var result = await h.ApproveAsync(id);

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.Applied, result.State);
        Assert.Equal(PayProfileChangeMessages.CalculatedRunNeedsRecalculation, result.Warning);
        Assert.Matches(@"\p{IsArabic}", result.Warning!);
    }

    public static TheoryData<string, PayrollRunType?, PayrollRunStatus, int> RunsWithoutAWarning() => new()
    {
        { "no run", null, PayrollRunStatus.Draft, 11 },
        { "a draft run", PayrollRunType.Regular, PayrollRunStatus.Draft, 11 },
        { "a calculated correction", PayrollRunType.Correction, PayrollRunStatus.Calculated, 11 },
        { "a calculated regular run of another month", PayrollRunType.Regular, PayrollRunStatus.Calculated, 10 },
    };

    [Theory]
    [MemberData(nameof(RunsWithoutAWarning))]
    public async Task No_warning_without_a_calculated_unfrozen_regular_run_of_the_month(string label, PayrollRunType? type, PayrollRunStatus status, int month)
    {
        var h = await new H().SeedAsync();
        var id = (await h.SubmitAsync()).RequestId!.Value;
        if (type is { } kind)
            await h.AddRunAsync(new DateOnly(2026, month, 1), kind, status, frozen: false);

        var result = await h.ApproveAsync(id);

        Assert.True(result.Success, $"{label}: {result.Error}");
        Assert.Null(result.Warning);
    }

    // ------------------------------------------------------------------ a version replaced on its own first day

    [Fact]
    public async Task A_version_replaced_on_its_own_first_day_holds_no_day_and_no_day_is_in_two_versions()
    {
        var h = await new H().SeedAsync();
        // 4500 from January; 4800 from 1 November; then a correction of the same month: 5000 from 1 November.
        Assert.True((await h.ApproveAsync((await h.SubmitAsync(salary: 4800m)).RequestId!.Value)).Success);
        var correction = await h.SubmitAsync(salary: 5000m);
        Assert.True(correction.Success, correction.Error);
        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync(correction.RequestId!.Value)).State);

        var profiles = (await h.ProfilesAsync()).OrderBy(p => p.BaseSalary).ToList();
        Assert.Equal([4500m, 4800m, 5000m], profiles.Select(p => p.BaseSalary));
        var (first, replaced, active) = (profiles[0], profiles[1], profiles[2]);
        Assert.Equal(Utc(2026, 10, 31), first.EffectiveTo);
        // The replaced version began on 1 November and ends the day before: it holds no day at all.
        Assert.Equal(Utc(2026, 11, 1), replaced.EffectiveFrom);
        Assert.Equal(Utc(2026, 10, 31), replaced.EffectiveTo);
        Assert.False(replaced.IsActive);
        Assert.Equal((Utc(2026, 11, 1), (DateTime?)null, true), (active.EffectiveFrom, active.EffectiveTo, active.IsActive));

        // Every day from January 2026 to the end of 2027 is in exactly one version.
        for (var day = new DateOnly(2026, 1, 1); day <= new DateOnly(2027, 12, 31); day = day.AddDays(1))
        {
            var stored = EmployeePayProfileVersioning.ToStoredDate(day);
            var holding = profiles.Where(p => p.EffectiveFrom <= stored && (p.EffectiveTo is null || p.EffectiveTo >= stored)).ToList();
            Assert.True(holding.Count == 1, $"{day:yyyy-MM-dd} is in {holding.Count} versions.");
            Assert.Equal(day < new DateOnly(2026, 11, 1) ? 4500m : 5000m, holding[0].BaseSalary);
        }
    }

    // ------------------------------------------------------------------ the activation meets every due request

    [Fact]
    public async Task One_cycle_meets_every_due_request_however_many_are_refused_before_it()
    {
        var h = await new H().SeedAsync();
        var employees = Enumerable.Range(0, 7).Select(_ => Guid.NewGuid()).ToList();
        await using (var db = h.Db())
        {
            foreach (var employee in employees)
            {
                db.EmployeePayProfiles.Add(new EmployeePayProfile
                {
                    EmployeeId = employee,
                    PayGradeId = h.GradeId,
                    BaseSalary = H.Salary,
                    OrgUnitId = H.Unit,
                    EffectiveFrom = Utc(2026, 1, 1),
                    IsActive = true,
                });
            }

            await db.SaveChangesAsync();
        }

        var requests = new List<Guid>();
        foreach (var employee in employees)
        {
            h.Clock.Now = h.Clock.Now.AddMinutes(1);   // the order of the cycle: by effective date, then by the time of the request
            var id = (await h.SubmitAsync(salary: 5000m, effectiveFrom: new DateOnly(2026, 12, 1), employee: employee)).RequestId!.Value;
            Assert.Equal(PayProfileChangeState.Scheduled, (await h.ApproveAsync(id)).State);
            requests.Add(id);
        }

        // The five oldest are refused at their date (their profile changed since); the two behind them are fine.
        await using (var db = h.Db())
        {
            foreach (var employee in employees.Take(5))
                (await db.EmployeePayProfiles.SingleAsync(p => p.EmployeeId == employee && p.IsActive)).BaseSalary = 4600m;
            await db.SaveChangesAsync();
        }

        h.Clock.Now = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);
        var first = await h.ActivateAsync();
        var second = await h.ActivateAsync();

        Assert.Equal((7, 2, 5, 5), (first.Due, first.Applied, first.Blocked, first.BlockedTotal));
        // The second cycle meets the five again and applies nothing twice.
        Assert.Equal((5, 0, 5, 5), (second.Due, second.Applied, second.Blocked, second.BlockedTotal));
        foreach (var id in requests.Skip(5))
            Assert.Equal(PayProfileChangeState.Applied, (await h.RequestAsync(id)).State);
        foreach (var employee in employees.Skip(5))
            Assert.Equal(5000m, (await h.ProfilesAsync(employee)).Single(p => p.IsActive).BaseSalary);
    }

    // ------------------------------------------------------------------ a planned end

    [Fact]
    public async Task A_planned_end_after_the_new_start_is_carried_to_the_new_version_and_the_closed_one_ends_the_day_before()
    {
        var h = await new H().SeedAsync();
        await ChangeActiveProfileAsync(h, p => p.EffectiveTo = Utc(2027, 3, 31));   // a fixed-term contract's profile
        var id = (await h.SubmitAsync(salary: 5000m)).RequestId!.Value;

        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync(id)).State);

        var profiles = await h.ProfilesAsync();
        Assert.Equal(Utc(2026, 10, 31), profiles[0].EffectiveTo);
        Assert.Equal((5000m, Utc(2026, 11, 1), (DateTime?)Utc(2027, 3, 31), true),
            (profiles[1].BaseSalary, profiles[1].EffectiveFrom, profiles[1].EffectiveTo, profiles[1].IsActive));
    }

    [Fact]
    public void Closing_never_extends_a_version_that_already_ended_and_its_end_does_not_reach_the_new_one()
    {
        var previous = new EmployeePayProfile { EffectiveFrom = Utc(2026, 1, 1), EffectiveTo = Utc(2026, 8, 31), IsActive = true, BaseSalary = 4500m };
        var plannedEnd = previous.EffectiveTo;

        EmployeePayProfileVersioning.Close(previous, new DateOnly(2026, 12, 1));
        var next = EmployeePayProfileVersioning.NextVersion(previous, new DateOnly(2026, 12, 1), 5000m, null, plannedEnd);

        Assert.Equal(Utc(2026, 8, 31), previous.EffectiveTo);   // not rewritten to 30 November
        Assert.False(previous.IsActive);
        Assert.Null(next.EffectiveTo);                           // an end before the new start says nothing about the new version
    }

    [Fact]
    public void A_planned_end_on_the_new_start_day_is_still_carried()
    {
        var previous = new EmployeePayProfile { EffectiveFrom = Utc(2026, 1, 1), EffectiveTo = Utc(2026, 12, 1), IsActive = true, BaseSalary = 4500m };
        var plannedEnd = previous.EffectiveTo;

        EmployeePayProfileVersioning.Close(previous, new DateOnly(2026, 12, 1));
        var next = EmployeePayProfileVersioning.NextVersion(previous, new DateOnly(2026, 12, 1), 5000m, null, plannedEnd);

        Assert.Equal(Utc(2026, 11, 30), previous.EffectiveTo);
        Assert.Equal(Utc(2026, 12, 1), next.EffectiveTo);
    }

    // ------------------------------------------------------------------ the salary a grade-only change leaves

    [Fact]
    public async Task A_grade_only_change_of_a_profile_paid_its_grades_base_is_checked_with_the_new_grades_base()
    {
        var h = await new H().SeedAsync();
        await ChangeActiveProfileAsync(h, p => p.BaseSalary = 0m);   // zero: the engine pays the grade's base

        var result = await h.SubmitAsync(salary: null, grade: h.HigherGradeId);

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.Applied, (await h.ApproveAsync(result.RequestId!.Value)).State);
        var active = await h.ActiveAsync();
        Assert.Equal((0m, h.HigherGradeId), (active.BaseSalary, active.PayGradeId!.Value));
    }
}
