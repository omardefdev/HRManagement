using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Recruitment;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Legal;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Hcm031TestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-031 (PLAN-31, P1, AC-HCM-31a): competition rules, lines and requisition grouping, the approval workflow, the
/// announcement effects, and the single-requisition path left untouched.
/// </summary>
public sealed class Hcm031CompetitionLifecycleTests
{
    // ---- pure rules -------------------------------------------------------------------------------------------------

    [Fact]
    public void Legal_inputs_are_all_required_and_range_checked()
    {
        Assert.Equal(CompetitionWorkflowRules.LegalInputsMissingMessage,
            CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { PassMark = null }));
        Assert.Equal(CompetitionWorkflowRules.LegalInputsMissingMessage,
            CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { TieBreakOrder = [] }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { ObjectionWindowDays = 0 }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { ObjectionWindowDays = 61 }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { ReserveValidityMonths = 37 }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { PassMark = 101m }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs() with { ReconcileThreshold = 0m }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(
            LegalInputs() with { TieBreakOrder = [CompetitionTieBreak.ExamScore, CompetitionTieBreak.ExamScore] }));
        Assert.NotNull(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs(maxLines: 3), lineCount: 2));
        Assert.Null(CompetitionWorkflowRules.ValidateLegalInputs(LegalInputs(), lineCount: 2));
    }

    [Fact]
    public void Tie_break_order_round_trips_and_ignores_unknown_or_repeated_names()
    {
        var order = new[] { CompetitionTieBreak.InterviewScore, CompetitionTieBreak.AppliedAt };
        var text = CompetitionWorkflowRules.FormatTieBreakOrder(order);
        Assert.Equal("InterviewScore,AppliedAt", text);
        Assert.Equal(order, CompetitionWorkflowRules.ParseTieBreakOrder(text));
        Assert.Equal([CompetitionTieBreak.ExamScore], CompetitionWorkflowRules.ParseTieBreakOrder("ExamScore,Bogus,ExamScore"));
    }

    [Fact]
    public void Status_transitions_follow_the_lifecycle_and_cancel_is_possible_until_finalized()
    {
        Assert.True(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.Draft, RecruitmentCompetitionStatus.PendingApproval));
        Assert.True(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.PendingApproval, RecruitmentCompetitionStatus.Draft));
        Assert.True(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.ResultsPublished, RecruitmentCompetitionStatus.Finalized));
        Assert.False(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.Draft, RecruitmentCompetitionStatus.Announced));
        Assert.False(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.Approved, RecruitmentCompetitionStatus.Finalized));
        Assert.True(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.ResultsPublished, RecruitmentCompetitionStatus.Cancelled));
        Assert.False(CompetitionWorkflowRules.CanTransition(RecruitmentCompetitionStatus.Finalized, RecruitmentCompetitionStatus.Cancelled));
    }

    [Fact]
    public void Criteria_shape_ignores_item_order_and_name_but_not_weight_or_stage()
    {
        var a = ExamCriteria();
        var reordered = a.Reverse().Select(i => i with { NameAr = i.NameAr + " *" }).ToList();
        Assert.Equal(CompetitionWorkflowRules.CriteriaShape(a), CompetitionWorkflowRules.CriteriaShape(reordered));
        Assert.NotEqual(CompetitionWorkflowRules.CriteriaShape(a), CompetitionWorkflowRules.CriteriaShape(ExamCriteria(60m)));
    }

    // ---- create / lines / attach ------------------------------------------------------------------------------------

    [Fact]
    public async Task Create_requires_an_active_legal_source_and_every_legal_input()
    {
        using var db = Hcm027TestSupport.CreateContext();
        await Hcm027TestSupport.SeedReferenceDataAsync(db);
        var lifecycle = CreateLifecycle(db);

        var (none, missingSource) = await lifecycle.CreateAsync(Input() with { LegalSourceId = null }, Creator);
        Assert.Null(none);
        Assert.Contains("المرجع القانوني", missingSource, StringComparison.Ordinal);

        var (_, unknownSource) = await lifecycle.CreateAsync(Input() with { LegalSourceId = "SYR-NOT-REAL" }, Creator);
        Assert.NotNull(unknownSource);

        // No defaults outside Development/acceptance: an empty legal input is refused, never filled in by code.
        var (_, missingInputs) = await lifecycle.CreateAsync(
            Input() with { LegalInputs = new CompetitionLegalInputs(null, null, null, null, null, null, null) }, Creator);
        Assert.Equal(CompetitionWorkflowRules.LegalInputsMissingMessage, missingInputs);

        var (created, error) = await lifecycle.CreateAsync(Input(), Creator);
        Assert.Null(error);
        Assert.Equal(RecruitmentCompetitionStatus.Draft, created!.Status);
        Assert.StartsWith("CMP-", created.Number);
        Assert.Equal("ExamScore,AppliedAt", created.TieBreakOrder);
    }

    [Fact]
    public void Production_and_staging_configuration_ship_no_competition_legal_defaults()
    {
        Assert.True(new RecruitmentOptions().Competition.Defaults.IsEmpty);
        var webRoot = Path.Combine(FindRepoRoot(), "src", "Services", "HRCore", "HR.Core.Web");
        foreach (var file in new[] { "appsettings.json", "appsettings.Production.json", "appsettings.Staging.json" })
        {
            var path = Path.Combine(webRoot, file);
            if (!System.IO.File.Exists(path))
                continue;
            var text = System.IO.File.ReadAllText(path);
            Assert.DoesNotContain("\"Competition\"", text);
        }
    }

    [Fact]
    public async Task Attach_refuses_an_unapproved_requisition_with_an_Arabic_message_AC_HCM_31a_negative()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 1);
        var line = await db.CompetitionLines.SingleAsync(l => l.CompetitionId == competition.Id);

        var draft = await SeedApprovedRequisitionAsync(db, line.GradeCode, status: RequisitionStatus.Draft);
        var (ok, error) = await lifecycle.AttachRequisitionAsync(line.Id, draft.Id, Creator);
        Assert.False(ok);
        Assert.Equal(CompetitionWorkflowRules.RequisitionNotApprovedMessage, error);

        var pending = await SeedApprovedRequisitionAsync(db, line.GradeCode, status: RequisitionStatus.PendingApproval);
        Assert.Equal(CompetitionWorkflowRules.RequisitionNotApprovedMessage,
            (await lifecycle.AttachRequisitionAsync(line.Id, pending.Id, Creator)).Error);

        var published = await SeedApprovedRequisitionAsync(db, line.GradeCode);
        published.PublishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        Assert.Equal(CompetitionWorkflowRules.RequisitionAlreadyPublishedMessage,
            (await lifecycle.AttachRequisitionAsync(line.Id, published.Id, Creator)).Error);

        var otherGrade = await SeedApprovedRequisitionAsync(db, "G9");
        Assert.Equal(CompetitionWorkflowRules.GradeMismatchMessage,
            (await lifecycle.AttachRequisitionAsync(line.Id, otherGrade.Id, Creator)).Error);

        Assert.Null(draft.CompetitionLineId);
        Assert.Equal(1, (await db.CompetitionLines.SingleAsync(l => l.Id == line.Id)).RequiredCount);
    }

    [Fact]
    public async Task Required_count_follows_attach_and_detach_and_a_requisition_joins_one_line_only()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 3, 2);
        var lines = await db.CompetitionLines.Where(l => l.CompetitionId == competition.Id).OrderBy(l => l.LineNo).ToListAsync();
        Assert.Equal([3, 2], lines.Select(l => l.RequiredCount));

        var attached = await db.RecruitmentRequisitions.FirstAsync(r => r.CompetitionLineId == lines[0].Id);
        var sameGradeLine = (await lifecycle.AddLineAsync(competition.Id, new CompetitionLineInput("سطر إضافي", lines[0].GradeCode, null, null, null), Creator)).Line!;
        Assert.Equal(CompetitionWorkflowRules.RequisitionAlreadyAttachedMessage,
            (await lifecycle.AttachRequisitionAsync(sameGradeLine.Id, attached.Id, Creator)).Error);

        var (detached, error) = await lifecycle.DetachRequisitionAsync(attached.Id, Creator);
        Assert.True(detached, error);
        Assert.Equal(2, (await db.CompetitionLines.AsNoTracking().SingleAsync(l => l.Id == lines[0].Id)).RequiredCount);
        Assert.Contains(await lifecycle.GetAttachableRequisitionsAsync(lines[0].Id), r => r.Id == attached.Id);
    }

    // ---- submit / approve -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Submit_requires_a_requisition_on_every_line_and_an_exam_item_with_one_criteria_shape()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 1);

        var empty = (await lifecycle.AddLineAsync(competition.Id, new CompetitionLineInput("سطر فارغ", "G7", null, null, null), Creator)).Line!;
        var (ok, error) = await lifecycle.SubmitForApprovalAsync(competition.Id, Creator, "creator");
        Assert.False(ok);
        Assert.Contains("لا يتضمن أي طلب شاغر", error, StringComparison.Ordinal);

        var noExam = await SeedApprovedRequisitionAsync(db, "G7",
            [new AssessmentCriteriaItem("INT", "المقابلة", 100m, 10m, AssessmentStage.Interview)]);
        Assert.True((await lifecycle.AttachRequisitionAsync(empty.Id, noExam.Id, Creator)).Success);
        Assert.Contains(CompetitionWorkflowRules.CriteriaNeedExamMessage,
            (await lifecycle.SubmitForApprovalAsync(competition.Id, Creator, "creator")).Error, StringComparison.Ordinal);

        Assert.True((await lifecycle.DetachRequisitionAsync(noExam.Id, Creator)).Success);
        var otherShape = await SeedApprovedRequisitionAsync(db, "G7", ExamCriteria(60m));
        Assert.True((await lifecycle.AttachRequisitionAsync(empty.Id, otherShape.Id, Creator)).Success);
        Assert.Equal(CompetitionWorkflowRules.CriteriaShapeMismatchMessage,
            (await lifecycle.SubmitForApprovalAsync(competition.Id, Creator, "creator")).Error);
        Assert.Equal(RecruitmentCompetitionStatus.Draft, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Submit_freezes_line_criteria_and_starts_the_competition_approval_workflow()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 3, 2);

        var (ok, error) = await lifecycle.SubmitForApprovalAsync(competition.Id, Creator, "creator");
        Assert.True(ok, error);

        var saved = await db.RecruitmentCompetitions.AsNoTracking().Include(c => c.Lines).SingleAsync();
        Assert.Equal(RecruitmentCompetitionStatus.PendingApproval, saved.Status);
        Assert.All(saved.Lines, l => Assert.NotNull(l.CriteriaVersionId));
        Assert.Equal(CompetitionLifecycleService.ApprovalWorkflowCode,
            await Hcm027TestSupport.GetWorkflowCodeAsync(db, saved.ApprovalWorkflowInstanceId));
        Assert.False((await lifecycle.AddLineAsync(competition.Id, new CompetitionLineInput("x", "G5", null, null, null), Creator)).Error is null);
    }

    [Fact]
    public async Task The_creator_cannot_approve_and_two_other_people_approve_AC_HCM_31a()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 3, 2);
        Assert.True((await lifecycle.SubmitForApprovalAsync(competition.Id, Creator, "creator")).Success);
        var instanceId = (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).ApprovalWorkflowInstanceId!.Value;
        var actions = CreateActionService(db);

        var self = await actions.ApproveAsync(instanceId, Actor(Creator), null, null);
        Assert.False(self.Success);
        Assert.Equal("لا يمكن للمنشئ اعتماد مسابقته الخاصة.", self.Error);

        Assert.True((await actions.ApproveAsync(instanceId, Actor(Reviewer), null, null)).Success);
        Assert.Equal(RecruitmentCompetitionStatus.PendingApproval, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
        Assert.True((await actions.ApproveAsync(instanceId, Actor(Approver, "hr-management-approver"), null, null)).Success);

        var approved = await db.RecruitmentCompetitions.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentCompetitionStatus.Approved, approved.Status);
        Assert.NotNull(approved.ApprovedAt);
    }

    [Fact]
    public async Task A_rejected_approval_returns_the_competition_to_draft_with_the_reason()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 1);
        Assert.True((await lifecycle.SubmitForApprovalAsync(competition.Id, Creator, "creator")).Success);
        var instanceId = (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).ApprovalWorkflowInstanceId!.Value;

        var rejected = await CreateActionService(db).RejectAsync(instanceId, Actor(Reviewer), "يجب مراجعة شروط التقديم");
        Assert.True(rejected.Success, rejected.Error);

        var saved = await db.RecruitmentCompetitions.AsNoTracking().Include(c => c.Lines).SingleAsync();
        Assert.Equal(RecruitmentCompetitionStatus.Draft, saved.Status);
        Assert.Equal("يجب مراجعة شروط التقديم", saved.ReturnReason);
        Assert.Null(saved.ApprovalWorkflowInstanceId);
        Assert.All(saved.Lines, l => Assert.Null(l.CriteriaVersionId));
    }

    // ---- announce ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Announce_creates_one_posting_and_publishes_attached_requisitions_without_postings_AC_HCM_31a()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 3, 2);

        Assert.Equal(RecruitmentCompetitionStatus.Announced, competition.Status);
        Assert.NotNull(competition.PublicJobPostingId);
        var posting = await db.PublicJobPostings.AsNoTracking().SingleAsync();
        Assert.Null(posting.RequisitionId);
        Assert.True(posting.IsPublished);
        Assert.Equal(competition.ApplicationClosingDate, posting.ClosingDate);

        var requisitions = await db.RecruitmentRequisitions.AsNoTracking().Where(r => r.CompetitionLineId != null).ToListAsync();
        Assert.Equal(5, requisitions.Count);
        Assert.All(requisitions, r =>
        {
            Assert.Equal(RequisitionStatus.Published, r.Status);
            Assert.Null(r.PublicJobPostingId);
            Assert.Null(r.InternalVacancyId);
            Assert.Equal(competition.ApplicationClosingDate, r.ApplicationClosingDate);
        });
        Assert.Empty(await db.InternalVacancies.ToListAsync());
    }

    [Fact]
    public async Task An_attached_requisition_cannot_be_published_closed_or_cancelled_on_its_own()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var lifecycle = CreateLifecycle(db);
        var competition = await SeedDraftCompetitionAsync(db, lifecycle, 1);
        var attached = await db.RecruitmentRequisitions.SingleAsync(r => r.CompetitionLineId != null);
        var requisitions = CreateRequisitionLifecycle(db);

        var (published, publishError) = await requisitions.PublishAsync(attached, Creator, "creator",
            applicationClosingDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
        Assert.False(published);
        Assert.Equal(CompetitionWorkflowRules.AttachedRequisitionPublishMessage, publishError);
        Assert.Equal(CompetitionWorkflowRules.AttachedRequisitionPublishMessage,
            (await requisitions.PublishInternalAsync(attached, Creator, "creator")).Error);
        Assert.Equal(CompetitionWorkflowRules.AttachedRequisitionChangeMessage,
            (await requisitions.CancelAsync(attached.Id, "إلغاء تجريبي للطلب", Creator, actorCanApprove: true)).Error);

        await ApproveAsync(db, lifecycle, competition.Id);
        Assert.True((await lifecycle.AnnounceAsync(competition.Id, Creator)).Success);
        Assert.Equal(CompetitionWorkflowRules.AttachedRequisitionChangeMessage, (await requisitions.CloseAsync(attached.Id)).Error);
    }

    [Fact]
    public async Task A_requisition_outside_any_competition_publishes_exactly_as_before()
    {
        using var db = Hcm027TestSupport.CreateContext();
        await Hcm027TestSupport.SeedReferenceDataAsync(db);
        var requisition = await SeedApprovedRequisitionAsync(db);

        var (ok, error) = await CreateRequisitionLifecycle(db).PublishAsync(requisition, Creator, "creator",
            applicationClosingDate: DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));

        Assert.True(ok, error);
        var saved = await db.RecruitmentRequisitions.AsNoTracking().SingleAsync(r => r.Id == requisition.Id);
        Assert.Equal(RequisitionStatus.Published, saved.Status);
        Assert.NotNull(saved.PublicJobPostingId);
        Assert.Equal(saved.Id, (await db.PublicJobPostings.AsNoTracking().SingleAsync()).RequisitionId);
        Assert.Empty(await db.RecruitmentCompetitions.ToListAsync());
    }

    [Fact]
    public async Task Close_intake_and_cancel_close_the_posting_and_cancel_rejects_open_applications()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        var application = new RecruitmentApplication
        {
            ApplicantFullName = "متقدم",
            AppliedPosition = competition.Title,
            CompetitionId = competition.Id,
            NationalIdHash = new string('a', 64),
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Submitted
        };
        db.RecruitmentApplications.Add(application);
        await db.SaveChangesAsync();
        var lifecycle = CreateLifecycle(db);

        Assert.True((await lifecycle.CloseIntakeAsync(competition.Id, Creator)).Success);
        Assert.False((await db.PublicJobPostings.AsNoTracking().SingleAsync()).IsPublished);
        Assert.Equal(RecruitmentCompetitionStatus.IntakeClosed, (await db.RecruitmentCompetitions.AsNoTracking().SingleAsync()).Status);
        Assert.All(await db.RecruitmentRequisitions.AsNoTracking().Where(r => r.CompetitionLineId != null).ToListAsync(),
            r => Assert.NotNull(r.IntakeClosedAt));

        Assert.NotNull((await lifecycle.CancelAsync(competition.Id, "قصير", Creator)).Error);
        var (cancelled, error) = await lifecycle.CancelAsync(competition.Id, "إلغاء المسابقة بقرار الإدارة", Creator);
        Assert.True(cancelled, error);
        var rejected = await db.RecruitmentApplications.AsNoTracking().SingleAsync();
        Assert.Equal(RecruitmentApplicationStatus.Rejected, rejected.Status);
        Assert.Equal(RecruitmentRejectionKind.RequisitionCancelled, rejected.RejectionKind);
        Assert.Null(await CompetitionLifecycleService.AttachedRequisitionBlockAsync(
            db, (await db.RecruitmentRequisitions.FirstAsync(r => r.CompetitionLineId != null)).Id, default));
    }

    [Fact]
    public async Task One_application_per_person_per_competition_is_enforced_by_the_database()
    {
        using var db = Hcm027TestSupport.CreateContext();
        var competition = await SeedAnnouncedCompetitionAsync(db, 1);
        RecruitmentApplication New() => new()
        {
            ApplicantFullName = "متقدم",
            AppliedPosition = competition.Title,
            CompetitionId = competition.Id,
            NationalIdHash = new string('b', 64),
            AppliedAt = DateTime.UtcNow,
            Status = RecruitmentApplicationStatus.Submitted
        };
        db.RecruitmentApplications.Add(New());
        await db.SaveChangesAsync();
        db.RecruitmentApplications.Add(New());
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task The_competition_approval_workflow_is_seeded_with_two_steps()
    {
        using var db = Hcm027TestSupport.CreateContext();
        await WorkflowPackSeeder.SeedAsync(db);
        var definition = await db.WorkflowDefinitions.AsNoTracking().SingleAsync(d => d.Code == CompetitionLifecycleService.ApprovalWorkflowCode);
        Assert.Equal("RecruitmentCompetition", definition.DomainEntityType);
        Assert.Contains("hr-core", definition.StepsJson, StringComparison.Ordinal);
        Assert.Contains("hr-management-approver", definition.StepsJson, StringComparison.Ordinal);
    }

    private static RecruitmentRequisitionLifecycleService CreateRequisitionLifecycle(HRCoreDbContext db) =>
        new(
            db,
            Mock.Of<IPolicyOrchestrator>(),
            new RecruitmentLegalSourceValidator(new LegislationRegisterProvider(db)),
            Hcm027TestSupport.CreateStarter(db),
            Hcm027TestSupport.Config(labScale: false),
            Microsoft.Extensions.Options.Options.Create(new RecruitmentOptions { RequireCriteriaBeforePublish = false }));

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "HRManagement.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
