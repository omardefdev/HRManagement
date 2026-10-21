using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.Services;

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
                MissingWorkflowWarning = "لم يُنشأ مسار مراجعة للاستئناف. فعّل PolicyEngine:Core.Recruitment أو استخدم بيئة المختبر (IsLabScale)."
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
                AdditionalBlockReason = (_, _, actorId) =>
                {
                    if (!RecruitmentAppealWorkflowRules.CanDecideAppeal(appeal, actorId))
                        return "غير مصرح: يجب أن تكون المراجع المستقل المعيّن.";

                    return null;
                }
            });
    }
}
