using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Payroll.Application.Features.PayProfileChanges;
using Payroll.Application.Features.ReferenceChanges;
using Payroll.Application.Options;
using Payroll.Domain.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;
using Payroll.Infrastructure.Repositories;
using H = HRManagement.UnitTests.Iam053SalaryChangeHarness;

namespace HRManagement.UnitTests;

/// <summary>
/// PLAN-53 (§4, D-53-4, P-8): salary scales and grades change by a request another person approves. Create, update and delete for both
/// types; the change is applied in the approval's save and not before; deciding one's own request is refused; a grade a pay profile or
/// an open salary change request names, and a scale that has grades, are not deleted (at the request and again at the approval); new
/// bounds change no pay profile; and <c>Payroll:MasterData:RequireApproval=false</c> is honoured in Development only.
/// </summary>
public sealed class Iam053ReferenceChangeTests
{
    private const string Reason = "تعديل السلّم بمرسوم الزيادة";
    private const string LegalReference = "المرسوم التشريعي 7 لعام 2026";

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Payroll.Web";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Fixture
    {
        public required H Harness { get; init; }
        public PayrollMasterDataOptions Options { get; } = new();
        public string EnvironmentName { get; set; } = "Production";
        public Guid ScaleId { get; set; }

        public async Task<PayrollReferenceChangeResult> SubmitAsync(
            PayrollReferenceEntityType type, PayrollReferenceOperation operation, Guid? id = null, PayGradeChange? grade = null,
            SalaryScaleChange? scale = null, string? reason = Reason, string? legalReference = LegalReference, Guid? actor = null)
        {
            await using var db = Harness.Db();
            var repository = new PayrollRepository(db);
            return await new SubmitPayrollReferenceChangeHandler(
                    repository, new PayrollReferenceChangeApplier(repository), Harness.Calendar,
                    Microsoft.Extensions.Options.Options.Create(Options), new Environment(EnvironmentName))
                .Handle(new SubmitPayrollReferenceChangeCommand(type, operation, id, grade, scale, reason, legalReference, actor ?? H.Processor, "processor"),
                    CancellationToken.None);
        }

        public async Task<PayrollReferenceChangeResult> DecideAsync(Guid id, PayProfileChangeDecision decision, string? note = null, Guid? actor = null)
        {
            await using var db = Harness.Db();
            var repository = new PayrollRepository(db);
            return await new DecidePayrollReferenceChangeHandler(repository, new PayrollReferenceChangeApplier(repository), Harness.Calendar)
                .Handle(new DecidePayrollReferenceChangeCommand(id, decision, note, actor ?? H.Approver, "approver"), CancellationToken.None);
        }

        public Task<PayrollReferenceChangeResult> ApproveAsync(Guid id) => DecideAsync(id, PayProfileChangeDecision.Approved);

        public async Task<PayrollReferenceChangeResult> CancelAsync(Guid id, string? reason = "أُدخل خطأ", Guid? actor = null, bool approver = false)
        {
            await using var db = Harness.Db();
            return await new CancelPayrollReferenceChangeHandler(new PayrollRepository(db), Harness.Calendar)
                .Handle(new CancelPayrollReferenceChangeCommand(id, reason, actor ?? H.Processor, "user", approver), CancellationToken.None);
        }

        public async Task<PayrollReferenceChangeRequest> RequestAsync(Guid id)
        {
            await using var db = Harness.Db();
            return await db.PayrollReferenceChangeRequests.AsNoTracking().SingleAsync(r => r.Id == id);
        }

        public async Task<PayGrade?> GradeAsync(Guid id)
        {
            await using var db = Harness.Db();
            return await db.PayGrades.AsNoTracking().FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<SalaryScale?> ScaleAsync(Guid id)
        {
            await using var db = Harness.Db();
            return await db.SalaryScales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<(int Scales, int Grades, int Requests)> CountsAsync()
        {
            await using var db = Harness.Db();
            return (await db.SalaryScales.CountAsync(), await db.PayGrades.CountAsync(), await db.PayrollReferenceChangeRequests.CountAsync());
        }

        /// <summary>The seeded grade G5 (4000 to 6000, base 4500) with one field changed.</summary>
        public PayGradeChange G5(decimal? max = null, decimal? min = null, decimal? baseSalary = null, string? code = null) =>
            new(ScaleId, code ?? "G5", "الخامسة", baseSalary ?? 4500m, min ?? H.GradeMin, max ?? H.GradeMax);
    }

    /// <summary>The salary change seed: scale S-2026 with G5 (the employee's grade) and G6 (nobody's).</summary>
    private static async Task<Fixture> StartAsync()
    {
        var harness = await new H().SeedAsync();
        await using var db = harness.Db();
        return new Fixture { Harness = harness, ScaleId = await db.SalaryScales.Select(s => s.Id).SingleAsync() };
    }

    private static SalaryScaleChange Scale(string code = "S-2027", string name = "سلم 2027", bool active = true, DateOnly? to = null) =>
        new(code, name, new DateOnly(2027, 1, 1), to, active);

    // ------------------------------------------------------------------ grades

    [Fact]
    public async Task A_grade_is_created_only_when_another_person_approves_the_request()
    {
        var f = await StartAsync();
        var proposal = new PayGradeChange(f.ScaleId, "G7", "السابعة", 8500m, 8000m, 10_000m);

        var submitted = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Create, grade: proposal);

        Assert.True(submitted.Success, submitted.Error);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, submitted.State);
        Assert.Equal((1, 2, 1), await f.CountsAsync());                  // no grade yet
        var pending = await f.RequestAsync(submitted.RequestId!.Value);
        Assert.Null(pending.EntityId);
        Assert.Null(pending.CurrentJson);
        Assert.Equal(proposal, PayrollReferenceChangeJson.Read<PayGradeChange>(pending.ProposedJson));
        Assert.Equal((Reason, LegalReference, H.Processor), (pending.Reason, pending.LegalReference, pending.RequestedBy));
        Assert.Contains("G7", pending.EntityLabel!, StringComparison.Ordinal);

        var approved = await f.ApproveAsync(pending.Id);

        Assert.True(approved.Success, approved.Error);
        Assert.Equal(PayrollReferenceChangeState.Applied, approved.State);
        var request = await f.RequestAsync(pending.Id);
        Assert.Equal((PayrollReferenceChangeState.Applied, (Guid?)H.Approver), (request.State, request.DecidedBy));
        Assert.Equal(f.Harness.Clock.Now.UtcDateTime, request.AppliedAt);
        var created = await f.GradeAsync(request.EntityId!.Value);
        Assert.Equal(proposal, PayGradeChange.From(created!));
        Assert.Equal((1, 3, 1), await f.CountsAsync());
    }

