using System.Reflection;
using HRManagement.MessageContracts.Events;
using Payroll.Application.Abstractions;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using H = HRManagement.UnitTests.Iam053SalaryChangeHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53 (§2, P-5): every transition of the salary change request, and every refusal with the state unchanged. A processor submits,
/// a different approver approves or rejects, the requester or an approver cancels, the applier is the only creator of a pay profile
/// version, and a refusal writes nothing. The messages are Arabic; the failure kinds are the HTTP answers (400, 409, 403, 404).
/// </summary>
public sealed class Iam053SalaryChangeStateMachineTests
{
    private static async Task<(H Harness, Guid RequestId)> PendingAsync(DateOnly? effectiveFrom = null, decimal? salary = 5000m)
    {
        var h = await new H().SeedAsync();
        var submitted = await h.SubmitAsync(salary: salary, effectiveFrom: effectiveFrom);
        Assert.True(submitted.Success, submitted.Error);
        h.Events.Published.Clear();
        return (h, submitted.RequestId!.Value);
    }

    private static DateOnly NextMonth(H h) => h.Calendar.FirstDayOfCurrentMonth.AddMonths(1);

    // ------------------------------------------------------------------ submit

    [Fact]
    public async Task Submit_creates_a_pending_request_with_the_current_values_and_changes_no_profile()
    {
        var h = await new H().SeedAsync();

        var result = await h.SubmitAsync(salary: 5000m);

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.PendingApproval, result.State);
        var request = await h.RequestAsync(result.RequestId!.Value);
        Assert.Equal(PayProfileChangeState.PendingApproval, request.State);
        Assert.Equal(PayProfileChangeKind.BaseSalary, request.ChangeKind);
        Assert.Equal(PayProfileChangeSource.Manual, request.Source);
        Assert.Equal(H.Unit, request.OrgUnitId);
        Assert.Equal(h.ProfileId, request.CurrentProfileId);
        Assert.Equal(H.Salary, request.CurrentBaseSalary);
        Assert.Equal(h.GradeId, request.CurrentPayGradeId);
        Assert.Equal(5000m, request.ProposedBaseSalary);
        Assert.Null(request.ProposedPayGradeId);
        Assert.Equal(new DateOnly(2026, 11, 1), request.EffectiveFrom);
        Assert.Equal(H.Reason, request.Reason);
        Assert.Equal(H.LegalReference, request.LegalReference);
        Assert.Equal(H.Processor, request.RequestedBy);
        Assert.Equal(h.Clock.Now.UtcDateTime, request.RequestedAt);
        Assert.Null(request.DecidedBy);
        Assert.Null(request.AppliedProfileId);
        await h.AssertProfileUntouchedAsync();

