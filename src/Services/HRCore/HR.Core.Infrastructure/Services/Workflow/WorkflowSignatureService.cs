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

        var payload = new
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
        };

        var hash = CanonicalPayloadBuilder.ComputeHash(payload);
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
}
