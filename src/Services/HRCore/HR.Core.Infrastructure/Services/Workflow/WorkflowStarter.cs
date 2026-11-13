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

        // PLAN-52 (P-5): the entity's unit, so api/workflow-instances can filter by it; a type with no key stays null (central).
        var scopes = new WorkflowInstanceScopeResolver(db);
        instance.ScopeOrgUnitId = await scopes.ResolveAsync(entityType, entityId, ct);

        // PLAN-44: every caller reaches here, so the loser of a race is told the same way on every path.
        if (await WorkflowInstanceInsert.TrySaveAsync(db, instance, ct) is not null)
            throw new WorkflowInstanceRunningException(entityType, entityId);

        // An entity added in the same unit of work is saved with the instance: read its unit now.
        if (instance.ScopeOrgUnitId is null
            && WorkflowInstanceScopeResolver.KeyedTypes.Contains(entityType)
            && await scopes.ResolveAsync(entityType, entityId, ct) is Guid unit)
        {
            instance.ScopeOrgUnitId = unit;
            await db.SaveChangesAsync(ct);
        }

        return instance.Id;
    }

    private async Task<WorkflowDefinition?> ResolveDefinitionAsync(
        string workflowCode,
        Guid requestedTenantId,
        string? policyDomain,
        CancellationToken ct)
    {
        // PLAN-43 (G-WF-04): both ways honour the validity window, so a pinned binding cannot start an expired version.
        var now = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(policyDomain))
        {
            // PLAN-48: the national read path, the requested tenant's own rows first.
            var binding = await db.PolicyWorkflowBindings
                .OwnOrNational(requestedTenantId)
                .AsNoTracking()
                .Where(b => b.Domain == policyDomain && b.WorkflowCode == workflowCode)
                .OwnFirst(requestedTenantId)
                .FirstOrDefaultAsync(ct);

            if (binding?.WorkflowDefinitionId is Guid defId)
            {
                var bound = await db.WorkflowDefinitions
                    .OwnOrNational(requestedTenantId)
                    .AsNoTracking()
                    .FirstOrDefaultAsync(d => d.Id == defId, ct);
                // PLAN-25 (G-14): a binding never revives an unpublished (retired) definition.
                if (bound is { IsPublished: true } && IsValidAt(bound, now))
                    return bound;
            }
        }

        return await db.WorkflowDefinitions
            .OwnOrNational(requestedTenantId)
            .AsNoTracking()
            .Where(d => d.Code == workflowCode
                && d.IsPublished
                && (d.ValidFrom == null || d.ValidFrom <= now)
                && (d.ValidTo == null || d.ValidTo >= now))
            .OwnFirst(requestedTenantId)
            .ThenByDescending(d => d.Version)
            .FirstOrDefaultAsync(ct);
    }

    private static bool IsValidAt(WorkflowDefinition definition, DateTime now) =>
        (definition.ValidFrom is null || definition.ValidFrom <= now)
        && (definition.ValidTo is null || definition.ValidTo >= now);
}
