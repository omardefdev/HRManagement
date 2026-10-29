using System.Security.Claims;
using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
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
    public static WorkflowPanelViewModel Build(
        WorkflowInstance? instance,
        WorkflowDefinition? definition,
        ClaimsPrincipal user,
        Guid? userId,
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
            return panel;

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

        if (!options.IsAwaitingApproval
            || instance.Status != WorkflowStatus.InProgress
            || currentStep is null)
        {
            return panel;
        }

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

        if (!UserHasStepRole(user, currentStep.RoleRequired))
            return $"هذه الخطوة تتطلب دور «{currentStep.RoleRequired}».";

        return options.AdditionalBlockReason?.Invoke(instance, currentStep, userId);
    }

    private static bool UserHasStepRole(ClaimsPrincipal user, string roleRequired)
    {
        if (string.IsNullOrWhiteSpace(roleRequired))
            return true;

        if (user.IsInRole(HrManagementRoles.Admin))
            return true;

        return user.IsInRole(roleRequired);
    }
}
