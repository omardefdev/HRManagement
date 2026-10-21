using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HR.Core.Infrastructure.Services.Workflow;

public interface IWorkflowStarter
{
    Task<Guid?> StartByWorkflowCodeAsync(
        string workflowCode,
        string entityType,
        Guid entityId,
        Guid? tenantId,
        Guid? actorUserId,
        string? actorUserName,
        string? contextJson = null,
        string? policyDomain = null,
        CancellationToken ct = default);
}

public sealed class WorkflowStarter(
    HRCoreDbContext db,
    IWorkflowEngine workflowEngine,
    ILogger<WorkflowStarter> logger) : IWorkflowStarter
{
    public async Task<Guid?> StartByWorkflowCodeAsync(
        string workflowCode,
        string entityType,
        Guid entityId,
        Guid? tenantId,
        Guid? actorUserId,
        string? actorUserName,
        string? contextJson = null,
        string? policyDomain = null,
        CancellationToken ct = default)
    {
        var requestedTenantId = tenantId ?? db.CurrentTenantId ?? TenantDefaults.DefaultTenantId;
        var definition = await ResolveDefinitionAsync(workflowCode, requestedTenantId, policyDomain, ct);
        if (definition is null)
        {
            logger.LogWarning("Workflow start: no definition for {Code}", workflowCode);
            return null;
        }

        var snapshot = WorkflowDefinitionMapper.ToSnapshot(definition);
        var result = workflowEngine.Start(
            snapshot,
            entityType,
            entityId,
            actorUserId,
            actorUserName,
            contextJson);

        if (!result.Success)
        {
            logger.LogWarning("Workflow start failed for {Code}: {Error}", workflowCode, result.Error);
            return null;
        }

        var instance = new WorkflowInstance
        {
            DefinitionId = definition.Id,
            DefinitionVersion = definition.Version,
            EntityType = entityType,
            EntityId = entityId,
            CurrentStepLevel = result.NewStepLevel,
            Status = result.NewStatus,
            ContextJson = contextJson,
            InitiatedBy = actorUserId,
            InitiatedByName = actorUserName,
            StartedAt = DateTime.UtcNow
        };

        if (result.NewStatus == WorkflowStatus.Completed)
            instance.CompletedAt = DateTime.UtcNow;

        db.WorkflowInstances.Add(instance);
        await db.SaveChangesAsync(ct);
        return instance.Id;
    }

    private async Task<WorkflowDefinition?> ResolveDefinitionAsync(
        string workflowCode,
        Guid requestedTenantId,
        string? policyDomain,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(policyDomain))
        {
            var binding = await db.PolicyWorkflowBindings
                .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
                .AsNoTracking()
                .Where(b => b.Domain == policyDomain
                    && b.WorkflowCode == workflowCode
                    && (b.TenantId == requestedTenantId || b.TenantId == TenantDefaults.DefaultTenantId))
                .OrderByDescending(b => b.TenantId == requestedTenantId)
                .FirstOrDefaultAsync(ct);

            if (binding?.WorkflowDefinitionId is Guid defId)
            {
                var bound = await db.WorkflowDefinitions
                    .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == defId, ct);
                if (bound is not null)
                    return bound;
            }
        }

        return await db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .AsNoTracking()
            .Where(d => d.Code == workflowCode
                && d.IsPublished
                && (d.TenantId == requestedTenantId || d.TenantId == TenantDefaults.DefaultTenantId))
            .OrderByDescending(d => d.TenantId == requestedTenantId)
            .ThenByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);
    }
}
