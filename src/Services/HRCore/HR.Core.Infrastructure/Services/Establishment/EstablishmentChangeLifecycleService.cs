using System.Text.Json;
using HR.Core.Domain.Entities;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Policy;
using HR.Core.Infrastructure.Services.Workflow;
using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.ServiceDefaults.Policy;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.SharedKernel.Enums;
using HRManagement.SharedKernel.Policy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HR.Core.Infrastructure.Services.Establishment;

public sealed class EstablishmentChangeLifecycleService(
    HRCoreDbContext db,
    ICodeGenerator codeGenerator,
    IPolicyOrchestrator policyOrchestrator,
    EstablishmentLegalSourceValidator legalSourceValidator,
    IEstablishmentChangeApplier applier,
    IWorkflowStarter workflowStarter,
    IConfiguration configuration)
{
    private readonly ApprovalStartCoordinator approvalStart = new(db, policyOrchestrator, workflowStarter, configuration);
    private const string EstablishmentChangeWorkflowCode = "ESTABLISHMENT-CHANGE-APPROVAL";
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<EstablishmentChangeRequest> CreateDraftAsync(
        EstablishmentChangeType changeType,
        object payload,
        Guid? targetEntityId = null,
        string? justificationNotes = null,
        Guid? createdBy = null,
        CancellationToken ct = default)
    {
        var code = await codeGenerator.GenerateAsync(CodeGenerationRequests.EstablishmentChangeRequest(), ct);
        var request = new EstablishmentChangeRequest
        {
            RequestNumber = code.Code,
            ChangeType = changeType,
            Status = EstablishmentChangeStatus.Draft,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            TargetEntityId = targetEntityId,
            JustificationNotes = justificationNotes,
            RequestedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };
        db.EstablishmentChangeRequests.Add(request);
        await db.SaveChangesAsync(ct);
        return request;
    }

    public async Task<(bool Success, string? Error)> UpdateDraftAsync(
        Guid id,
        object payload,
        string? justificationNotes,
        CancellationToken ct = default)
    {
        var request = await db.EstablishmentChangeRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return (false, "طلب التغيير غير موجود.");
        if (!EstablishmentChangeWorkflowRules.CanEdit(request.Status))
            return (false, "يمكن تعديل مسودات طلبات التغيير فقط.");

        request.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        request.JustificationNotes = justificationNotes;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SubmitForApprovalAsync(
        Guid id,
        string? legalSourceId,
        string? decisionReference,
        Guid? actorUserId,
        string? actorUserName,
        DateTime? effectiveFrom = null,
        CancellationToken ct = default)
    {
        var request = await db.EstablishmentChangeRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return (false, "طلب التغيير غير موجود.");
        if (!EstablishmentChangeWorkflowRules.CanSubmit(request.Status))
            return (false, "لا يمكن تقديم الطلب في حالته الحالية.");

        var legalError = await legalSourceValidator.ValidateRequiredAsync(legalSourceId, ct);
        if (legalError is not null) return (false, legalError);

        var contextJson = BuildApprovalContextJson(request);

        // PLAN-25 (G-02): the change is never applied without approval (it was applied at once outside lab scale);
        // PendingApproval only together with the instance.
        var start = await approvalStart.StartAndApplyAsync(
            new ApprovalStartRequest(
                "EstablishmentChangeRequest",
                request.Id,
                request.TenantId,
                EstablishmentChangeWorkflowCode,
                PolicyDomains.CoreEstablishment,
                actorUserId,
                actorUserName,
                contextJson,
                () => PolicyDomainFactsBuilders.ForEstablishmentChange(
                    request.Id,
                    request.TenantId,
                    "SubmitForApproval",
                    request.ChangeType.ToString(),
                    request.Status.ToString(),
                    hasOrgScope: false,
                    contextJson: contextJson),
                "مرفوض بسياسة الملاك."),
            async instanceId =>
            {
                request.LegalSourceId = string.IsNullOrWhiteSpace(legalSourceId) ? null : legalSourceId.Trim();
                request.DecisionReference = string.IsNullOrWhiteSpace(decisionReference) ? null : decisionReference.Trim();
                request.EffectiveFrom = effectiveFrom?.ToUniversalTime();
                request.Status = EstablishmentChangeStatus.PendingApproval;
                request.WorkflowInstanceId = instanceId;
                await db.SaveChangesAsync(ct);
            },
            ct);

        return start.Started ? (true, null) : (false, start.Error);
    }

    /// <summary>PLAN-25: starts the missing approval of a change request left <c>PendingApproval</c> without an instance.</summary>
    public async Task<(bool Success, string? Error)> RestartApprovalAsync(
        Guid id,
        Guid? actorUserId,
        string? actorUserName,
        CancellationToken ct = default)
    {
        var request = await db.EstablishmentChangeRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return (false, "طلب التغيير غير موجود.");
        if (request.Status != EstablishmentChangeStatus.PendingApproval || request.WorkflowInstanceId is not null)
            return (false, ApprovalRestartRules.NotStuckMessage);

        var start = await approvalStart.StartAndApplyAsync(
            new ApprovalStartRequest(
                "EstablishmentChangeRequest",
                request.Id,
                request.TenantId,
                EstablishmentChangeWorkflowCode,
                PolicyDomains.CoreEstablishment,
                // PLAN-25 (review finding 10): the record's creator stays the initiator (initiator rule); the audit names the actor.
                request.CreatedBy ?? actorUserId,
                request.CreatedBy is null ? actorUserName : null,
                BuildApprovalContextJson(request)),
            async instanceId =>
            {
                request.WorkflowInstanceId = instanceId;
                ApprovalRestartRules.AddRestartAudit(db, "EstablishmentChangeRequest", request.Id, instanceId, actorUserId);
                await db.SaveChangesAsync(ct);
            },
            ct);

        return start.Started ? (true, null) : (false, start.Error);
    }

    private static string BuildApprovalContextJson(EstablishmentChangeRequest request) => JsonSerializer.Serialize(new
    {
        changeRequestId = request.Id,
        changeType = request.ChangeType.ToString(),
        targetEntityId = request.TargetEntityId
    });

    public async Task<(bool Success, string? Error)> ApplyApprovedAsync(
        EstablishmentChangeRequest request,
        Guid? actorUserId,
        CancellationToken ct = default)
    {
        if (!EstablishmentChangeWorkflowRules.CanApply(request.Status))
            return (false, "لا يمكن تطبيق الطلب في حالته الحالية.");

        var applyResult = await applier.ApplyAsync(request, ct);
        if (!applyResult.Success)
            return (false, applyResult.Error);

        request.Status = EstablishmentChangeStatus.Applied;
        request.AppliedAt = DateTime.UtcNow;
        request.AppliedBy = actorUserId;
        request.ResultEntityId = applyResult.EntityId;

        db.EstablishmentChangeLogs.Add(new EstablishmentChangeLog
        {
            TenantId = request.TenantId,
            ChangeRequestId = request.Id,
            EntityType = applyResult.EntityType,
            EntityId = applyResult.EntityId ?? Guid.Empty,
            ChangeType = request.ChangeType,
            SnapshotBeforeJson = applyResult.BeforeJson,
            SnapshotAfterJson = applyResult.AfterJson,
            LegalSourceId = request.LegalSourceId,
            DecisionReference = request.DecisionReference,
            WorkflowInstanceId = request.WorkflowInstanceId,
            ActorUserId = actorUserId,
            OccurredAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> CancelAsync(Guid id, CancellationToken ct = default)
    {
        var request = await db.EstablishmentChangeRequests.FirstOrDefaultAsync(r => r.Id == id, ct);
        if (request is null) return (false, "طلب التغيير غير موجود.");
        if (!EstablishmentChangeWorkflowRules.CanCancel(request.Status))
            return (false, "لا يمكن إلغاء الطلب في حالته الحالية.");

        // PLAN-25 (review finding 9): every submitted request now has a workflow — cancel it with the request.
        if (request.WorkflowInstanceId is Guid instanceId)
        {
            var instance = await db.WorkflowInstances.FirstOrDefaultAsync(i => i.Id == instanceId, ct);
            if (instance is { Status: HRManagement.SharedKernel.Workflow.WorkflowStatus.InProgress })
            {
                instance.Status = HRManagement.SharedKernel.Workflow.WorkflowStatus.Cancelled;
                instance.CompletedAt = DateTime.UtcNow;
            }
        }

        request.Status = EstablishmentChangeStatus.Cancelled;
        request.WorkflowInstanceId = null;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }
}
