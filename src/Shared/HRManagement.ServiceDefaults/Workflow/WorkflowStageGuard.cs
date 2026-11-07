using HRManagement.SharedKernel.Workflow;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Workflow;

/// <summary>
/// PLAN-25 (G-04), PLAN-43 (G-WF-06): <c>Workflow</c> section. The step role is enforced for every entity type except the recorded
/// exemptions, and <c>hr-admin</c> acts on a step it does not hold the role of only with <see cref="AdminBypass"/>.
/// </summary>
public sealed class WorkflowStepRoleOptions
{
    public const string SectionName = "Workflow";

    /// <summary>The key PLAN-43 replaced. It is ignored; a start-up warning names it.</summary>
    public const string IgnoredKey = "Workflow:EnforceStepRoleEntityTypes";

    /// <summary>
    /// Types whose step role cannot reach the approval route yet, each with an owner plan and a date in
    /// <c>docs/qa/workflow-step-role-exemptions.json</c>: <c>AssetLossReport</c> until PLAN-45, the others until PLAN-46.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultExemptEntityTypes =
    [
        "AssetLossReport",
        "SeparationRecord",
        "DisciplinaryAction",
        "CardRequest",
        "Ticket"
    ];

    /// <summary>False in every environment unless set: an admin acts on a step only with that step's role.</summary>
    public bool AdminBypass { get; set; }

    public WorkflowStepRoleSection StepRole { get; set; } = new();

    public WorkflowMakerSection Maker { get; set; } = new();

    public bool IsEnforced(string? entityType) =>
        !string.IsNullOrWhiteSpace(entityType)
        && !(StepRole.ExemptEntityTypes ?? DefaultExemptEntityTypes)
            .Contains(entityType, StringComparer.OrdinalIgnoreCase);

    /// <returns>True when the old key is still set.</returns>
    public static bool WarnAboutIgnoredKey(IConfiguration configuration, ILogger logger)
    {
        if (!configuration.GetSection(IgnoredKey).Exists())
            return false;

        logger.LogWarning(
            "{Key} is ignored since PLAN-43: the step role is enforced for every type except Workflow:StepRole:ExemptEntityTypes",
            IgnoredKey);
        return true;
    }
}

/// <summary><c>Workflow:StepRole</c>.</summary>
public sealed class WorkflowStepRoleSection
{
    /// <summary>Null means <see cref="WorkflowStepRoleOptions.DefaultExemptEntityTypes"/>; an empty list exempts nothing.</summary>
    public string[]? ExemptEntityTypes { get; set; }
}

/// <summary><c>Workflow:Maker</c> (PLAN-44, D-WF-8).</summary>
public sealed class WorkflowMakerSection
{
    /// <summary>The submitter counts as a maker. See <see cref="WorkflowMakerRules"/>.</summary>
    public bool IncludeSubmitter { get; set; } = true;
}

/// <summary>WF-001 / PLAN-03 / PLAN-25: API guards against illegal stage transitions and actors without the step role.</summary>
public static class WorkflowStageGuard
{
    public const string NotInProgressMessage = "مسار الاعتماد ليس قيد التنفيذ.";
    public const string SkipStageMessage = "لا يمكن تخطي مراحل الاعتماد: يجب أن تطابق الخطوة المستهدفة الخطوة الحالية.";
    public const string InitiatorLaterStageMessage = "لا يمكن لمنشئ المسار اعتماد مراحل لاحقة.";
    public const string ServiceAccountMessage = "لا يعتمد حساب خدمة أي خطوة ولا يرفضها.";

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
