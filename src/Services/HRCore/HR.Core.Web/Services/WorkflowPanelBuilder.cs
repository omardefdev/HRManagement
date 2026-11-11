using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.SeparationOfDuties;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using HR.Core.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public sealed class WorkflowPanelBuildOptions
{
    public bool IsAwaitingApproval { get; init; }
    public IReadOnlySet<Guid> EntityMakers { get; init; } = new HashSet<Guid>();
    public string? MissingWorkflowWarning { get; init; }
    public bool BlockInitiatorOnLaterSteps { get; init; } = true;
    public string? CreatorSoDMessage { get; init; } = "لا يمكن للمنشئ اعتماد طلبه الخاص.";
    public Func<WorkflowInstance, WorkflowStepSnapshot, Guid?, string?>? AdditionalBlockReason { get; init; }

    /// <summary>PLAN-25: the server step-role rule for the inbox list (detail panels ask the evaluator instead).</summary>
    public WorkflowStepRoleOptions StepRoleOptions { get; init; } = new();

    /// <summary>PLAN-50 (§6): the user's delegations in force, loaded once for the inbox's bulk check (the panels ask the evaluator).</summary>
    public IReadOnlyList<ActiveDelegation>? Delegations { get; init; }
}

public static class WorkflowPanelDataLoader
{
    public static async Task<(WorkflowInstance? Instance, WorkflowDefinition? Definition)> LoadAsync(
        HRCoreDbContext db,
        Guid? workflowInstanceId,
        CancellationToken ct = default)
    {
        if (workflowInstanceId is not Guid workflowId)
            return (null, null);

        var instance = await db.WorkflowInstances.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == workflowId, ct);
        if (instance is null)
            return (null, null);

        // PLAN-48: the national read path (the instance's tenant, or national).
        var definition = await db.WorkflowDefinitions
            .OwnOrNational(instance.TenantId)
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == instance.DefinitionId
                && d.Version == instance.DefinitionVersion, ct);

        return (instance, definition);
    }
}

public static class WorkflowPanelBuilder
{
    /// <summary>
    /// PLAN-25 (G-04, decision 5): the detail panels. Approve and reject are offered exactly when the server would accept
    /// them — the same <see cref="WorkflowApprovalEvaluator"/> (stage guard, step role, entity gate) that
    /// <c>WorkflowInstanceActionService</c> runs. Only the display state comes from <paramref name="options"/>.
    /// </summary>
    public static async Task<WorkflowPanelViewModel> BuildAsync(
        WorkflowInstance? instance,
        WorkflowDefinition? definition,
        ClaimsPrincipal user,
        WorkflowPanelBuildOptions options,
        WorkflowApprovalEvaluator evaluator,
        CancellationToken ct = default)
    {
        var (panel, currentStep) = BuildDisplay(instance, definition, user, options);
        if (instance is null || definition is null || currentStep is null
            || !options.IsAwaitingApproval || instance.Status != WorkflowStatus.InProgress)
            return panel;

        // PLAN-50 (§6): the evaluator asks its delegation lookup when the user lacks the step's role.
        var actor = WorkflowActionActor.FromPrincipal(user);
        var approve = await evaluator.GetDecisionAsync(
            instance, definition, actor, WorkflowActionKind.Approve, instance.CurrentStepLevel, ct);
        var reject = await evaluator.GetDecisionAsync(instance, definition, actor, WorkflowActionKind.Reject, ct: ct);

        panel.CanApprove = approve.Allowed;
        panel.CanReject = reject.Allowed;
        panel.BlockReason = approve.BlockReason ?? reject.BlockReason;
        panel.ApproveTargetStepLevel = panel.CanApprove ? instance.CurrentStepLevel : null;
        panel.OnBehalfOf = approve.OnBehalfOf ?? reject.OnBehalfOf;
        return panel;
    }

    /// <summary>
    /// The work inbox's bulk check (no per-row queries): the same domain rules from pre-loaded context and the server's
    /// step-role rule (<see cref="WorkflowPanelBuildOptions.StepRoleOptions"/>).
    /// </summary>
    public static WorkflowPanelViewModel Build(
        WorkflowInstance? instance,
        WorkflowDefinition? definition,
        ClaimsPrincipal user,
        Guid? userId,
        WorkflowPanelBuildOptions options)
    {
        var (panel, currentStep) = BuildDisplay(instance, definition, user, options);
        if (instance is null || definition is null || currentStep is null
            || !options.IsAwaitingApproval || instance.Status != WorkflowStatus.InProgress)
            return panel;

        var sodFacts = WorkflowDefinitionMapper.ToSodFacts(instance, definition);
        var (blockReason, onBehalfOf) = ResolveBlockReason(instance, currentStep, sodFacts, user, userId, options);
        if (blockReason is not null)
        {
            panel.BlockReason = blockReason;
            return panel;
        }

        panel.OnBehalfOf = onBehalfOf;

        // PLAN-43: the distinct-approver rule binds approving only, as on the server. PLAN-50: for the delegate and the principal.
        if ((WorkflowSodRules.DistinctApprover(sodFacts, userId)
             ?? (onBehalfOf is null ? null : WorkflowSodRules.DistinctApprover(sodFacts, onBehalfOf.PrincipalUserId))) is { } distinctApprover)
        {
            panel.BlockReason = distinctApprover;
            panel.CanReject = true;
            return panel;
        }

        panel.CanApprove = true;
        panel.CanReject = true;
        panel.ApproveTargetStepLevel = instance.CurrentStepLevel;
        return panel;
    }

