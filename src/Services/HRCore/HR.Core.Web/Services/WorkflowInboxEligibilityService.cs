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

    /// <summary>PLAN-44: the makers of an entity, as its gate sees them.</summary>
    public IReadOnlySet<Guid> MakersOf(Guid? entityCreatedBy, WorkflowInstance instance) =>
        WorkflowMakerRules.Makers(
            entityCreatedBy, instance.InitiatedBy, instance.RequestedByUserId, _stepRoleOptions.Maker.IncludeSubmitter);

    public bool AdminBypass => _stepRoleOptions.AdminBypass;

    public bool IsActionable(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        ClaimsPrincipal user,
        Guid? userId,
        WorkflowInboxEntityContext context,
        OrgAccessScope orgScope) =>
        Evaluate(instance, definition, user, userId, context, orgScope).Actionable;

    /// <summary>
    /// PLAN-50 (§6): whether the item is the user's to act on, and through which delegation when he lacks the step's role. An item acted on
    /// behalf must also lie in the principal's stored scope (<paramref name="principalScopes"/>, by the stored root), as the evaluator
    /// requires on the server.
    /// </summary>
    public (bool Actionable, HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation? OnBehalfOf) Evaluate(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        ClaimsPrincipal user,
        Guid? userId,
        WorkflowInboxEntityContext context,
        OrgAccessScope orgScope,
        IReadOnlyList<HRManagement.SharedKernel.SeparationOfDuties.ActiveDelegation>? delegations = null,
        IReadOnlyDictionary<Guid, OrgAccessScope>? principalScopes = null)
    {
        if (!WorkflowInboxOrgScope.Allows(orgScope, context.ScopeOrgUnitId))
            return (false, null);

        if (userId is Guid actorId && context.EntityMakers.Contains(actorId))
            return (false, null);

        var options = BuildOptions(
            instance, context, HrOrgScopeVisibility.GetEmployeeId(user), RecruitmentAppealAccess.HasReviewerRole(user));
        var panel = WorkflowPanelBuilder.Build(
            instance, definition, user, userId, new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = options.IsAwaitingApproval,
                EntityMakers = options.EntityMakers,
                BlockInitiatorOnLaterSteps = options.BlockInitiatorOnLaterSteps,
                CreatorSoDMessage = options.CreatorSoDMessage,
                // PLAN-25 (review finding 6): the override distinct-approver rule, as OfferApprovalGate applies it.
                AdditionalBlockReason = (inst, step, actorId) =>
                    options.AdditionalBlockReason?.Invoke(inst, step, actorId)
                    ?? (OfferOverrideApprovalRules.IsOverrideWorkflow(definition.Code)
                        && OfferOverrideApprovalRules.HasApprovedEarlierStep(inst.StepHistoryJson, inst.CurrentStepLevel, actorId)
                            ? OfferOverrideApprovalRules.SameApproverMessage
                            : null),
                StepRoleOptions = _stepRoleOptions,
                Delegations = delegations
            });
        if (!panel.CanApprove)
            return (false, null);

        if (panel.OnBehalfOf is { PrincipalScopeOrgUnitId: Guid root } onBehalf)
        {
            if (principalScopes is null || !principalScopes.TryGetValue(root, out var principalScope)
                || !WorkflowInboxOrgScope.Allows(principalScope, context.ScopeOrgUnitId))
                return (false, null);
            return (true, onBehalf);
        }

        return (true, panel.OnBehalfOf);
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
                EntityMakers = context.EntityMakers,
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
                EntityMakers = context.EntityMakers,
                BlockInitiatorOnLaterSteps = context.BlockInitiatorOnLaterSteps,
                CreatorSoDMessage = SelectionCommitteeRules.ApproverIsMemberOrCreatorMessage,
                // HCM-028: same rule as SelectionCommitteeLifecycleService.ApproveStepAsync.
                AdditionalBlockReason = (inst, _, actorId) => context.CommitteeMembers is null
                    ? null
                    : SelectionCommitteeRules.ValidateApproval(
                        inst.CurrentStepLevel, context.CommitteeMembers, context.CommitteeCreatedBy, actorId, actorEmployeeId)
            },
            _ => new WorkflowPanelBuildOptions
            {
                IsAwaitingApproval = context.IsAwaitingApproval,
                EntityMakers = context.EntityMakers,
                BlockInitiatorOnLaterSteps = context.BlockInitiatorOnLaterSteps,
                CreatorSoDMessage = context.CreatorSoDMessage
            }
        };
}
