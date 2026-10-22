using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

/// <summary>
/// HCM-028 (H3): progress of RECRUITMENT-APPEAL-REVIEW on RecruitmentAppeals/Details. Decisions are explicit forms
/// (outcome / request information), so <see cref="WorkflowPanelViewModel.CanApprove"/> only tells whether the current
/// user is the assigned reviewer on an active step.
/// </summary>
public sealed class RecruitmentAppealWorkflowUiService(HRCoreDbContext db)
{
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
        var userId = WorkflowPanelBuilder.ParseUserId(user);

        return WorkflowPanelBuilder.Build(
            instance,
            definition,
            user,
            userId,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = appeal.Status == RecruitmentAppealStatus.Pending,
                BlockInitiatorOnLaterSteps = false,
                AdditionalBlockReason = (_, _, _) => RecruitmentAppealAccess.CanDecide(appeal, user)
                    ? null
                    : RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage
            });
    }
}
