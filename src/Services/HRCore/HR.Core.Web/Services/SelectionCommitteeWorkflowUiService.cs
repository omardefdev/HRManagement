using System.Security.Claims;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Web.ViewModels;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.Services;

public sealed class SelectionCommitteeWorkflowUiService(
    HRCoreDbContext db,
    EstablishmentScopeService establishmentScope)
{
    public async Task<SelectionCommitteeDetailsViewModel> BuildDetailsAsync(
        SelectionCommitteeSession session,
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var (instance, definition) = await WorkflowPanelDataLoader.LoadAsync(db, session.WorkflowInstanceId, ct);
        var userId = WorkflowPanelBuilder.ParseUserId(user);
        var actorEmployeeId = HrOrgScopeVisibility.GetEmployeeId(user);
        var accessScope = await establishmentScope.GetScopeAsync(user, ct);
        var orgUnitId = session.Requisition?.FundedPosition?.OrgUnitId;

        var workflowPanel = WorkflowPanelBuilder.Build(
            instance,
            definition,
            user,
            userId,
            new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = session.ApprovedAt is null && session.WorkflowInstanceId is not null,
                EntityCreatedBy = session.CreatedBy,
                CreatorSoDMessage = SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
                MissingWorkflowWarning = session.WorkflowInstanceId is null && session.ApprovedAt is null
                    ? null
                    : null,
                AdditionalBlockReason = (inst, step, actorId) =>
                {
                    var orgBlock = WorkflowInboxOrgScope.GetBlockReason(accessScope, orgUnitId);
                    if (orgBlock is not null)
                        return orgBlock;

                    // HCM-028: same rule as SelectionCommitteeLifecycleService.ApproveStepAsync.
                    return SelectionCommitteeRules.ValidateApproval(
                        inst.CurrentStepLevel, session.Members, session.CreatedBy, actorId, actorEmployeeId);
                }
            });

        return new SelectionCommitteeDetailsViewModel
        {
            Session = session,
            CanSubmit = session.ApprovedAt is null
                && !session.IsLocked
                && session.WorkflowInstanceId is null
                && session.Members.Any(),
            Workflow = workflowPanel
        };
    }
}