    public static Guid? ParseUserId(ClaimsPrincipal user)
    {
        var v = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
        return Guid.TryParse(v, out var g) ? g : null;
    }

    private static (WorkflowPanelViewModel Panel, WorkflowStepSnapshot? CurrentStep) BuildDisplay(
        WorkflowInstance? instance,
        WorkflowDefinition? definition,
        ClaimsPrincipal user,
        WorkflowPanelBuildOptions options)
    {
        var panel = new WorkflowPanelViewModel
        {
            HasWorkflowInstance = instance is not null,
            MissingWorkflowWarning = options.IsAwaitingApproval
                && instance is null
                && options.MissingWorkflowWarning is not null
                    ? options.MissingWorkflowWarning
                    : null,
            // PLAN-25: HrCoreWrite (Admin or hr-core) may restart the missing workflow.
            CanRestartApproval = options.IsAwaitingApproval
                && instance is null
                && options.MissingWorkflowWarning is not null
                && (user.IsInRole(HrManagementRoles.Admin) || user.IsInRole(HrManagementRoles.HrCore))
        };

        if (instance is null || definition is null)
            return (panel, null);

        panel.WorkflowInstanceId = instance.Id;
        panel.WorkflowStatus = instance.Status;
        panel.CurrentStepLevel = instance.CurrentStepLevel;
        panel.WorkflowCode = definition.Code;
        panel.WorkflowName = definition.Name;

        var steps = JsonSerializer.Deserialize<List<WorkflowStepSnapshot>>(definition.StepsJson) ?? [];
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
        var approvedLevels = history
            .Where(h => h.Status == WorkflowStepStatus.Approved)
            .Select(h => h.Level)
            .ToHashSet();

        panel.Steps = steps
            .OrderBy(s => s.Level)
            .Select(s => new WorkflowStepViewModel
            {
                Level = s.Level,
                Name = s.Name,
                RoleRequired = s.RoleRequired,
                State = ResolveStepState(instance, s.Level, approvedLevels)
            })
            .ToList();

        var currentStep = steps.FirstOrDefault(s => s.Level == instance.CurrentStepLevel);
        panel.CurrentStepName = currentStep?.Name;
        panel.CurrentStepRoleRequired = currentStep?.RoleRequired;
        return (panel, currentStep);
    }

    private static string ResolveStepState(
        WorkflowInstance instance,
        int level,
        HashSet<int> approvedLevels)
    {
        if (approvedLevels.Contains(level) || level < instance.CurrentStepLevel)
            return "completed";

        if (instance.Status == WorkflowStatus.InProgress && level == instance.CurrentStepLevel)
            return "current";

        return "upcoming";
    }

    private static (string? Reason, ActiveDelegation? OnBehalfOf) ResolveBlockReason(
        WorkflowInstance instance,
        WorkflowStepSnapshot currentStep,
        WorkflowSodFacts sodFacts,
        ClaimsPrincipal user,
        Guid? userId,
        WorkflowPanelBuildOptions options)
    {
        if (MakerOrInitiator(instance, userId, options) is { } own)
            return (own, null);

        // PLAN-43: exactly the server's rules, exempt types included: role, then the rules of WorkflowSodRules, then the entity's.
        var actor = WorkflowActionActor.FromPrincipal(user);
        ActiveDelegation? onBehalfOf = null;
        var roleError = WorkflowStageGuard.ValidateStepRole(currentStep, instance.EntityType, actor.Roles, options.StepRoleOptions);
        if (roleError is not null)
        {
            // PLAN-50 (§6): a delegation of the step's role that covers the type stands in for it, as in the evaluator.
            onBehalfOf = options.Delegations is { Count: > 0 } delegations
                         && !string.IsNullOrWhiteSpace(currentStep.RoleRequired)
                         && options.StepRoleOptions.IsEnforced(instance.EntityType)
                ? ApprovalDelegationMatcher.Match(delegations, currentStep.RoleRequired, instance.EntityType, DateTime.UtcNow)
                : null;
            if (onBehalfOf is null)
                return (roleError, null);
        }

        // PLAN-50 (P-6): every identity rule after the role, as the delegate and then as the principal.
        var reason = WorkflowSodRules.Excluded(sodFacts, userId)
            ?? WorkflowSodRules.Subject(sodFacts, actor.EmployeeId)
            ?? options.AdditionalBlockReason?.Invoke(instance, currentStep, userId);
        if (reason is null && onBehalfOf is not null)
        {
            reason = MakerOrInitiator(instance, onBehalfOf.PrincipalUserId, options)
                ?? WorkflowSodRules.Excluded(sodFacts, onBehalfOf.PrincipalUserId)
                ?? WorkflowSodRules.Subject(sodFacts, onBehalfOf.PrincipalEmployeeId)
                ?? options.AdditionalBlockReason?.Invoke(instance, currentStep, onBehalfOf.PrincipalUserId);
        }

        return reason is null ? (null, onBehalfOf) : (reason, null);
    }

    private static string? MakerOrInitiator(WorkflowInstance instance, Guid? userId, WorkflowPanelBuildOptions options)
    {
        if (userId is Guid actorId && options.EntityMakers.Contains(actorId))
            return options.CreatorSoDMessage;

        if (options.BlockInitiatorOnLaterSteps
            && userId.HasValue
            && instance.InitiatedBy == userId
            && instance.CurrentStepLevel > 1)
        {
            return "لا يمكن لمنشئ المسار اعتماد مراحل لاحقة.";
        }

        return null;
    }
}