        var published = Assert.IsType<PayProfileChangeSubmittedEvent>(Assert.Single(h.Events.Published));
        Assert.Equal(request.Id, published.RequestId);
        Assert.Equal(nameof(PayProfileChangeKind.BaseSalary), published.ChangeKind);
        Assert.Equal(nameof(PayProfileChangeSource.Manual), published.Source);
    }

    [Fact]
    public void The_submitted_event_carries_no_amount()
    {
        foreach (var type in new[] { typeof(IPayProfileChangeSubmitted), typeof(PayProfileChangeSubmittedEvent) })
        {
            var amounts = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType) == typeof(decimal)
                            || p.Name.Contains("Salary", StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Name).ToList();
            Assert.True(amounts.Count == 0, $"{type.Name} carries an amount: {string.Join(", ", amounts)}");
        }
    }

    [Theory]
    [InlineData(5000, false, PayProfileChangeKind.BaseSalary)]
    [InlineData(0, true, PayProfileChangeKind.PayGrade)]
    [InlineData(6500, true, PayProfileChangeKind.BaseSalaryAndGrade)]
    public async Task The_kind_is_what_differs_from_the_current_profile(int salary, bool newGrade, PayProfileChangeKind kind)
    {
        var h = await new H().SeedAsync();
        if (kind == PayProfileChangeKind.PayGrade)
            await h.SetGradeBoundsAsync(h.HigherGradeId, 4000, 8000);   // the current salary fits the new grade

        var result = await h.SubmitAsync(salary: salary == 0 ? null : salary, grade: newGrade ? h.HigherGradeId : null);

        Assert.True(result.Success, result.Error);
        var request = await h.RequestAsync(result.RequestId!.Value);
        Assert.Equal(kind, request.ChangeKind);
        Assert.Equal(kind == PayProfileChangeKind.PayGrade ? null : salary, request.ProposedBaseSalary);
        Assert.Equal(newGrade ? h.HigherGradeId : null, request.ProposedPayGradeId);
    }

    [Fact]
    public async Task A_value_equal_to_the_current_one_is_not_proposed()
    {
        var h = await new H().SeedAsync();

        // The PUT alias always sends the salary and the grade: only what differs becomes the proposal.
        var result = await h.SubmitAsync(salary: 5000m, grade: h.GradeId);

        Assert.True(result.Success, result.Error);
        var request = await h.RequestAsync(result.RequestId!.Value);
        Assert.Equal(PayProfileChangeKind.BaseSalary, request.ChangeKind);
        Assert.Null(request.ProposedPayGradeId);
    }

    public static TheoryData<string> BrokenRules() => new()
    {
        "no-reason", "blank-reason", "long-reason", "no-legal-reference", "long-legal-reference", "nothing-proposed", "same-values",
        "zero-salary", "negative-salary", "mid-month", "before-current-month", "before-current-version", "below-bounds", "above-bounds",
        "grade-only-out-of-bounds", "unknown-grade", "no-active-profile", "automatic-source",
    };

    [Theory]
    [MemberData(nameof(BrokenRules))]
    public async Task A_broken_input_rule_is_refused_as_invalid_and_writes_nothing(string rule)
    {
        var h = await new H().SeedAsync();
        var result = rule switch
        {
            "no-reason" => await h.SubmitAsync(reason: null),
            "blank-reason" => await h.SubmitAsync(reason: "   "),
            "long-reason" => await h.SubmitAsync(reason: new string('س', 501)),
            "no-legal-reference" => await h.SubmitAsync(legalReference: null),
            "long-legal-reference" => await h.SubmitAsync(legalReference: new string('م', 201)),
            "nothing-proposed" => await h.SubmitAsync(salary: null, grade: null),
            "same-values" => await h.SubmitAsync(salary: H.Salary, grade: h.GradeId),
            "zero-salary" => await h.SubmitAsync(salary: 0m),
            "negative-salary" => await h.SubmitAsync(salary: -1m),
            "mid-month" => await h.SubmitAsync(effectiveFrom: new DateOnly(2026, 12, 15)),
            "before-current-month" => await h.SubmitAsync(effectiveFrom: new DateOnly(2026, 10, 1)),
            "before-current-version" => await BeforeCurrentVersionAsync(h),
            "below-bounds" => await h.SubmitAsync(salary: H.GradeMin - 1),
            "above-bounds" => await h.SubmitAsync(salary: H.GradeMax + 1),
            "grade-only-out-of-bounds" => await h.SubmitAsync(salary: null, grade: h.HigherGradeId),   // 4500 is below G6's 6000
            "unknown-grade" => await h.SubmitAsync(salary: null, grade: Guid.NewGuid()),
            "no-active-profile" => await h.SubmitAsync(employee: H.OtherEmployee),
            "automatic-source" => await h.SubmitAsync(source: PayProfileChangeSource.Promotion),
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.Invalid, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Matches(@"\p{IsArabic}", result.Error!);
        Assert.Equal(0, await h.RequestCountAsync());
        Assert.Empty(h.Events.Published);
        await h.AssertProfileUntouchedAsync();
    }

    private static async Task<PayProfileChangeResult> BeforeCurrentVersionAsync(H h)
    {
        // The active version starts on 1 January 2027; a request for December 2026 would start before it.
        await using (var db = h.Db())
        {
            var profile = db.EmployeePayProfiles.Single();
            profile.EffectiveFrom = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }

        var result = await h.SubmitAsync(effectiveFrom: new DateOnly(2026, 12, 1));
        await using (var db = h.Db())
        {
            db.EmployeePayProfiles.Single().EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            await db.SaveChangesAsync();
        }
        return result;
    }

    [Theory]
    [InlineData(3999)]
    [InlineData(6001)]
    public async Task An_out_of_bounds_salary_is_refused_with_a_message_naming_both_bounds(int salary)
    {
        var h = await new H().SeedAsync();

        var result = await h.SubmitAsync(salary: salary);

        Assert.Equal(PayProfileChangeFailure.Invalid, result.Failure);
        Assert.Contains("G5", result.Error!, StringComparison.Ordinal);
        Assert.Contains("4000", result.Error!, StringComparison.Ordinal);
        Assert.Contains("6000", result.Error!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4000)]
    [InlineData(6000)]
    public async Task The_bounds_themselves_are_inside(int salary)
    {
        var h = await new H().SeedAsync();
        Assert.True((await h.SubmitAsync(salary: salary)).Success);
    }

    [Fact]
    public async Task A_bound_that_is_not_set_is_not_applied()
    {
        var h = await new H().SeedAsync();
        await h.SetGradeBoundsAsync(h.GradeId, 4000, 0);   // no maximum

        Assert.True((await h.SubmitAsync(salary: 99_000m)).Success);
        var below = await new H().SeedAsync();
        await below.SetGradeBoundsAsync(below.GradeId, 0, 6000);   // no minimum
        Assert.True((await below.SubmitAsync(salary: 10m)).Success);
    }

    [Fact]
    public async Task The_settings_relax_their_own_rule_only()
    {
        var bounds = await new H().SeedAsync();
        bounds.Options.AllowOutOfGradeBounds = true;
        Assert.True((await bounds.SubmitAsync(salary: H.GradeMax + 500)).Success);

        var midMonth = await new H().SeedAsync();
        midMonth.Options.RequireMonthStart = false;
        Assert.True((await midMonth.SubmitAsync(effectiveFrom: new DateOnly(2026, 11, 20))).Success);
        // Still not before the current month.
        var early = await new H().SeedAsync();
        early.Options.RequireMonthStart = false;
        Assert.Equal(PayProfileChangeFailure.Invalid, (await early.SubmitAsync(effectiveFrom: new DateOnly(2026, 10, 20))).Failure);

        var reference = await new H().SeedAsync();
        reference.Options.RequireLegalReference = false;
        Assert.True((await reference.SubmitAsync(legalReference: null)).Success);
        // The reason is never optional.
        Assert.Equal(PayProfileChangeFailure.Invalid, (await reference.SubmitAsync(reason: null)).Failure);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_second_open_request_of_the_same_employee_is_a_conflict(bool firstIsScheduled)
    {
        var h = await new H().SeedAsync();
        var first = await h.SubmitAsync(effectiveFrom: firstIsScheduled ? NextMonth(h) : null);
        if (firstIsScheduled)
            Assert.Equal(PayProfileChangeState.Scheduled, (await h.ApproveAsync(first.RequestId!.Value)).State);

        var second = await h.SubmitAsync(salary: 5100m);

        Assert.False(second.Success);
        Assert.Equal(PayProfileChangeFailure.Conflict, second.Failure);
        Assert.Equal(409, second.StatusCode);
        Assert.Equal(PayProfileChangeMessages.OpenRequestExists, second.Error);
        Assert.Equal(1, await h.RequestCountAsync());

        // Once the open one is cancelled, a new request is accepted.
        Assert.True((await h.CancelAsync(first.RequestId!.Value)).Success);
        Assert.True((await h.SubmitAsync(salary: 5100m)).Success);
    }

    [Fact]
    public async Task An_employee_outside_the_callers_branch_does_not_exist_for_him()
    {
        var h = await new H().SeedAsync();

        var foreign = await h.SubmitAsync(scope: H.Restricted(H.ForeignUnit));
        Assert.Equal(PayProfileChangeFailure.NotFound, foreign.Failure);
        Assert.Equal(404, foreign.StatusCode);
        // No profile to place the employee in a branch: the same answer, so nothing tells a restricted caller that he exists.
        Assert.Equal(PayProfileChangeFailure.NotFound, (await h.SubmitAsync(employee: H.OtherEmployee, scope: H.Restricted(H.Unit))).Failure);
        Assert.Equal(0, await h.RequestCountAsync());

        Assert.True((await h.SubmitAsync(scope: H.Restricted(H.Unit))).Success);
    }

    [Fact]
    public async Task A_profile_with_no_unit_is_central_a_restricted_caller_never_reaches_it()
    {
        var h = await new H().SeedAsync(withUnit: false);

        Assert.Equal(PayProfileChangeFailure.NotFound, (await h.SubmitAsync(scope: H.Restricted(H.Unit))).Failure);
        var central = await h.SubmitAsync();
        Assert.True(central.Success, central.Error);
        Assert.Null((await h.RequestAsync(central.RequestId!.Value)).OrgUnitId);

        // The request is central too: the restricted approver does not find it.
        Assert.Equal(PayProfileChangeFailure.NotFound,
            (await h.DecideAsync(central.RequestId.Value, PayProfileChangeDecision.Approved, scope: H.Restricted(H.Unit))).Failure);
        Assert.Equal(PayProfileChangeFailure.NotFound, (await h.CancelAsync(central.RequestId.Value, scope: H.Restricted(H.Unit))).Failure);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(central.RequestId.Value)).State);
    }

    // ------------------------------------------------------------------ approve

    [Fact]
    public async Task Approving_a_request_that_is_due_applies_it_through_the_applier_in_one_unit()
    {
        var (h, id) = await PendingAsync();

        var result = await h.ApproveAsync(id);

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.Applied, result.State);

        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Applied, request.State);
        Assert.Equal(H.Approver, request.DecidedBy);
        Assert.Equal(h.Clock.Now.UtcDateTime, request.DecidedAt);
        Assert.Equal(h.Clock.Now.UtcDateTime, request.AppliedAt);
        Assert.Null(request.LastApplyError);

        var profiles = await h.ProfilesAsync();
        Assert.Equal(2, profiles.Count);
        var (previous, next) = (profiles[0], profiles[1]);
        // The previous version closed the day before, so 1 November belongs to the new version alone.
        Assert.False(previous.IsActive);
        Assert.Equal(new DateTime(2026, 10, 31, 0, 0, 0, DateTimeKind.Utc), previous.EffectiveTo);
        Assert.Equal(H.Salary, previous.BaseSalary);
        Assert.True(next.IsActive);
        Assert.Equal(request.AppliedProfileId, next.Id);
        Assert.Equal(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc), next.EffectiveFrom);
        Assert.Null(next.EffectiveTo);
        Assert.Equal(5000m, next.BaseSalary);
        // Every field of the previous version is carried: the old upsert dropped these.
        Assert.Equal(h.GradeId, next.PayGradeId);
        Assert.Equal(H.Unit, next.OrgUnitId);
        Assert.Equal("law50", next.RegimeCode);

        await using (var db = h.Db())
        {
            var history = Assert.Single(db.EmployeePayProfileHistories);
            Assert.Equal(previous.Id, history.SourceProfileId);
            Assert.Equal(H.Salary, history.BaseSalary);
            Assert.Equal(previous.EffectiveTo, history.EffectiveTo);
            Assert.Equal(H.Reason, history.ChangeReason);
        }

        Assert.Equal(2, h.Events.Published.Count);
        var decided = Assert.IsType<PayProfileChangeDecidedEvent>(h.Events.Published[0]);
        Assert.Equal((nameof(PayProfileChangeDecision.Approved), nameof(PayProfileChangeState.Applied), H.Approver),
            (decided.Decision, decided.ResultingState, decided.DecidedBy));
        var applied = Assert.IsType<PayProfileChangeAppliedEvent>(h.Events.Published[1]);
        Assert.Equal((id, H.Employee, next.Id, (Guid?)previous.Id), (applied.RequestId, applied.EmployeeId, applied.NewProfileId, applied.PreviousProfileId));
        Assert.Equal((5000m, (decimal?)H.Salary, "G5", nameof(PayProfileChangeSource.Manual)),
            (applied.BaseSalary, applied.PreviousBaseSalary, applied.PayGradeCode, applied.Source));
        Assert.Equal(new DateOnly(2026, 11, 1), applied.EffectiveFrom);
    }

    [Fact]
    public async Task Approving_a_request_of_a_later_month_schedules_it_and_changes_no_profile()
    {
        var h = await new H().SeedAsync();
        var submitted = await h.SubmitAsync(effectiveFrom: NextMonth(h));
        h.Events.Published.Clear();

        var result = await h.DecideAsync(submitted.RequestId!.Value, PayProfileChangeDecision.Approved, "موافق");

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.Scheduled, result.State);
        var request = await h.RequestAsync(submitted.RequestId.Value);
        Assert.Equal(PayProfileChangeState.Scheduled, request.State);
        Assert.Equal(H.Approver, request.DecidedBy);
        Assert.Equal("موافق", request.DecisionNote);
        Assert.Null(request.AppliedAt);
        Assert.Null(request.AppliedProfileId);
        await h.AssertProfileUntouchedAsync();

        var decided = Assert.IsType<PayProfileChangeDecidedEvent>(Assert.Single(h.Events.Published));
        Assert.Equal((nameof(PayProfileChangeDecision.Approved), nameof(PayProfileChangeState.Scheduled)), (decided.Decision, decided.ResultingState));
    }

    [Theory]
    [InlineData(PayProfileChangeDecision.Approved)]
    [InlineData(PayProfileChangeDecision.Rejected)]
    public async Task The_requester_cannot_decide_his_own_request(PayProfileChangeDecision decision)
    {
        var (h, id) = await PendingAsync();

        // hr-admin holds both policies: the rule is the handler's, on the user, whatever his roles.
        var result = await h.DecideAsync(id, decision, "ملاحظة", actor: H.Processor);

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.SelfDecision, result.Failure);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("لا يجوز اعتماد الطلب من طالبه", result.Error);
        Assert.Equal(PayProfileChangeState.PendingApproval, result.State);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);
        Assert.Null((await h.RequestAsync(id)).DecidedBy);
        Assert.Empty(h.Events.Published);
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task Two_admins_can_request_and_approve_one_cannot_do_both()
    {
        var adminA = Guid.NewGuid();
        var adminB = Guid.NewGuid();
        var h = await new H().SeedAsync();
        var submitted = await h.SubmitAsync(actor: adminA);

        Assert.Equal(PayProfileChangeFailure.SelfDecision, (await h.ApproveAsync(submitted.RequestId!.Value, actor: adminA)).Failure);
        Assert.True((await h.ApproveAsync(submitted.RequestId.Value, actor: adminB)).Success);
    }

    [Fact]
    public async Task The_bounds_are_checked_again_at_the_approval()
    {
        var (h, id) = await PendingAsync(salary: 5800m);
        await h.SetGradeBoundsAsync(h.GradeId, 4000, 5500);   // the grade's maximum was lowered after the request

        var result = await h.ApproveAsync(id);

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.Invalid, result.Failure);
        Assert.Contains("5500", result.Error!, StringComparison.Ordinal);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);
        Assert.Empty(h.Events.Published);
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task An_approval_with_no_active_profile_left_is_refused()
    {
        var (h, id) = await PendingAsync();
        await using (var db = h.Db())
        {
            db.EmployeePayProfiles.Single().IsActive = false;   // a separation closed it meanwhile
            await db.SaveChangesAsync();
        }

        var result = await h.ApproveAsync(id);

        Assert.Equal(PayProfileChangeFailure.Invalid, result.Failure);
        Assert.Equal(PayProfileChangeMessages.NoActiveProfile, result.Error);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);
    }

    // ------------------------------------------------------------------ reject

    [Fact]
    public async Task Rejecting_needs_a_note_and_closes_the_request_without_touching_the_profile()
    {
        var (h, id) = await PendingAsync();

        var withoutNote = await h.DecideAsync(id, PayProfileChangeDecision.Rejected, "  ");
        Assert.Equal(PayProfileChangeFailure.Invalid, withoutNote.Failure);
        Assert.Equal(PayProfileChangeMessages.RejectNoteRequired, withoutNote.Error);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);
        Assert.Empty(h.Events.Published);

        var result = await h.DecideAsync(id, PayProfileChangeDecision.Rejected, "لا مرجع قانوني كافٍ");

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayProfileChangeState.Rejected, result.State);
        var request = await h.RequestAsync(id);
        Assert.Equal((PayProfileChangeState.Rejected, (Guid?)H.Approver, "لا مرجع قانوني كافٍ"), (request.State, request.DecidedBy, request.DecisionNote));
        await h.AssertProfileUntouchedAsync();
        var decided = Assert.IsType<PayProfileChangeDecidedEvent>(Assert.Single(h.Events.Published));
        Assert.Equal((nameof(PayProfileChangeDecision.Rejected), nameof(PayProfileChangeState.Rejected)), (decided.Decision, decided.ResultingState));

        // A rejected request is closed: the employee may get a new one.
        Assert.True((await h.SubmitAsync(salary: 5100m)).Success);
    }

    // ------------------------------------------------------------------ wrong state

    public static TheoryData<PayProfileChangeState> ClosedOrScheduled() =>
        new() { PayProfileChangeState.Scheduled, PayProfileChangeState.Applied, PayProfileChangeState.Rejected, PayProfileChangeState.Cancelled, PayProfileChangeState.Superseded };

    private static async Task<(H Harness, Guid RequestId)> InStateAsync(PayProfileChangeState state)
    {
        var h = await new H().SeedAsync();
        var future = state == PayProfileChangeState.Scheduled || state == PayProfileChangeState.Superseded;
        var id = (await h.SubmitAsync(effectiveFrom: future ? NextMonth(h) : null)).RequestId!.Value;
        switch (state)
        {
            case PayProfileChangeState.Scheduled:
                Assert.True((await h.ApproveAsync(id)).Success);
                break;
            case PayProfileChangeState.Applied:
                Assert.True((await h.ApproveAsync(id)).Success);
                break;
            case PayProfileChangeState.Rejected:
                Assert.True((await h.DecideAsync(id, PayProfileChangeDecision.Rejected, "رفض")).Success);
                break;
            case PayProfileChangeState.Cancelled:
                Assert.True((await h.CancelAsync(id)).Success);
                break;
            case PayProfileChangeState.Superseded:
                Assert.True((await h.SystemChangeAsync(PayProfileChangeSource.Promotion)).Success);
                break;
        }

        Assert.Equal(state, (await h.RequestAsync(id)).State);
        h.Events.Published.Clear();
        return (h, id);
    }

    [Theory]
    [MemberData(nameof(ClosedOrScheduled))]
    public async Task A_request_that_is_not_pending_cannot_be_decided(PayProfileChangeState state)
    {
        var (h, id) = await InStateAsync(state);
        var profilesBefore = (await h.ProfilesAsync()).Count;

        foreach (var decision in new[] { PayProfileChangeDecision.Approved, PayProfileChangeDecision.Rejected })
        {
            var result = await h.DecideAsync(id, decision, "ملاحظة");

            Assert.False(result.Success);
            Assert.Equal(PayProfileChangeFailure.Conflict, result.Failure);
            Assert.Equal(409, result.StatusCode);
            Assert.Equal(state, result.State);
            Assert.Equal(state, (await h.RequestAsync(id)).State);
        }

        Assert.Empty(h.Events.Published);
        Assert.Equal(profilesBefore, (await h.ProfilesAsync()).Count);
    }

    [Fact]
    public async Task An_unknown_request_is_not_found()
    {
        var h = await new H().SeedAsync();
        Assert.Equal(PayProfileChangeFailure.NotFound, (await h.ApproveAsync(Guid.NewGuid())).Failure);
        Assert.Equal(PayProfileChangeFailure.NotFound, (await h.CancelAsync(Guid.NewGuid())).Failure);
    }

    [Fact]
    public async Task A_request_of_another_branch_is_not_found_by_a_restricted_approver()
    {
        var (h, id) = await PendingAsync();

        var foreign = await h.DecideAsync(id, PayProfileChangeDecision.Approved, scope: H.Restricted(H.ForeignUnit));

        Assert.Equal(PayProfileChangeFailure.NotFound, foreign.Failure);
        Assert.Equal(PayProfileChangeFailure.NotFound, (await h.CancelAsync(id, actorCanApprove: true, scope: H.Restricted(H.ForeignUnit))).Failure);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);
        await h.AssertProfileUntouchedAsync();
        // His own branch: he decides.
        Assert.True((await h.DecideAsync(id, PayProfileChangeDecision.Approved, scope: H.Restricted(H.Unit))).Success);
    }

    // ------------------------------------------------------------------ cancel

    [Fact]
    public async Task The_requester_cancels_his_pending_request_with_a_reason()
    {
        var (h, id) = await PendingAsync();

        Assert.Equal(PayProfileChangeFailure.Invalid, (await h.CancelAsync(id, reason: " ")).Failure);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);

        var result = await h.CancelAsync(id, "أُدخل الراتب خطأ");

        Assert.True(result.Success, result.Error);
        var request = await h.RequestAsync(id);
        Assert.Equal((PayProfileChangeState.Cancelled, (Guid?)H.Processor, "أُدخل الراتب خطأ"), (request.State, request.CancelledBy, request.CancelReason));
        Assert.Equal(h.Clock.Now.UtcDateTime, request.CancelledAt);
        Assert.Empty(h.Events.Published);
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task A_scheduled_request_can_be_cancelled_and_keeps_its_approver()
    {
        var (h, id) = await InStateAsync(PayProfileChangeState.Scheduled);

        var result = await h.CancelAsync(id, "أُلغي القرار", actor: H.Approver, actorCanApprove: true);

        Assert.True(result.Success, result.Error);
        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Cancelled, request.State);
        Assert.Equal(H.Approver, request.DecidedBy);      // who approved it
        Assert.Equal(H.Approver, request.CancelledBy);    // who cancelled it
        await h.AssertProfileUntouchedAsync();
        // Cancelled: the activation never applies it.
        h.Clock.Now = h.Clock.Now.AddMonths(2);
        Assert.Equal(0, (await h.ActivateAsync()).Applied);
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task Another_processor_cannot_cancel_a_request_an_approver_can()
    {
        var (h, id) = await PendingAsync();

        var other = await h.CancelAsync(id, actor: H.OtherProcessor, actorCanApprove: false);
        Assert.False(other.Success);
        Assert.Equal(PayProfileChangeFailure.Forbidden, other.Failure);
        Assert.Equal(403, other.StatusCode);
        Assert.Equal(PayProfileChangeState.PendingApproval, (await h.RequestAsync(id)).State);

        Assert.True((await h.CancelAsync(id, actor: H.Approver, actorCanApprove: true)).Success);
    }

    [Theory]
    [InlineData(PayProfileChangeState.Applied)]
    [InlineData(PayProfileChangeState.Rejected)]
    [InlineData(PayProfileChangeState.Cancelled)]
    [InlineData(PayProfileChangeState.Superseded)]
    public async Task A_closed_request_cannot_be_cancelled(PayProfileChangeState state)
    {
        var (h, id) = await InStateAsync(state);

        var result = await h.CancelAsync(id, actor: H.Approver, actorCanApprove: true);

        Assert.Equal(PayProfileChangeFailure.Conflict, result.Failure);
        Assert.Equal(state, (await h.RequestAsync(id)).State);
    }

    // ------------------------------------------------------------------ activation

    [Fact]
    public async Task A_scheduled_request_is_applied_once_when_its_date_comes_and_not_before()
    {
        var (h, id) = await InStateAsync(PayProfileChangeState.Scheduled);   // from 1 December 2026

        var early = await h.ActivateAsync();
        Assert.Equal((0, 0, 0), (early.Due, early.Applied, early.Blocked));
        Assert.Equal(PayProfileChangeState.Scheduled, (await h.RequestAsync(id)).State);
        await h.AssertProfileUntouchedAsync();

        h.Clock.Now = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);
        var first = await h.ActivateAsync();
        var second = await h.ActivateAsync();

        Assert.Equal((1, 1, 0, 0), (first.Due, first.Applied, first.Blocked, first.BlockedTotal));
        Assert.Equal((0, 0), (second.Due, second.Applied));
        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Applied, request.State);
        Assert.Equal(H.Approver, request.DecidedBy);                 // the approval stands; the activation is the system's
        Assert.Equal(h.Clock.Now.UtcDateTime, request.AppliedAt);
        var profiles = await h.ProfilesAsync();
        Assert.Equal(2, profiles.Count);                             // once, though the cycle ran twice
        Assert.Equal(new DateTime(2026, 11, 30, 0, 0, 0, DateTimeKind.Utc), profiles[0].EffectiveTo);
        Assert.Equal(new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), profiles[1].EffectiveFrom);
        Assert.Equal(1, await h.HistoryCountAsync());
        // The activation publishes the application only: the decision was published when it was made.
        Assert.IsType<PayProfileChangeAppliedEvent>(Assert.Single(h.Events.Published));
    }

    [Fact]
    public async Task A_refused_activation_keeps_the_request_scheduled_with_its_reason_until_the_cause_is_gone()
    {
        var (h, id) = await InStateAsync(PayProfileChangeState.Scheduled);   // from 1 December 2026
        var run = await h.AddRunAsync(new DateOnly(2026, 12, 1), PayrollRunType.Regular, PayrollRunStatus.Calculated, frozen: true);
        h.Clock.Now = new DateTimeOffset(2026, 12, 2, 6, 0, 0, TimeSpan.Zero);

        var blocked = await h.ActivateAsync();

        Assert.Equal((1, 0, 1, 1), (blocked.Due, blocked.Applied, blocked.Blocked, blocked.BlockedTotal));
        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Scheduled, request.State);
        Assert.Contains("2026/12", request.LastApplyError!, StringComparison.Ordinal);
        Assert.Matches(@"\p{IsArabic}", request.LastApplyError!);
        await h.AssertProfileUntouchedAsync();
        Assert.Empty(h.Events.Published);

        // The same refusal the next cycle: still one blocked request, nothing applied.
        var again = await h.ActivateAsync();
        Assert.Equal((1, 0, 1, 1), (again.Due, again.Applied, again.Blocked, again.BlockedTotal));

        // The run is reversed: the request is applied and its reason cleared.
        await h.SetRunStatusAsync(run, PayrollRunStatus.Reversed);
        var applied = await h.ActivateAsync();
        Assert.Equal((1, 1, 0, 0), (applied.Due, applied.Applied, applied.Blocked, applied.BlockedTotal));
        request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Applied, request.State);
        Assert.Null(request.LastApplyError);
    }

    [Fact]
    public async Task The_bounds_are_checked_again_at_the_activation()
    {
        var h = await new H().SeedAsync();
        var id = (await h.SubmitAsync(salary: 5800m, effectiveFrom: NextMonth(h))).RequestId!.Value;
        Assert.True((await h.ApproveAsync(id)).Success);
        await h.SetGradeBoundsAsync(h.GradeId, 4000, 5500);
        h.Clock.Now = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);

        var summary = await h.ActivateAsync();

        Assert.Equal((1, 0, 1), (summary.Due, summary.Applied, summary.Blocked));
        var request = await h.RequestAsync(id);
        Assert.Equal(PayProfileChangeState.Scheduled, request.State);
        Assert.Contains("5500", request.LastApplyError!, StringComparison.Ordinal);
        await h.AssertProfileUntouchedAsync();
    }

    [Fact]
    public async Task The_applier_refuses_a_request_in_the_wrong_state_for_its_path()
    {
        var (h, id) = await PendingAsync();
        await using var db = h.Db();
        var request = db.PayProfileChangeRequests.Single(r => r.Id == id);

        // A pending request is not applied by the activation path (no approval), and a closed one by neither.
        var notScheduled = await h.Applier(db).ApplyAsync(request, approval: null);
        Assert.Equal(PayProfileChangeFailure.Conflict, notScheduled.Failure);
        Assert.Equal(PayProfileChangeState.PendingApproval, request.State);
        await h.AssertProfileUntouchedAsync();
    }

    // ------------------------------------------------------------------ one applier, one active profile

    [Fact]
    public async Task After_every_scenario_the_employee_has_exactly_one_active_profile_and_versions_that_touch_without_overlap()
    {
        var h = await new H().SeedAsync();
        // November: applied. December: scheduled then activated. January: an automatic change.
        Assert.True((await h.ApproveAsync((await h.SubmitAsync(salary: 4800m)).RequestId!.Value)).Success);
        var december = (await h.SubmitAsync(salary: 5000m, effectiveFrom: new DateOnly(2026, 12, 1))).RequestId!.Value;
        Assert.True((await h.ApproveAsync(december)).Success);
        h.Clock.Now = new DateTimeOffset(2026, 12, 1, 6, 0, 0, TimeSpan.Zero);
        Assert.Equal(1, (await h.ActivateAsync()).Applied);
        h.Clock.Now = new DateTimeOffset(2027, 1, 3, 6, 0, 0, TimeSpan.Zero);
        Assert.True((await h.SystemChangeAsync(PayProfileChangeSource.Promotion, salary: 5400m)).Success);

        var profiles = await h.ProfilesAsync();
        Assert.Equal(4, profiles.Count);
        Assert.Single(profiles, p => p.IsActive);
        Assert.Equal([4500m, 4800m, 5000m, 5400m], profiles.Select(p => p.BaseSalary));
        for (var i = 0; i < profiles.Count - 1; i++)
        {
            Assert.False(profiles[i].IsActive);
            // Each version ends the day before the next one starts: no gap, no day in two versions.
            Assert.Equal(profiles[i + 1].EffectiveFrom.AddDays(-1), profiles[i].EffectiveTo);
        }

        Assert.All(profiles, p => Assert.Equal((H.Unit, "law50"), (p.OrgUnitId!.Value, p.RegimeCode)));
        Assert.Equal(3, await h.HistoryCountAsync());
    }
}
