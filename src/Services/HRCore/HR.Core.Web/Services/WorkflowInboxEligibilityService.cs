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

        var options = BuildOptions(instance, context);
        var panel = WorkflowPanelBuilder.Build(instance, definition, user, userId, options);
        return panel.CanApprove;
    }

    internal static WorkflowPanelBuildOptions BuildOptions(
        WorkflowInstance instance,
        WorkflowInboxEntityContext context) =>
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
                AdditionalBlockReason = (inst, step, _) =>
                {
                    if (inst.CurrentStepLevel == 2
                        && step.Level == 2
                        && context.CommitteeMembers is not null
                        && !RecruitmentAppealWorkflowRules.HasQuorum(context.CommitteeMembers))
                    {
                        return "لا يمكن اعتماد خطوة النصاب: يجب 3 أعضاء فعّالين على الأقل (غير منسحبين).";
                    }

                    return null;
                }
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
