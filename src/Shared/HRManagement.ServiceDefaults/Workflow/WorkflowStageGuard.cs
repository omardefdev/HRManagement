using HRManagement.SharedKernel.Workflow;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>
/// PLAN-25 (G-04): <c>Workflow</c> section — which entity types have their step role enforced on the server, and whether
/// <c>hr-admin</c> may act on a step it does not hold the role of.
/// </summary>
public sealed class WorkflowStepRoleOptions
{
    public const string SectionName = "Workflow";

    /// <summary>Recruitment and establishment workflows (decision 4). Other domains are a follow-up.</summary>
    public static readonly IReadOnlyList<string> DefaultEnforcedEntityTypes =
    [
        "RecruitmentRequisition",
        "OfferLetter",
        "SelectionCommitteeSession",
        "RecruitmentAppeal",
        "RecruitmentCompetition",
        "CompetitionResultVersion",
        "EstablishmentChangeRequest"
    ];

    /// <summary>False in every environment unless set: an admin acts on a step only with that step's role.</summary>
    public bool AdminBypass { get; set; }

    /// <summary>Null means <see cref="DefaultEnforcedEntityTypes"/>; an empty list turns enforcement off.</summary>
    public string[]? EnforceStepRoleEntityTypes { get; set; }

    public bool IsEnforced(string? entityType) =>
        !string.IsNullOrWhiteSpace(entityType)
        && (EnforceStepRoleEntityTypes ?? DefaultEnforcedEntityTypes)
            .Contains(entityType, StringComparer.OrdinalIgnoreCase);
}

/// <summary>WF-001 / PLAN-03 / PLAN-25: API guards against illegal stage transitions and actors without the step role.</summary>
public static class WorkflowStageGuard
{
    public const string NotInProgressMessage = "مسار الاعتماد ليس قيد التنفيذ.";
    public const string SkipStageMessage = "لا يمكن تخطي مراحل الاعتماد: يجب أن تطابق الخطوة المستهدفة الخطوة الحالية.";
    public const string InitiatorLaterStageMessage = "لا يمكن لمنشئ المسار اعتماد مراحل لاحقة.";

    public static string StepRoleMessage(string roleRequired) => $"هذه الخطوة تتطلب دور «{roleRequired}».";

    public static string StepNotFoundMessage(int level) => $"الخطوة {level} غير موجودة في تعريف المسار.";

    /// <summary>Status, no stage skipping, existing step, then the initiator rule (WF-001 / PLAN-03).</summary>
    public static string? ValidateApprove(
        WorkflowInstanceState state,
        WorkflowDefinitionSnapshot definition,
        Guid? actorId,
        Guid? initiatedBy,
        int? targetStepLevel) =>
        ValidateStructure(state, definition, targetStepLevel)
        ?? ValidateInitiator(state, actorId, initiatedBy);

    /// <summary>
    /// PLAN-25 (G-04): <see cref="ValidateApprove(WorkflowInstanceState, WorkflowDefinitionSnapshot, Guid?, Guid?, int?)"/> plus
    /// the step role (checked before the initiator rule, so a user without the role sees the role message).
    /// </summary>
    public static string? ValidateApprove(
        WorkflowInstanceState state,
        WorkflowDefinitionSnapshot definition,
        Guid? actorId,
        Guid? initiatedBy,
        int? targetStepLevel,
        string? entityType,
        IReadOnlySet<string> actorRoles,
        WorkflowStepRoleOptions options) =>
        ValidateStructure(state, definition, targetStepLevel)
        ?? ValidateStepRole(CurrentStep(state, definition), entityType, actorRoles, options)
        ?? ValidateInitiator(state, actorId, initiatedBy);

    /// <summary>PLAN-25 (G-04): a reject needs an in-progress instance, an existing step and the step role.</summary>
    public static string? ValidateReject(
        WorkflowInstanceState state,
        WorkflowDefinitionSnapshot definition,
        string? entityType,
        IReadOnlySet<string> actorRoles,
        WorkflowStepRoleOptions options) =>
        ValidateStructure(state, definition, targetStepLevel: null)
        ?? ValidateStepRole(CurrentStep(state, definition), entityType, actorRoles, options);

    /// <summary>In progress, the target step (if any) is the current one, and the current step exists.</summary>
    public static string? ValidateStructure(
        WorkflowInstanceState state,
        WorkflowDefinitionSnapshot definition,
        int? targetStepLevel)
    {
        if (state.Status != WorkflowStatus.InProgress)
            return NotInProgressMessage;

        if (targetStepLevel.HasValue && targetStepLevel.Value != state.CurrentStepLevel)
            return SkipStageMessage;

        if (definition.Steps.All(s => s.Level != state.CurrentStepLevel))
            return StepNotFoundMessage(state.CurrentStepLevel);

        return null;
    }

    /// <summary>The initiator of a workflow never approves its later stages.</summary>
    public static string? ValidateInitiator(WorkflowInstanceState state, Guid? actorId, Guid? initiatedBy) =>
        actorId.HasValue && initiatedBy.HasValue && actorId.Value == initiatedBy.Value && state.CurrentStepLevel > 1
            ? InitiatorLaterStageMessage
            : null;

    /// <summary>
    /// The step's <see cref="WorkflowStepSnapshot.RoleRequired"/> must be one of the actor's roles (case-insensitive) when the
    /// entity type is enforced; <c>hr-admin</c> passes only with <see cref="WorkflowStepRoleOptions.AdminBypass"/>.
    /// </summary>
    public static string? ValidateStepRole(
        WorkflowStepSnapshot step,
        string? entityType,
        IReadOnlySet<string> actorRoles,
        WorkflowStepRoleOptions options)
    {
        if (!options.IsEnforced(entityType) || string.IsNullOrWhiteSpace(step.RoleRequired))
            return null;

        if (actorRoles.Any(r => string.Equals(r, step.RoleRequired, StringComparison.OrdinalIgnoreCase)))
            return null;

        if (options.AdminBypass
            && actorRoles.Any(r => string.Equals(r, HrManagementRoles.Admin, StringComparison.OrdinalIgnoreCase)))
            return null;

        return StepRoleMessage(step.RoleRequired);
    }

    private static WorkflowStepSnapshot CurrentStep(WorkflowInstanceState state, WorkflowDefinitionSnapshot definition) =>
        definition.Steps.First(s => s.Level == state.CurrentStepLevel);
}
