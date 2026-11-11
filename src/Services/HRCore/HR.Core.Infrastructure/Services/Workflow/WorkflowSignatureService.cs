using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.ServiceDefaults.Signatures;
using HRManagement.SharedKernel.Signatures;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

public interface IWorkflowSignatureService
{
    Task<SignatureRecord> RecordStepActionAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowStepHistoryEntry historyEntry,
        string intent,
        Guid? actorId,
        string? actorRole,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowSignatureService(
    HRCoreDbContext db,
    ISignatureService signatureService,
    ITenantContext tenantContext) : IWorkflowSignatureService
{
    public async Task<SignatureRecord> RecordStepActionAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        WorkflowStepHistoryEntry historyEntry,
        string intent,
        Guid? actorId,
        string? actorRole,
        CancellationToken cancellationToken = default)
    {
        var tenantId = tenantContext.TenantId ?? instance.TenantId;
        if (tenantId == Guid.Empty)
            tenantId = TenantDefaults.DefaultTenantId;

        var hash = CanonicalPayloadBuilder.ComputeHash(Payload(instance, definition, historyEntry, intent, actorId));
        var subjectType = string.IsNullOrWhiteSpace(instance.EntityType)
            ? "WorkflowInstance"
            : instance.EntityType;

        var record = await signatureService.SignAsync(new SignatureRequest(
            tenantId,
            subjectType,
            instance.EntityId.ToString("D"),
            actorId?.ToString("D") ?? "unknown",
            actorRole ?? string.Empty,
            intent,
            hash), cancellationToken);

        db.SignatureRecords.Add(record);
        await db.SaveChangesAsync(cancellationToken);
        return record;
    }

    /// <summary>
    /// The signed payload. A direct action signs the twelve fields it always signed, so an existing signature still verifies; PLAN-50 (§6):
    /// an action on behalf (<see cref="WorkflowStepHistoryEntry.OnBehalfOfId"/>) adds <c>onBehalfOfUserId</c> and <c>delegationId</c>, and
    /// its caller signs with the delegated role.
    /// </summary>
    public static object Payload(
        WorkflowInstance instance, WorkflowDefinition definition, WorkflowStepHistoryEntry historyEntry, string intent, Guid? actorId) =>
        historyEntry.OnBehalfOfId is null
            ? new
            {
                workflowInstanceId = instance.Id,
                definitionId = definition.Id,
                definitionVersion = definition.Version,
                definitionCode = definition.Code,
                entityType = instance.EntityType,
                entityId = instance.EntityId,
                stepLevel = historyEntry.Level,
                stepName = historyEntry.StepName,
                intent,
                actorId,
                comments = historyEntry.Comments,
                actionAt = historyEntry.ActionAt
            }
            : new
            {
                workflowInstanceId = instance.Id,
                definitionId = definition.Id,
                definitionVersion = definition.Version,
                definitionCode = definition.Code,
                entityType = instance.EntityType,
                entityId = instance.EntityId,
                stepLevel = historyEntry.Level,
                stepName = historyEntry.StepName,
                intent,
                actorId,
                comments = historyEntry.Comments,
                actionAt = historyEntry.ActionAt,
                onBehalfOfUserId = historyEntry.OnBehalfOfId,
                delegationId = historyEntry.DelegationId
            };
}