    [Fact]
    public async Task A_grades_bounds_change_at_the_approval_and_not_before_and_the_request_keeps_who_asked_who_approved_and_why()
    {
        var f = await StartAsync();

        var submitted = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m));

        Assert.True(submitted.Success, submitted.Error);
        // Before the approval the approved value stands: what HR.Core's offer check reads is the grade's row, untouched.
        Assert.Equal(H.GradeMax, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
        var pending = await f.RequestAsync(submitted.RequestId!.Value);
        Assert.Equal(f.G5(), PayrollReferenceChangeJson.Read<PayGradeChange>(pending.CurrentJson));
        Assert.Equal(f.G5(max: 7500m), PayrollReferenceChangeJson.Read<PayGradeChange>(pending.ProposedJson));

        Assert.True((await f.DecideAsync(pending.Id, PayProfileChangeDecision.Approved, "موافق")).Success);

        Assert.Equal(7500m, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
        var request = await f.RequestAsync(pending.Id);
        Assert.Equal((H.Processor, (Guid?)H.Approver, LegalReference, "موافق"),
            (request.RequestedBy, request.DecidedBy, request.LegalReference, request.DecisionNote));
        Assert.Equal(f.Harness.GradeId, request.EntityId);
    }

    [Fact]
    public async Task New_bounds_change_no_pay_profile_and_the_profile_they_leave_outside_is_listed()
    {
        var f = await StartAsync();   // the employee: 4500 on G5 (4000 to 6000)
        await using (var db = f.Harness.Db())
            Assert.Empty(await db.EmployeePayProfiles.Where(EmployeePayProfileRules.OutsideGradeBounds).ToListAsync());

        // The grade's minimum is raised above the employee's salary.
        var id = (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId,
            f.G5(min: 5000m, baseSalary: 5500m))).RequestId!.Value;
        Assert.True((await f.ApproveAsync(id)).Success);

        // Rule 1: the profile is exactly as it was, with no new version and no history row.
        await f.Harness.AssertProfileUntouchedAsync();
        // And it shows where the pay profiles screen lists the salaries outside their grade's bounds.
        await using (var db = f.Harness.Db())
        {
            var outside = Assert.Single(await db.EmployeePayProfiles.Include(p => p.PayGrade).Where(EmployeePayProfileRules.OutsideGradeBounds).ToListAsync());
            Assert.Equal(H.Employee, outside.EmployeeId);
            Assert.True(EmployeePayProfileRules.IsOutsideGradeBounds(outside.BaseSalary, outside.PayGrade!.MinSalary, outside.PayGrade.MaxSalary));
        }

        // It is corrected by a salary change request, which the new bounds now judge.
        Assert.Equal(PayProfileChangeFailure.Invalid, (await f.Harness.SubmitAsync(salary: 4800m)).Failure);
        Assert.True((await f.Harness.SubmitAsync(salary: 5200m)).Success);
    }

    [Fact]
    public async Task A_grade_nobody_names_is_deleted_by_an_approved_request()
    {
        var f = await StartAsync();

        var submitted = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Delete, f.Harness.HigherGradeId);

        Assert.True(submitted.Success, submitted.Error);
        Assert.NotNull(await f.GradeAsync(f.Harness.HigherGradeId));       // still there while pending
        var pending = await f.RequestAsync(submitted.RequestId!.Value);
        Assert.Null(pending.ProposedJson);
        Assert.NotNull(pending.CurrentJson);

        Assert.True((await f.ApproveAsync(pending.Id)).Success);
        Assert.Null(await f.GradeAsync(f.Harness.HigherGradeId));
    }

    [Theory]
    [InlineData("active-profile")]
    [InlineData("closed-profile")]
    [InlineData("open-request-proposes-it")]
    public async Task A_grade_in_use_is_not_deleted(string use)
    {
        var f = await StartAsync();
        var grade = f.Harness.GradeId;
        switch (use)
        {
            case "closed-profile":
                await using (var db = f.Harness.Db())
                {
                    var profile = db.EmployeePayProfiles.Single();
                    profile.IsActive = false;                               // closed, and it still names the grade
                    await db.SaveChangesAsync();
                }
                break;
            case "open-request-proposes-it":
                grade = f.Harness.HigherGradeId;                            // no profile names G6, a pending salary change proposes it
                Assert.True((await f.Harness.SubmitAsync(salary: 6500m, grade: grade)).Success);
                break;
        }

        var result = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Delete, grade);

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.Conflict, result.Failure);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal(PayrollReferenceChangeMessages.GradeInUse, result.Error);
        Assert.NotNull(await f.GradeAsync(grade));
        Assert.Equal(0, (await f.CountsAsync()).Requests);
    }

    [Fact]
    public async Task A_grade_that_comes_into_use_after_the_request_is_not_deleted_at_the_approval()
    {
        var f = await StartAsync();
        var id = (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Delete, f.Harness.HigherGradeId)).RequestId!.Value;
        // Meanwhile the employee is promoted to that grade.
        Assert.True((await f.Harness.SystemChangeAsync(
            PayProfileChangeSource.Promotion, PayProfileChangeKind.BaseSalaryAndGrade, 6500m, f.Harness.HigherGradeId)).Success);

        var result = await f.ApproveAsync(id);

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.Conflict, result.Failure);
        Assert.Equal(PayrollReferenceChangeMessages.GradeInUse, result.Error);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, (await f.RequestAsync(id)).State);
        Assert.NotNull(await f.GradeAsync(f.Harness.HigherGradeId));
    }

    // ------------------------------------------------------------------ scales

    [Fact]
    public async Task A_salary_scale_is_created_changed_and_deleted_by_approved_requests()
    {
        var f = await StartAsync();

        // Create.
        var create = await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Create, scale: Scale());
        Assert.True(create.Success, create.Error);
        Assert.Equal(1, (await f.CountsAsync()).Scales);
        Assert.True((await f.ApproveAsync(create.RequestId!.Value)).Success);
        var scaleId = (await f.RequestAsync(create.RequestId.Value)).EntityId!.Value;
        var created = await f.ScaleAsync(scaleId);
        Assert.Equal(("S-2027", "سلم 2027", new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc), true),
            (created!.Code, created.NameAr, created.EffectiveFrom, created.IsActive));

        // Update: the name, an end date, inactive. Unchanged until approved.
        var update = await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Update, scaleId,
            scale: Scale(name: "سلم 2027 المعدّل", active: false, to: new DateOnly(2027, 12, 31)));
        Assert.True(update.Success, update.Error);
        Assert.Equal("سلم 2027", (await f.ScaleAsync(scaleId))!.NameAr);
        Assert.True((await f.ApproveAsync(update.RequestId!.Value)).Success);
        var changed = await f.ScaleAsync(scaleId);
        Assert.Equal(("سلم 2027 المعدّل", false, (DateTime?)new DateTime(2027, 12, 31, 0, 0, 0, DateTimeKind.Utc)),
            (changed!.NameAr, changed.IsActive, changed.EffectiveTo));

        // Delete: it has no grades.
        var delete = await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Delete, scaleId);
        Assert.True(delete.Success, delete.Error);
        Assert.NotNull(await f.ScaleAsync(scaleId));
        Assert.True((await f.ApproveAsync(delete.RequestId!.Value)).Success);
        Assert.Null(await f.ScaleAsync(scaleId));
    }

    [Fact]
    public async Task A_scale_that_has_grades_is_not_deleted_at_the_request_nor_at_the_approval()
    {
        var f = await StartAsync();

        var atRequest = await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Delete, f.ScaleId);
        Assert.Equal(PayProfileChangeFailure.Conflict, atRequest.Failure);
        Assert.Equal(PayrollReferenceChangeMessages.ScaleHasGrades, atRequest.Error);

        // A new scale has no grades when its deletion is requested, and gets one before the approval.
        var created = await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Create, scale: Scale());
        Assert.True((await f.ApproveAsync(created.RequestId!.Value)).Success);
        var scaleId = (await f.RequestAsync(created.RequestId.Value)).EntityId!.Value;
        var delete = await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Delete, scaleId);
        Assert.True(delete.Success, delete.Error);
        var grade = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Create,
            grade: new PayGradeChange(scaleId, "G1", "الأولى", 3000m, 2500m, 4000m), actor: H.OtherProcessor);
        Assert.True((await f.ApproveAsync(grade.RequestId!.Value)).Success);

        var atApproval = await f.ApproveAsync(delete.RequestId!.Value);

        Assert.Equal(PayProfileChangeFailure.Conflict, atApproval.Failure);
        Assert.Equal(PayrollReferenceChangeMessages.ScaleHasGrades, atApproval.Error);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, (await f.RequestAsync(delete.RequestId.Value)).State);
        Assert.NotNull(await f.ScaleAsync(scaleId));
        Assert.Equal(3, (await f.CountsAsync()).Grades);    // the scale's grade was not deleted with it (the old relation cascaded)
    }

    // ------------------------------------------------------------------ the second person

    [Theory]
    [InlineData(PayProfileChangeDecision.Approved)]
    [InlineData(PayProfileChangeDecision.Rejected)]
    public async Task The_requester_cannot_decide_his_own_reference_request(PayProfileChangeDecision decision)
    {
        var f = await StartAsync();
        var id = (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m))).RequestId!.Value;

        var result = await f.DecideAsync(id, decision, "ملاحظة", actor: H.Processor);

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.SelfDecision, result.Failure);
        Assert.Equal(403, result.StatusCode);
        Assert.Equal("لا يجوز اعتماد الطلب من طالبه", result.Error);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, (await f.RequestAsync(id)).State);
        Assert.Equal(H.GradeMax, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
    }

    [Fact]
    public async Task Rejecting_needs_a_note_and_leaves_the_record_as_it_was()
    {
        var f = await StartAsync();
        var id = (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m))).RequestId!.Value;

        Assert.Equal(PayrollReferenceChangeMessages.RejectNoteRequired, (await f.DecideAsync(id, PayProfileChangeDecision.Rejected)).Error);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, (await f.RequestAsync(id)).State);

        var rejected = await f.DecideAsync(id, PayProfileChangeDecision.Rejected, "المرجع لا يشمل هذه الدرجة");
        Assert.True(rejected.Success, rejected.Error);
        var request = await f.RequestAsync(id);
        Assert.Equal((PayrollReferenceChangeState.Rejected, (Guid?)H.Approver, "المرجع لا يشمل هذه الدرجة"), (request.State, request.DecidedBy, request.DecisionNote));
        Assert.Equal(H.GradeMax, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
        Assert.Null(request.AppliedAt);

        // Closed: it cannot be decided again, and the record may get a new request.
        Assert.Equal(PayProfileChangeFailure.Conflict, (await f.ApproveAsync(id)).Failure);
        Assert.True((await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7000m))).Success);
    }

    [Fact]
    public async Task The_requester_or_an_approver_cancels_a_pending_request_and_nobody_else()
    {
        var f = await StartAsync();
        var id = (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m))).RequestId!.Value;

        Assert.Equal(PayProfileChangeFailure.Forbidden, (await f.CancelAsync(id, actor: H.OtherProcessor)).Failure);
        Assert.Equal(PayProfileChangeFailure.Invalid, (await f.CancelAsync(id, reason: " ")).Failure);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, (await f.RequestAsync(id)).State);

        Assert.True((await f.CancelAsync(id, "خطأ في الرقم")).Success);
        var request = await f.RequestAsync(id);
        Assert.Equal((PayrollReferenceChangeState.Cancelled, (Guid?)H.Processor, "خطأ في الرقم"), (request.State, request.CancelledBy, request.CancelReason));
        Assert.Equal(H.GradeMax, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
        Assert.Equal(PayProfileChangeFailure.Conflict, (await f.CancelAsync(id)).Failure);
        Assert.Equal(PayProfileChangeFailure.Conflict, (await f.ApproveAsync(id)).Failure);

        var second = (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m))).RequestId!.Value;
        Assert.True((await f.CancelAsync(second, actor: H.Approver, approver: true)).Success);
    }

    // ------------------------------------------------------------------ validation, at the request and at the approval

    public static TheoryData<string> BrokenProposals() => new()
    {
        "no-reason", "no-legal-reference", "long-legal-reference", "min-above-base", "base-above-max", "negative", "no-code", "unknown-scale",
        "nothing-changes", "no-proposal", "scale-no-code", "scale-dates",
    };

    [Theory]
    [MemberData(nameof(BrokenProposals))]
    public async Task A_broken_proposal_is_refused_as_invalid_and_writes_nothing(string rule)
    {
        var f = await StartAsync();
        var grade = PayrollReferenceEntityType.PayGrade;
        var update = PayrollReferenceOperation.Update;
        var result = rule switch
        {
            "no-reason" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(max: 7500m), reason: " "),
            "no-legal-reference" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(max: 7500m), legalReference: null),
            "long-legal-reference" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(max: 7500m), legalReference: new string('م', 201)),
            "min-above-base" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(min: 4600m)),
            "base-above-max" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(max: 4400m)),
            "negative" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(min: -1m)),
            "no-code" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(code: "  ")),
            "unknown-scale" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5(max: 7500m) with { SalaryScaleId = Guid.NewGuid() }),
            "nothing-changes" => await f.SubmitAsync(grade, update, f.Harness.GradeId, f.G5()),
            "no-proposal" => await f.SubmitAsync(grade, update, f.Harness.GradeId),
            "scale-no-code" => await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Create, scale: Scale(code: "")),
            "scale-dates" => await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Create, scale: Scale(to: new DateOnly(2026, 12, 31))),
            _ => throw new ArgumentOutOfRangeException(nameof(rule)),
        };

        Assert.False(result.Success);
        Assert.Equal(PayProfileChangeFailure.Invalid, result.Failure);
        Assert.Equal(400, result.StatusCode);
        Assert.Matches(@"\p{IsArabic}", result.Error!);
        Assert.Equal((1, 2, 0), await f.CountsAsync());
        Assert.Equal(f.G5(), PayGradeChange.From((await f.GradeAsync(f.Harness.GradeId))!));
    }

    [Fact]
    public async Task A_code_already_taken_is_a_conflict_at_the_request_and_again_at_the_approval()
    {
        var f = await StartAsync();
        // G6 exists in the scale: G5 cannot take its code.
        var taken = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(code: "G6"));
        Assert.Equal((PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.GradeCodeTaken), (taken.Failure, taken.Error));
        Assert.Equal(PayProfileChangeFailure.Conflict,
            (await f.SubmitAsync(PayrollReferenceEntityType.SalaryScale, PayrollReferenceOperation.Create, scale: Scale(code: "S-2026"))).Failure);

        // Two requests to create a grade with the same code are both accepted (neither record exists); the second approval is refused.
        var g7 = new PayGradeChange(f.ScaleId, "G7", "السابعة", 8500m, 8000m, 10_000m);
        var first = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Create, grade: g7);
        var second = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Create, grade: g7 with { GradeNameAr = "السابعة مكررة" }, actor: H.OtherProcessor);
        Assert.True(first.Success && second.Success);
        Assert.True((await f.ApproveAsync(first.RequestId!.Value)).Success);

        var refused = await f.ApproveAsync(second.RequestId!.Value);

        Assert.Equal((PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.GradeCodeTaken), (refused.Failure, refused.Error));
        Assert.Equal(409, refused.StatusCode);
        Assert.Equal(PayrollReferenceChangeState.PendingApproval, (await f.RequestAsync(second.RequestId.Value)).State);
        Assert.Equal(3, (await f.CountsAsync()).Grades);
    }

    [Fact]
    public async Task One_open_request_per_record_and_an_unknown_record_is_not_found()
    {
        var f = await StartAsync();
        Assert.True((await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m))).Success);

        var again = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7000m), actor: H.OtherProcessor);
        Assert.Equal((PayProfileChangeFailure.Conflict, PayrollReferenceChangeMessages.OpenRequestExists), (again.Failure, again.Error));
        // The same holds for a deletion of the record that already has a pending change (and G5 is in use anyway).
        Assert.Equal(PayProfileChangeFailure.Conflict,
            (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Delete, f.Harness.GradeId)).Failure);

        foreach (var operation in new[] { PayrollReferenceOperation.Update, PayrollReferenceOperation.Delete })
        {
            var unknown = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, operation, Guid.NewGuid(), f.G5(max: 7500m));
            Assert.Equal(PayProfileChangeFailure.NotFound, unknown.Failure);
            Assert.Equal(404, unknown.StatusCode);
        }

        Assert.Equal(PayProfileChangeFailure.NotFound, (await f.ApproveAsync(Guid.NewGuid())).Failure);
        Assert.Equal(PayProfileChangeFailure.NotFound, (await f.CancelAsync(Guid.NewGuid())).Failure);
        Assert.Equal(1, (await f.CountsAsync()).Requests);
    }

    // ------------------------------------------------------------------ Payroll:MasterData:RequireApproval

    [Fact]
    public async Task With_the_approval_switched_off_in_development_a_request_is_applied_when_it_is_submitted_by_the_same_code()
    {
        var f = await StartAsync();
        f.Options.RequireApproval = false;
        f.EnvironmentName = Environments.Development;

        var result = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m));

        Assert.True(result.Success, result.Error);
        Assert.Equal(PayrollReferenceChangeState.Applied, result.State);
        Assert.Equal(7500m, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
        var request = await f.RequestAsync(result.RequestId!.Value);
        Assert.Null(request.DecidedBy);                    // nobody approved it: the record says so
        Assert.NotNull(request.AppliedAt);
        // The rules still hold: a grade in use is not deleted even there.
        Assert.Equal(PayProfileChangeFailure.Conflict,
            (await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Delete, f.Harness.GradeId)).Failure);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Acceptance")]
    public async Task Outside_development_the_setting_is_ignored_and_the_service_warns(string environment)
    {
        var f = await StartAsync();
        f.Options.RequireApproval = false;
        f.EnvironmentName = environment;

        var result = await f.SubmitAsync(PayrollReferenceEntityType.PayGrade, PayrollReferenceOperation.Update, f.Harness.GradeId, f.G5(max: 7500m));

        Assert.Equal(PayrollReferenceChangeState.PendingApproval, result.State);
        Assert.Equal(H.GradeMax, (await f.GradeAsync(f.Harness.GradeId))!.MaxSalary);
        Assert.True(PayrollMasterDataApproval.IsIgnored(f.Options, new Environment(environment)));
        Assert.True(PayrollMasterDataApproval.IsRequired(f.Options, new Environment(environment)));
    }

    [Fact]
    public void The_setting_is_true_by_default_and_then_nothing_is_ignored_anywhere()
    {
        var options = new PayrollMasterDataOptions();
        Assert.True(options.RequireApproval);
        foreach (var environment in new[] { Environments.Development, "Production", "Acceptance" })
        {
            Assert.True(PayrollMasterDataApproval.IsRequired(options, new Environment(environment)));
            Assert.False(PayrollMasterDataApproval.IsIgnored(options, new Environment(environment)));
        }

        // No environment at all (a host built by hand) is not Development: the approval is required.
        Assert.True(PayrollMasterDataApproval.IsRequired(new PayrollMasterDataOptions { RequireApproval = false }, null));
        Assert.False(PayrollMasterDataApproval.IsRequired(new PayrollMasterDataOptions { RequireApproval = false }, new Environment(Environments.Development)));
    }
}
