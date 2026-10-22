using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

public sealed record WorkflowActionActor(Guid? UserId, string? UserName, string? Role);

public sealed record WorkflowActionResult(bool Success, string? Error, WorkflowInstance? Instance);

public interface IWorkflowInstanceActionService
{
    Task<WorkflowActionResult> ApproveAsync(
        Guid instanceId,
        WorkflowActionActor actor,
        string? comments,
        int? targetStepLevel,
        CancellationToken ct = default);

    Task<WorkflowActionResult> RejectAsync(
        Guid instanceId,
        WorkflowActionActor actor,
        string? comments,
        CancellationToken ct = default);
}

public sealed class WorkflowInstanceActionService(
    HRCoreDbContext context,
    IWorkflowEngine engine,
    IWorkflowSignatureService signatures,
    IPublishEndpoint publish,
    RecruitmentRequisitionWorkflowHandler requisitionHandler,
    OfferLetterWorkflowHandler offerHandler,
    SelectionCommitteeWorkflowHandler committeeHandler,
    RecruitmentAppealWorkflowHandler appealHandler,
    EstablishmentChangeWorkflowHandler establishmentChangeHandler) : IWorkflowInstanceActionService
{
    public async Task<WorkflowActionResult> ApproveAsync(
        Guid instanceId,
        WorkflowActionActor actor,
        string? comments,
        int? targetStepLevel,
        CancellationToken ct = default)
    {
        var instance = await context.WorkflowInstances.FindAsync([instanceId], ct);
        if (instance is null)
            return new WorkflowActionResult(false, "Workflow instance not found.", null);

        var definition = await ResolvePinnedDefinitionAsync(instance, ct);
        if (definition is null)
            return new WorkflowActionResult(false, "Workflow definition not found for this instance version.", null);

        var snapshot = WorkflowDefinitionMapper.ToSnapshot(definition);
        var state = WorkflowDefinitionMapper.ToState(instance);

        var guardError = WorkflowStageGuard.ValidateApprove(
            state, snapshot, actor.UserId, instance.InitiatedBy, targetStepLevel);
        if (guardError is not null)
            return new WorkflowActionResult(false, guardError, null);

        var sodError = await ValidateSelfApprovalAsync(instance, definition, actor.UserId, ct);
        if (sodError is not null)
            return new WorkflowActionResult(false, sodError, null);

        var result = engine.ApproveStep(state, snapshot, actor.UserId, actor.UserName, comments);
        if (!result.Success)
            return new WorkflowActionResult(false, result.Error, null);

        instance.CurrentStepLevel = result.NewStepLevel;
        instance.Status = result.NewStatus;

        if (result.HistoryEntry is not null)
        {
            var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
            history.Add(result.HistoryEntry);
            instance.StepHistoryJson = JsonSerializer.Serialize(history);

            await signatures.RecordStepActionAsync(
                instance, definition, result.HistoryEntry, "approve", actor.UserId, actor.Role, ct);
        }

        if (result.NewStatus == WorkflowStatus.Completed)
            instance.CompletedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);

        if (result.NewStatus == WorkflowStatus.Completed)
            await PublishWorkflowOutcomeAsync(instance, approved: true, ct: ct);

        return new WorkflowActionResult(true, null, instance);
    }

    public async Task<WorkflowActionResult> RejectAsync(
        Guid instanceId,
        WorkflowActionActor actor,
        string? comments,
        CancellationToken ct = default)
    {
        var instance = await context.WorkflowInstances.FindAsync([instanceId], ct);
        if (instance is null)
            return new WorkflowActionResult(false, "Workflow instance not found.", null);

        var definition = await ResolvePinnedDefinitionAsync(instance, ct);
        if (definition is null)
            return new WorkflowActionResult(false, "Workflow definition not found for this instance version.", null);

        var state = WorkflowDefinitionMapper.ToState(instance);
        var result = engine.Reject(state, actor.UserId, actor.UserName, comments);
        if (!result.Success)
            return new WorkflowActionResult(false, result.Error, null);

        instance.Status = result.NewStatus;

        if (result.HistoryEntry is not null)
        {
            var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
            history.Add(result.HistoryEntry);
            instance.StepHistoryJson = JsonSerializer.Serialize(history);

            await signatures.RecordStepActionAsync(
                instance, definition, result.HistoryEntry, "reject", actor.UserId, actor.Role, ct);
        }

        instance.CompletedAt = DateTime.UtcNow;
        await context.SaveChangesAsync(ct);

        await PublishWorkflowOutcomeAsync(instance, approved: false, reason: comments, ct: ct);

        return new WorkflowActionResult(true, null, instance);
    }

    private async Task<string?> ValidateSelfApprovalAsync(
        WorkflowInstance instance,
        WorkflowDefinition definition,
        Guid? actorUserId,
        CancellationToken ct)
    {
        if (!actorUserId.HasValue)
            return null;

        if (string.Equals(instance.EntityType, "RecruitmentRequisition", StringComparison.OrdinalIgnoreCase))
        {
            var requisition = await context.RecruitmentRequisitions.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == instance.EntityId, ct);
            if (requisition?.CreatedBy == actorUserId)
                return "لا يمكن للمنشئ اعتماد طلبه الخاص.";
        }

        if (string.Equals(instance.EntityType, "EstablishmentChangeRequest", StringComparison.OrdinalIgnoreCase))
        {
            var changeRequest = await context.EstablishmentChangeRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == instance.EntityId, ct);
            if (changeRequest?.CreatedBy == actorUserId)
                return "لا يمكن للمنشئ اعتماد طلبه الخاص.";
        }

        // HCM-028: the session creator never approves the committee; member and quorum checks need the actor's
        // employee id and live in SelectionCommitteeLifecycleService.ApproveStepAsync.
        if (string.Equals(instance.EntityType, "SelectionCommitteeSession", StringComparison.OrdinalIgnoreCase))
        {
            var session = await context.SelectionCommitteeSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == instance.EntityId, ct);
            var committeeError = SelectionCommitteeRules.ValidateApproval(
                instance.CurrentStepLevel, members: null, session?.CreatedBy, actorUserId, actorEmployeeId: null);
            if (committeeError is not null)
                return committeeError;
        }

        if (string.Equals(instance.EntityType, "OfferLetter", StringComparison.OrdinalIgnoreCase))
        {
            var offer = await context.OfferLetters.AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == instance.EntityId, ct);
            if (offer?.CreatedBy == actorUserId)
                return "لا يمكن للمنشئ اعتماد عرضه الخاص.";

            // HCM-027: merit override offers need a distinct approver for every step.
            if (OfferOverrideApprovalRules.IsOverrideWorkflow(definition.Code)
                && OfferOverrideApprovalRules.HasApprovedEarlierStep(instance.StepHistoryJson, instance.CurrentStepLevel, actorUserId))
                return OfferOverrideApprovalRules.SameApproverMessage;
        }

        return null;
    }

    private async Task PublishWorkflowOutcomeAsync(
        WorkflowInstance instance,
        bool approved,
        string? reason = null,
        CancellationToken ct = default)
    {
        if (string.Equals(instance.EntityType, "AssetAssignment", StringComparison.OrdinalIgnoreCase))
        {
            await PublishAssetAssignmentOutcomeAsync(instance, approved, reason);
            return;
        }

        if (string.Equals(instance.EntityType, "AssetLossReport", StringComparison.OrdinalIgnoreCase))
        {
            await PublishAssetLossOutcomeAsync(instance, approved, reason);
            return;
        }

        if (string.Equals(instance.EntityType, "RecruitmentRequisition", StringComparison.OrdinalIgnoreCase))
            await requisitionHandler.HandleAsync(instance, approved, reason, ct);

        if (string.Equals(instance.EntityType, "OfferLetter", StringComparison.OrdinalIgnoreCase))
            await offerHandler.HandleAsync(instance, approved, reason, ct);

        if (string.Equals(instance.EntityType, "SelectionCommitteeSession", StringComparison.OrdinalIgnoreCase))
            await committeeHandler.HandleAsync(instance, approved, reason, ct);

        if (string.Equals(instance.EntityType, "RecruitmentAppeal", StringComparison.OrdinalIgnoreCase))
            await appealHandler.HandleAsync(instance, approved, reason, ct);

        if (string.Equals(instance.EntityType, "EstablishmentChangeRequest", StringComparison.OrdinalIgnoreCase))
            await establishmentChangeHandler.HandleAsync(instance, approved, reason, ct);
    }

    private async Task PublishAssetAssignmentOutcomeAsync(WorkflowInstance instance, bool approved, string? reason = null)
    {
        if (!string.Equals(instance.EntityType, "AssetAssignment", StringComparison.OrdinalIgnoreCase))
            return;

        if (approved)
        {
            var ctx = ParseAssignmentContext(instance.ContextJson);
            await publish.Publish<IAssetAssignmentApproved>(new
            {
                AssignmentId = instance.EntityId,
                AssetId = ctx.AssetId,
                EmployeeId = ctx.EmployeeId,
                OccurredAt = DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            });
        }
        else
        {
            await publish.Publish<IAssetAssignmentRejected>(new
            {
                AssignmentId = instance.EntityId,
                AssetId = ParseAssignmentContext(instance.ContextJson).AssetId,
                Reason = reason,
                OccurredAt = DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            });
        }
    }

    private async Task PublishAssetLossOutcomeAsync(WorkflowInstance instance, bool approved, string? reason = null)
    {
        var ctx = ParseLossContext(instance.ContextJson);
        if (approved)
        {
            await publish.Publish<IAssetLossApproved>(new
            {
                LossReportId = instance.EntityId,
                AssetId = ctx.AssetId,
                OccurredAt = DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            });
        }
        else
        {
            await publish.Publish<IAssetLossRejected>(new
            {
                LossReportId = instance.EntityId,
                AssetId = ctx.AssetId,
                Reason = reason,
                OccurredAt = DateTime.UtcNow,
                SourceSystem = "hr-core",
                SchemaVersion = 1
            });
        }
    }

    private static (Guid AssetId, Guid LossReportId) ParseLossContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (Guid.Empty, Guid.Empty);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var assetId = root.TryGetProperty("assetId", out var a) && Guid.TryParse(a.GetString(), out var ag) ? ag : Guid.Empty;
            var lossReportId = root.TryGetProperty("lossReportId", out var l) && Guid.TryParse(l.GetString(), out var lg) ? lg : Guid.Empty;
            return (assetId, lossReportId);
        }
        catch
        {
            return (Guid.Empty, Guid.Empty);
        }
    }

    private static (Guid AssetId, Guid EmployeeId) ParseAssignmentContext(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return (Guid.Empty, Guid.Empty);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var assetId = root.TryGetProperty("assetId", out var a) && Guid.TryParse(a.GetString(), out var ag) ? ag : Guid.Empty;
            var employeeId = root.TryGetProperty("employeeId", out var e) && Guid.TryParse(e.GetString(), out var eg) ? eg : Guid.Empty;
            return (assetId, employeeId);
        }
        catch
        {
            return (Guid.Empty, Guid.Empty);
        }
    }

    private Task<WorkflowDefinition?> ResolvePinnedDefinitionAsync(WorkflowInstance instance, CancellationToken ct) =>
        context.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == instance.DefinitionId
                && d.Version == instance.DefinitionVersion
                && (d.TenantId == instance.TenantId || d.TenantId == TenantDefaults.DefaultTenantId), ct);
}
