using System.Security.Claims;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;

namespace HR.Core.Web.Services;

/// <summary>PLAN-25: the step-role rule follows the server (Workflow options registered in DI).</summary>
public sealed class WorkflowInboxEligibilityService(WorkflowStepRoleOptions? stepRoleOptions = null)
{
    private readonly WorkflowStepRoleOptions _stepRoleOptions = stepRoleOptions ?? new WorkflowStepRoleOptions();

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

        var options = BuildOptions(
            instance, context, HrOrgScopeVisibility.GetEmployeeId(user), RecruitmentAppealAccess.HasReviewerRole(user));
        var panel = WorkflowPanelBuilder.Build(
            instance, definition, user, userId, new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = options.IsAwaitingApproval,
                EntityCreatedBy = options.EntityCreatedBy,
                BlockInitiatorOnLaterSteps = options.BlockInitiatorOnLaterSteps,
                CreatorSoDMessage = options.CreatorSoDMessage,
                // PLAN-25 (review finding 6): the override distinct-approver rule, as OfferApprovalGate applies it.
                AdditionalBlockReason = (inst, step, actorId) =>
                    options.AdditionalBlockReason?.Invoke(inst, step, actorId)
                    ?? (OfferOverrideApprovalRules.IsOverrideWorkflow(definition.Code)
                        && OfferOverrideApprovalRules.HasApprovedEarlierStep(inst.StepHistoryJson, inst.CurrentStepLevel, actorId)
                            ? OfferOverrideApprovalRules.SameApproverMessage
                            : null),
                StepRoleOptions = _stepRoleOptions
            });
        return panel.CanApprove;
    }

    internal static WorkflowPanelBuildOptions BuildOptions(
        WorkflowInstance instance,
        WorkflowInboxEntityContext context,
        Guid? actorEmployeeId = null,
        bool actorHasAppealReviewerRole = false) =>
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
                // HCM-028 (H2): only the assigned reviewer (employee, or legacy user id) with the compliance role.
                AdditionalBlockReason = (_, _, actorId) =>
                    context.Appeal is not null
                    && RecruitmentAppealWorkflowRules.IsAssignedReviewer(
                        context.Appeal, actorHasAppealReviewerRole, actorId, actorEmployeeId)
                        ? null
                        : RecruitmentAppealWorkflowRules.NotAssignedReviewerMessage
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
