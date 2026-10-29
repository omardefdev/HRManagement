using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Enums;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;

namespace HR.Core.Web.Services;

public sealed class RecruitmentRequisitionWorkflowUiService(
    HRCoreDbContext db,
    EstablishmentScopeService establishmentScope)
{
    public async Task<RecruitmentRequisitionDetailsViewModel> BuildDetailsAsync(
        RecruitmentRequisition requisition,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, requisition.WorkflowInstanceId, ct);
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        var accessScope = await establishmentScope.GetScopeAsync(user, ct);
        var orgUnitId = requisition.FundedPosition?.OrgUnitId;

        var workflowPanel = WorkflowPanelBuilder.Build(
            instance,
            definition,
            user,
            userId,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = requisition.Status == RequisitionStatus.PendingApproval,
                EntityCreatedBy = requisition.CreatedBy,
                MissingWorkflowWarning = requisition.WorkflowInstanceId is null
                    ? HR.Core.Infrastructure.Services.Workflow.ApprovalRestartRules.StuckWarning
                    : null,
                AdditionalBlockReason = (_, _, _) => WorkflowInboxOrgScope.GetBlockReason(accessScope, orgUnitId)
            });

        return new RecruitmentRequisitionDetailsViewModel
        {
            Requisition = requisition,
            Workflow = workflowPanel
        };
    }
}
