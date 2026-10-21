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

        request.LegalSourceId = string.IsNullOrWhiteSpace(legalSourceId) ? null : legalSourceId.Trim();
        request.DecisionReference = string.IsNullOrWhiteSpace(decisionReference) ? null : decisionReference.Trim();
        request.EffectiveFrom = effectiveFrom?.ToUniversalTime();

        var contextJson = JsonSerializer.Serialize(new
        {
            changeRequestId = request.Id,
            changeType = request.ChangeType.ToString(),
            targetEntityId = request.TargetEntityId
        });

        var mode = ResolveMode();
        if (mode == PolicyEnforcementMode.Off)
        {
            request.Status = EstablishmentChangeStatus.PendingApproval;

            if (IsLabScale())
            {
                request.WorkflowInstanceId = await workflowStarter.StartByWorkflowCodeAsync(
                    EstablishmentChangeWorkflowCode,
                    "EstablishmentChangeRequest",
                    request.Id,
                    request.TenantId,
                    actorUserId,
                    actorUserName,
                    contextJson,
                    PolicyDomains.CoreEstablishment,
                    ct);
            }

            await db.SaveChangesAsync(ct);

            if (IsLabScale())
                return (true, null);

            return await ApplyApprovedAsync(request, actorUserId, ct);
        }

        var policyRequest = PolicyDomainFactsBuilders.ForEstablishmentChange(
            request.Id,
            request.TenantId,
            "SubmitForApproval",
            request.ChangeType.ToString(),
            request.Status.ToString(),
            hasOrgScope: false,
            contextJson: contextJson);
        policyRequest.Mode = mode;
        policyRequest.ActorUserId = actorUserId;
        policyRequest.ActorUserName = actorUserName;

        var policyResult = await policyOrchestrator.EnforceAsync(policyRequest, ct);
        if (!policyResult.Allowed)
            return (false, policyResult.Decision.DenyReason ?? "مرفوض بسياسة الملاك.");

        request.Status = EstablishmentChangeStatus.PendingApproval;
        request.WorkflowInstanceId = policyResult.WorkflowInstanceId;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

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

        request.Status = EstablishmentChangeStatus.Cancelled;
        request.WorkflowInstanceId = null;
        await db.SaveChangesAsync(ct);
        return (true, null);
    }

    private PolicyEnforcementMode ResolveMode()
    {
        var mode = configuration["PolicyEngine:Core.Establishment"]
            ?? configuration["PolicyEngine:DefaultMode"]
            ?? "Off";
        return Enum.TryParse<PolicyEnforcementMode>(mode, true, out var parsed)
            ? parsed
            : PolicyEnforcementMode.Off;
    }

    private bool IsLabScale()
    {
        var value = configuration["NationalScope:IsLabScale"];
        return bool.TryParse(value, out var parsed) && parsed;
    }
}
