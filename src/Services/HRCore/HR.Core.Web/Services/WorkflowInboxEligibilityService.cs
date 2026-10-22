using System.Security.Claims;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.Services;

public sealed class WorkflowInboxEligibilityService
{
    public bool IsActionable(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        ClaimsPrincipal user,
        Guid? userId,
        WorkflowInboxEntityContext context,
        OrgAccessScope orgScope)
    {
        if (!WorkflowInboxOrgScope.Allows(orgScope, context.ScopeOrgUnitId))
            return false;

        if (userId.HasValue && context.EntityCreatedBy == userId)
            return false;

        var options = BuildOptions(instance, context, HrOrgScopeVisibility.GetEmployeeId(user));
        var panel = WorkflowPanelBuilder.Build(instance, definition, user, userId, options);
        return panel.CanApprove;
    }

    internal static WorkflowPanelBuildOptions BuildOptions(
        WorkflowInstance instance,
        WorkflowInboxEntityContext context,
        Guid? actorEmployeeId = null) =>
        instance.EntityType switch
        {
            "OfferLetter" => new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = context.IsAwaitingApproval,
                EntityCreatedBy = context.EntityCreatedBy,
                CreatorSoDMessage = context.CreatorSoDMessage
                    ?? "لا يمكن للمنشئ اعتماد عرضه الخاص."
            },
            "RecruitmentAppeal" => new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = context.IsAwaitingApproval,
                BlockInitiatorOnLaterSteps = false,
                AdditionalBlockReason = (_, _, actorId) =>
                {
                    if (context.Appeal is null
                        || !RecruitmentAppealWorkflowRules.CanDecideAppeal(context.Appeal, actorId))
                    {
                        return "غير مصرح: يجب أن تكون المراجع المستقل المعيّن.";
                    }

                    return null;
                }
            },
            "SelectionCommitteeSession" => new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = context.IsAwaitingApproval,
                EntityCreatedBy = context.EntityCreatedBy,
                BlockInitiatorOnLaterSteps = context.BlockInitiatorOnLaterSteps,
                CreatorSoDMessage = SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
                // HCM-028: same rule as SelectionCommitteeLifecycleService.ApproveStepAsync.
                AdditionalBlockReason = (inst, _, actorId) => context.CommitteeMembers is null
                    ? null
                    : SelectionCommitteeRules.ValidateApproval(
                        inst.CurrentStepLevel, context.CommitteeMembers, context.EntityCreatedBy, actorId, actorEmployeeId)
            },
            _ => new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = context.IsAwaitingApproval,
                EntityCreatedBy = context.EntityCreatedBy,
                BlockInitiatorOnLaterSteps = context.BlockInitiatorOnLaterSteps,
                CreatorSoDMessage = context.CreatorSoDMessage
            }
        };
}
