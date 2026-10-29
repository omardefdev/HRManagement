using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Workflow;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using HR.Core.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Web.Services;

public sealed class WorkflowPanelBuildOptions
{
    public bool IsAwaitingApproval { get; init; }
    public Guid? EntityCreatedBy { get; init; }
    public string? MissingWorkflowWarning { get; init; }
    public bool BlockInitiatorOnLaterSteps { get; init; } = true;
    public string? CreatorSoDMessage { get; init; } = "لا يمكن للمنشئ اعتماد طلبه الخاص.";
    public Func<WorkflowInstance, WorkflowStepSnapshot, Guid?, string?>? AdditionalBlockReason { get; init; }

    /// <summary>PLAN-25: the server step-role rule for the inbox list (detail panels ask the evaluator instead).</summary>
    public WorkflowStepRoleOptions StepRoleOptions { get; init; } = new();
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

        var definition = await db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == instance.DefinitionId
                && d.Version == instance.DefinitionVersion
                && (d.TenantId == instance.TenantId || d.TenantId == TenantDefaults.DefaultTenantId), ct);

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

        var actor = WorkflowActionActor.FromPrincipal(user);
        var approveBlock = await evaluator.GetBlockReasonAsync(
            instance, definition, actor, WorkflowActionKind.Approve, instance.CurrentStepLevel, ct);
        var rejectBlock = await evaluator.GetBlockReasonAsync(instance, definition, actor, WorkflowActionKind.Reject, ct: ct);

        panel.CanApprove = approveBlock is null;
        panel.CanReject = rejectBlock is null;
        panel.BlockReason = approveBlock ?? rejectBlock;
        panel.ApproveTargetStepLevel = panel.CanApprove ? instance.CurrentStepLevel : null;
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

        var blockReason = ResolveBlockReason(instance, currentStep, user, userId, options);
        if (blockReason is not null)
        {
            panel.BlockReason = blockReason;
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

    private static string? ResolveBlockReason(
        WorkflowInstance instance,
        WorkflowStepSnapshot currentStep,
        ClaimsPrincipal user,
        Guid? userId,
        WorkflowPanelBuildOptions options)
    {
        if (userId.HasValue && options.EntityCreatedBy == userId)
            return options.CreatorSoDMessage;

        if (options.BlockInitiatorOnLaterSteps
            && userId.HasValue
            && instance.InitiatedBy == userId
            && instance.CurrentStepLevel > 1)
        {
            return "لا يمكن لمنشئ المسار اعتماد مراحل لاحقة.";
        }

        if (!UserHasStepRole(user, currentStep, instance.EntityType, options.StepRoleOptions))
            return WorkflowStageGuard.StepRoleMessage(currentStep.RoleRequired);

        return options.AdditionalBlockReason?.Invoke(instance, currentStep, userId);
    }

    private static bool UserHasStepRole(
        ClaimsPrincipal user,
        WorkflowStepSnapshot step,
        string entityType,
        WorkflowStepRoleOptions stepRoleOptions)
    {
        if (string.IsNullOrWhiteSpace(step.RoleRequired))
            return true;

        // Enforced entity types: exactly the server rule (no admin bypass unless configured).
        if (stepRoleOptions.IsEnforced(entityType))
            return WorkflowStageGuard.ValidateStepRole(
                step, entityType, WorkflowActionActor.FromPrincipal(user).Roles, stepRoleOptions) is null;

        if (user.IsInRole(HrManagementRoles.Admin))
            return true;

        return user.IsInRole(step.RoleRequired);
    }
}
