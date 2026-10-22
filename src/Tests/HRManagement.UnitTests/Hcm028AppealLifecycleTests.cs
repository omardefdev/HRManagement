using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Moq;
using static HRManagement.UnitTests.Hcm027TestSupport;
using static HRManagement.UnitTests.Hcm028AppealTestSupport;

namespace HRManagement.UnitTests;

/// <summary>
/// HCM-028 (H, G-09): one appeal path — filing without a reviewer, independent reviewer assignment (starts the
/// workflow), explicit outcomes decided only by the assigned compliance reviewer, info request → resubmit.
/// </summary>
public class Hcm028AppealLifecycleTests
{
    // ---------------------------------------------------------------- H1 filing

    [Fact]
    public async Task Filing_needs_no_reviewer_and_starts_no_workflow()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var appeal = await FileAsync(CreateService(db), app);

        var saved = await ReloadAsync(db, appeal.Id);
        Assert.Equal(RecruitmentAppealStatus.Pending, saved.Status);
        Assert.Equal(RecruitmentAppealFiledBy.HrOnBehalf, saved.FiledBy);
        Assert.Null(saved.IndependentReviewerEmployeeId);
        Assert.Null(saved.IndependentReviewerId);
        Assert.Null(saved.WorkflowInstanceId);
        Assert.Equal(RecruitmentAppealStatus.Pending, saved.RecruitmentApplication!.AppealStatus);
        Assert.Equal(0, await db.WorkflowInstances.CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == appeal.Id && l.Action == "AppealFiled"));
    }

    [Fact]
    public async Task Filing_requires_a_reason_of_twenty_characters_and_one_appeal_per_rejection()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);

        var (none, error) = await service.FileAppealAsync(app.Id, "سبب قصير", RecruitmentAppealFiledBy.HrOnBehalf, HrUserId);
        Assert.Null(none);
        Assert.Contains("20", error);

        await FileAsync(service, app);
        (none, error) = await service.FileAppealAsync(app.Id, Reason, RecruitmentAppealFiledBy.HrOnBehalf, HrUserId);
        Assert.Null(none);
        Assert.Equal(RecruitmentAppealWorkflowRules.AlreadyAppealedMessage, error);
    }

    // ---------------------------------------------------------------- H2 assignment

    [Fact]
    public async Task Assignment_starts_the_review_workflow()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, CreateService(db), app);

        var saved = await ReloadAsync(db, appeal.Id);
        Assert.Equal(reviewer.Id, saved.IndependentReviewerEmployeeId);
        Assert.Equal(HrUserId, saved.AssignedBy);
        Assert.NotNull(saved.AssignedAt);
        Assert.Equal(RecruitmentAppealService.AppealReviewWorkflowCode, await GetWorkflowCodeAsync(db, saved.WorkflowInstanceId));
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == appeal.Id && l.Action == "AppealReviewerAssigned"));
    }

    public enum ConflictKind { Applicant, CommitteeMember, InterviewPanel, Assessor, Filer, Assigner }

    [Theory]
    [InlineData(ConflictKind.Applicant)]
    [InlineData(ConflictKind.CommitteeMember)]
    [InlineData(ConflictKind.InterviewPanel)]
    [InlineData(ConflictKind.Assessor)]
    [InlineData(ConflictKind.Filer)]
    [InlineData(ConflictKind.Assigner)]
    public async Task Assignment_refuses_a_reviewer_who_is_not_independent(ConflictKind conflict)
    {
        await using var db = CreateContext();
        var (req, app) = await SeedRejectedAsync(db);
        var employee = await Hcm028TestSupport.SeedEmployeeAsync(db, "موظف غير مستقل");
        Guid? filerEmployee = null;
        Guid? assignerEmployee = null;

        switch (conflict)
        {
            case ConflictKind.Applicant:
                var vacancy = new InternalVacancy { Title = "داخلي", RequisitionId = req.Id };
                db.InternalVacancies.Add(vacancy);
                var internalApp = new InternalApplication { InternalVacancyId = vacancy.Id, EmployeeId = employee.Id, RequisitionId = req.Id };
                db.InternalApplications.Add(internalApp);
                app.SourceInternalApplicationId = internalApp.Id;
                break;
            case ConflictKind.CommitteeMember:
                var session = await Hcm028TestSupport.SeedSessionAsync(db, req);
                var (added, addError) = await new SelectionCommitteeService(db).AddMemberAsync(
                    session.Id, employee.Id, SelectionCommitteeMember.MemberRole, hasConflictOfInterest: true);
                Assert.True(added, addError); // a recused member is still excluded
                break;
            case ConflictKind.InterviewPanel:
                var slot = new RecruitmentInterviewSlot { RecruitmentApplicationId = app.Id, ScheduledAt = DateTime.UtcNow.AddDays(-20) };
                db.RecruitmentInterviewSlots.Add(slot);
                db.RecruitmentInterviewPanelMembers.Add(new RecruitmentInterviewPanelMember { SlotId = slot.Id, EmployeeId = employee.Id });
                break;
            case ConflictKind.Assessor:
                var criteria = new RecruitmentAssessmentCriteria { RequisitionId = req.Id, Title = "معايير" };
                db.RecruitmentAssessmentCriteria.Add(criteria);
                db.RecruitmentAssessmentScores.Add(new RecruitmentAssessmentScore
                {
                    RecruitmentApplicationId = app.Id,
                    CriteriaVersionId = criteria.Id,
                    ItemKey = "experience",
                    AssessorUserId = Guid.NewGuid(),
                    AssessorEmployeeId = employee.Id,
                    Score = 60
                });
                break;
            case ConflictKind.Filer:
                filerEmployee = employee.Id;
                break;
            case ConflictKind.Assigner:
                assignerEmployee = employee.Id;
                break;
        }

        await db.SaveChangesAsync();
        var service = CreateService(db);
        var (appeal, fileError) = await service.FileAppealAsync(
            app.Id, Reason, RecruitmentAppealFiledBy.HrOnBehalf, HrUserId, filerEmployee);
        Assert.True(appeal is not null, fileError);

        var (ok, error) = await service.AssignReviewerAsync(appeal!.Id, employee.Id, HrUserId, assignerEmployee);

        Assert.False(ok);
        Assert.Equal("يجب أن يكون المراجع مستقلاً عن اللجنة والمقابلة والتقييم.", error);
        var saved = await ReloadAsync(db, appeal.Id);
        Assert.Null(saved.IndependentReviewerEmployeeId);
        Assert.Equal(0, await db.WorkflowInstances.CountAsync());
    }

    [Fact]
    public async Task Reassignment_cancels_the_running_workflow_and_starts_a_new_one()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, _) = await FileAndAssignAsync(db, service, app);
        var firstInstance = (await ReloadAsync(db, appeal.Id)).WorkflowInstanceId!.Value;

        var second = await Hcm028TestSupport.SeedEmployeeAsync(db, "مراجع ثان");
        var (ok, error) = await service.AssignReviewerAsync(appeal.Id, second.Id, HrUserId);

        Assert.True(ok, error);
        var saved = await ReloadAsync(db, appeal.Id);
        Assert.Equal(second.Id, saved.IndependentReviewerEmployeeId);
        Assert.NotEqual(firstInstance, saved.WorkflowInstanceId);
        Assert.Equal(WorkflowStatus.Cancelled, (await db.WorkflowInstances.SingleAsync(i => i.Id == firstInstance)).Status);
        Assert.Equal(WorkflowStatus.InProgress, (await db.WorkflowInstances.SingleAsync(i => i.Id == saved.WorkflowInstanceId)).Status);
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == appeal.Id && l.Action == "AppealReviewerReassigned"));
    }

    // ---------------------------------------------------------------- H3 authorization

    [Fact]
    public async Task Decide_by_a_compliance_user_who_is_not_assigned_is_refused()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, _) = await FileAndAssignAsync(db, service, app);
        var other = await Hcm028TestSupport.SeedEmployeeAsync(db, "امتثال آخر");

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(OtherComplianceUserId, other.Id));

        Assert.False(ok);
        Assert.Equal("غير مصرح: يجب أن تكون المراجع المستقل المعيّن.", error);
        Assert.Equal(RecruitmentAppealStatus.Pending, (await ReloadAsync(db, appeal.Id)).Status);
    }

    [Fact]
    public async Task Decide_by_the_assigned_employee_without_the_compliance_role_is_refused()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id, HrManagementRoles.HrCore));

        Assert.False(ok);
        Assert.Equal(RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage, error);
    }

    [Fact]
    public async Task Generic_workflow_approval_without_an_outcome_is_refused()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var (appeal, _) = await FileAndAssignAsync(db, CreateService(db), app);
        var instanceId = (await ReloadAsync(db, appeal.Id)).WorkflowInstanceId!.Value;

        var result = await CreateActionService(db).ApproveAsync(
            instanceId, new WorkflowActionActor(ReviewerUserId, "reviewer", HrManagementRoles.Compliance), "ok", null);

        Assert.False(result.Success);
        Assert.Equal(RecruitmentAppealStatus.Pending, (await ReloadAsync(db, appeal.Id)).Status);
    }

    [Fact]
    public async Task Decide_requires_a_rationale_of_thirty_characters()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, "تعليل قصير", null, Reviewer(ReviewerUserId, reviewer.Id));

        Assert.False(ok);
        Assert.Contains("30", error);
    }

    // ---------------------------------------------------------------- H3 outcomes

    [Fact]
    public async Task Upheld_returns_the_application_to_review_and_releases_its_rank()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db, rank: 1);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id));

        Assert.True(ok, error);
        var saved = await ReloadAsync(db, appeal.Id);
        Assert.Equal(RecruitmentAppealStatus.Decided, saved.Status);
        Assert.Equal(RecruitmentAppealOutcome.Upheld, saved.Outcome);
        Assert.Equal(Rationale, saved.Decision);
        Assert.NotNull(saved.DecidedAt);
        var savedApp = saved.RecruitmentApplication!;
        Assert.Equal(RecruitmentApplicationStatus.UnderReview, savedApp.Status);
        Assert.Equal(RecruitmentAppealStatus.Decided, savedApp.AppealStatus);
        Assert.Null(savedApp.RejectedAt);
        Assert.Null(savedApp.RejectionKind);
        Assert.Null(savedApp.Rank);
        Assert.Equal(WorkflowStatus.Completed, (await db.WorkflowInstances.SingleAsync(i => i.Id == saved.WorkflowInstanceId)).Status);
        var audit = await db.AuditLogs.SingleAsync(l => l.EntityId == appeal.Id && l.Action == "AppealDecided");
        Assert.Equal(ReviewerUserId, audit.UserId);
    }

    [Fact]
    public async Task Dismissed_keeps_the_original_rejection()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var rejectedAt = (await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id)).RejectedAt;
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Dismissed, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id));

        Assert.True(ok, error);
        var savedApp = (await ReloadAsync(db, appeal.Id)).RecruitmentApplication!;
        Assert.Equal(RecruitmentApplicationStatus.Rejected, savedApp.Status);
        Assert.Equal(RecruitmentRejectionKind.SelectionDecision, savedApp.RejectionKind);
        Assert.Equal(rejectedAt, savedApp.RejectedAt);
        Assert.Equal(RecruitmentAppealStatus.Decided, savedApp.AppealStatus);
    }

    [Fact]
    public async Task Upheld_no_remedy_keeps_rejection_and_is_audited()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.UpheldNoRemedy, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id));

        Assert.True(ok, error);
        Assert.Equal(RecruitmentApplicationStatus.Rejected, (await ReloadAsync(db, appeal.Id)).RecruitmentApplication!.Status);
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == appeal.Id && l.Action == "AppealUpheldNoRemedy"));
    }

    [Fact]
    public async Task Upheld_rank_correction_ranks_the_candidate_with_the_rationale()
    {
        await using var db = CreateContext();
        var (req, app) = await SeedRejectedAsync(db, rank: 3);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.UpheldRankCorrection, Rationale, 2, Reviewer(ReviewerUserId, reviewer.Id));

        Assert.True(ok, error);
        var saved = await ReloadAsync(db, appeal.Id);
        Assert.Equal(2, saved.CorrectedRank);
        var savedApp = saved.RecruitmentApplication!;
        Assert.Equal(RecruitmentApplicationStatus.Ranked, savedApp.Status);
        Assert.Equal(2, savedApp.Rank);
        Assert.Equal(Rationale, savedApp.RankOverrideReason);
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == app.Id && l.Action == "AppealRankCorrection"));
    }

    [Fact]
    public async Task Rank_correction_needs_a_previous_rank_and_a_free_rank()
    {
        await using var db = CreateContext();
        var (req, unranked) = await SeedRejectedAsync(db, rank: null);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, unranked);
        var actor = Reviewer(ReviewerUserId, reviewer.Id);

        var (ok, error) = await service.DecideAsync(appeal.Id, RecruitmentAppealOutcome.UpheldRankCorrection, Rationale, 1, actor);
        Assert.False(ok);
        Assert.Equal(RecruitmentAppealWorkflowRules.RankCorrectionNotAllowedMessage, error);

        // A ranked appellant cannot take a rank held by another candidate.
        var (_, ranked) = await SeedRejectedAsync(db, suffix: "AQ", rank: 2);
        await SeedApplicationAsync(db, await db.RecruitmentRequisitions.SingleAsync(r => r.Id == ranked.RequisitionId),
            RecruitmentApplicationStatus.Ranked, 1, "صاحب الرتبة الأولى");
        var (appeal2, reviewer2) = await FileAndAssignAsync(db, service, ranked);
        (ok, error) = await service.DecideAsync(
            appeal2.Id, RecruitmentAppealOutcome.UpheldRankCorrection, Rationale, 1, Reviewer(ReviewerUserId, reviewer2.Id));
        Assert.False(ok);
        Assert.Equal(RecruitmentSelectionService.DuplicateRankMessage, error);
        Assert.Equal(RecruitmentAppealStatus.Pending, (await ReloadAsync(db, appeal2.Id)).Status);
        Assert.Null((await ReloadAsync(db, appeal2.Id)).Outcome);
    }

    [Theory]
    [InlineData(RequisitionStatus.Closed)]
    [InlineData(RequisitionStatus.Cancelled)]
    public async Task Closed_or_cancelled_requisition_limits_outcomes(RequisitionStatus status)
    {
        await using var db = CreateContext();
        var (req, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);
        req.Status = status;
        await db.SaveChangesAsync();
        var actor = Reviewer(ReviewerUserId, reviewer.Id);

        foreach (var refused in new[] { RecruitmentAppealOutcome.Upheld, RecruitmentAppealOutcome.UpheldRankCorrection })
        {
            var (ok, error) = await service.DecideAsync(appeal.Id, refused, Rationale, 1, actor);
            Assert.False(ok);
            Assert.Equal(RecruitmentAppealWorkflowRules.NoVacancyOutcomeMessage, error);
        }

        var (allowed, allowedError) = await service.DecideAsync(appeal.Id, RecruitmentAppealOutcome.UpheldNoRemedy, Rationale, null, actor);
        Assert.True(allowed, allowedError);
    }

    [Fact]
    public async Task Request_info_then_resubmit_restarts_the_review_for_the_same_reviewer()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);
        var actor = Reviewer(ReviewerUserId, reviewer.Id);
        var firstInstance = (await ReloadAsync(db, appeal.Id)).WorkflowInstanceId!.Value;

        var (asked, askError) = await service.RequestInfoAsync(appeal.Id, "يرجى إرفاق محضر المقابلة", actor);
        Assert.True(asked, askError);
        var waiting = await ReloadAsync(db, appeal.Id);
        Assert.Equal(RecruitmentAppealStatus.Pending, waiting.Status);
        Assert.Equal("يرجى إرفاق محضر المقابلة", waiting.InfoRequest);
        Assert.NotNull(waiting.InfoRequestedAt);
        Assert.Null(waiting.WorkflowInstanceId);
        Assert.True(waiting.IsAwaitingInfo);
        Assert.Equal(WorkflowStatus.Rejected, (await db.WorkflowInstances.SingleAsync(i => i.Id == firstInstance)).Status);

        var (blocked, blockedError) = await service.DecideAsync(appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, actor);
        Assert.False(blocked);
        Assert.Equal(RecruitmentAppealService.AwaitingInfoMessage, blockedError);

        var (resubmitted, resubmitError) = await service.ResubmitAsync(appeal.Id, "أُرفق محضر المقابلة الموقّع", HrUserId);
        Assert.True(resubmitted, resubmitError);
        var resumed = await ReloadAsync(db, appeal.Id);
        Assert.Equal(reviewer.Id, resumed.IndependentReviewerEmployeeId);
        Assert.NotNull(resumed.WorkflowInstanceId);
        Assert.NotEqual(firstInstance, resumed.WorkflowInstanceId);
        Assert.False(resumed.IsAwaitingInfo);

        var (decided, decideError) = await service.DecideAsync(appeal.Id, RecruitmentAppealOutcome.Dismissed, Rationale, null, actor);
        Assert.True(decided, decideError);
    }

    [Fact]
    public async Task Legacy_reviewer_user_id_still_decides()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var appeal = await FileAsync(service, app);
        // Pre-HCM-028 row: reviewer typed as a Keycloak user id, workflow started at filing.
        var tracked = await db.RecruitmentAppeals.SingleAsync(a => a.Id == appeal.Id);
        tracked.IndependentReviewerId = ReviewerUserId;
        tracked.WorkflowInstanceId = await CreateStarter(db).StartByWorkflowCodeAsync(
            RecruitmentAppealService.AppealReviewWorkflowCode, "RecruitmentAppeal", appeal.Id, tracked.TenantId, HrUserId, "hr");
        await db.SaveChangesAsync();

        var (wrong, wrongError) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(OtherComplianceUserId, null));
        Assert.False(wrong);
        Assert.Equal(RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage, wrongError);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(ReviewerUserId, null));
        Assert.True(ok, error);
        Assert.Equal(RecruitmentAppealStatus.Decided, (await ReloadAsync(db, appeal.Id)).Status);
    }

    [Fact]
    public async Task Without_a_workflow_definition_the_reviewer_decides_directly_and_it_is_audited()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db, starter: Mock.Of<IWorkflowStarter>()); // starter resolves nothing (H5)
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);
        Assert.Null((await ReloadAsync(db, appeal.Id)).WorkflowInstanceId);

        var (wrong, _) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(OtherComplianceUserId, Guid.NewGuid()));
        Assert.False(wrong);

        var (ok, error) = await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id));
        Assert.True(ok, error);
        Assert.Equal(RecruitmentApplicationStatus.UnderReview, (await ReloadAsync(db, appeal.Id)).RecruitmentApplication!.Status);
        Assert.Equal(1, await db.AuditLogs.CountAsync(l => l.EntityId == appeal.Id && l.Action == "AppealDecidedWithoutWorkflow"));
    }

    [Fact]
    public async Task A_new_rejection_after_an_upheld_appeal_can_be_appealed_again()
    {
        await using var db = CreateContext();
        var (_, app) = await SeedRejectedAsync(db);
        var service = CreateService(db);
        var (appeal, reviewer) = await FileAndAssignAsync(db, service, app);
        Assert.True((await service.DecideAsync(
            appeal.Id, RecruitmentAppealOutcome.Upheld, Rationale, null, Reviewer(ReviewerUserId, reviewer.Id))).Success);

        var (rejected, error) = await new RecruitmentSelectionService(db)
            .TransitionStatusAsync(app.Id, RecruitmentApplicationStatus.Rejected, false, "إعادة الرفض بعد إعادة التقييم");
        Assert.True(rejected, error);

        var saved = await db.RecruitmentApplications.AsNoTracking().SingleAsync(a => a.Id == app.Id);
        Assert.Equal(RecruitmentAppealStatus.None, saved.AppealStatus);
        Assert.Null(service.GetFilingError(saved, DateTime.UtcNow));
    }
}
