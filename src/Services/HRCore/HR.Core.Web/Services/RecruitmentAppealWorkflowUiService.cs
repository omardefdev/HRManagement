using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-028 (H3): progress of RECRUITMENT-APPEAL-REVIEW on RecruitmentAppeals/Details. Decisions are explicit forms
/// (outcome / request information), so <see cref="WorkflowPanelViewModel.CanApprove"/> only tells whether the current
/// user is the assigned reviewer on an active step.
/// </summary>
public sealed class RecruitmentAppealWorkflowUiService(HRCoreDbContext db, WorkflowApprovalEvaluator? evaluator = null)
{
    private readonly WorkflowApprovalEvaluator _evaluator = evaluator ?? WorkflowApprovalEvaluator.CreateDefault(db);

    public async Task<WorkflowPanelViewModel?> BuildPanelAsync(
        RecruitmentAppeal? appeal,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        if (appeal is null || appeal.Status != RecruitmentAppealStatus.Pending)
            return null;

        if (appeal.WorkflowInstanceId is null)
            return new WorkflowPanelViewModel
            {
                MissingWorkflowWarning = appeal.IndependentReviewerEmployeeId is null && appeal.IndependentReviewerId is null
                    ? "لم يُعيَّن مراجع مستقل بعد — يبدأ مسار المراجعة عند التعيين."
                    : appeal.IsAwaitingInfo
                        ? "طلب المراجع معلومات إضافية — يُستأنف المسار بعد استيفائها."
                        : "تعذّر إنشاء مسار مراجعة (لا يوجد تعريف RECRUITMENT-APPEAL-REVIEW) — يبت المراجع المعيّن مباشرة."
            };

        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, appeal.WorkflowInstanceId, ct);

        // PLAN-25 (G-04): the evaluator's reject-side check (step role «compliance» + the assigned reviewer). The approve
        // side would also require the explicit outcome, which the decision form itself supplies.
        var panel = await WorkflowPanelBuilder.BuildAsync(
            instance,
            definition,
            user,
            new WorkflowPanelBuildOptions { IsAwaitingApproval = appeal.Status == RecruitmentAppealStatus.Pending },
            _evaluator,
            ct);
        var canDecide = panel.CanReject;
        panel.CanApprove = canDecide;
        panel.BlockReason = canDecide ? null : panel.BlockReason;
        return panel;
    }
}
