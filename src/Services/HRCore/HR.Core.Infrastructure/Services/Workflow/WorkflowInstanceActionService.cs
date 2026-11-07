using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Domain.Services;
using HR.Core.Infrastructure.Data;
using HR.Core.Infrastructure.Services.Establishment;
using HR.Core.Infrastructure.Services.Recruitment;
using HR.Core.Infrastructure.Services.Recruitment.Competitions;
using HR.Core.Infrastructure.Services.Workflow.Gates;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.ServiceDefaults.Workflow;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow;

public sealed record WorkflowActionResult(bool Success, string? Error, WorkflowInstance? Instance)
{
    /// <summary>PLAN-44: the instance changed under this action; nothing was written.</summary>
    public bool IsConflict => Error == WorkflowConcurrency.ConflictMessage;
}

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
        CancellationToken ct = default,
        int? targetStepLevel = null);

    /// <summary>
    /// PLAN-44: runs the handler of a finished instance again, for an outcome that slipped before this plan. The instance is not touched,
    /// and the caller owns the save. Handlers check the entity's state and link, so a second run does nothing.
    /// </summary>
    Task ReapplyOutcomeAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default);
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
    EstablishmentChangeWorkflowHandler establishmentChangeHandler,
    // HCM-031 (PLAN-31): optional so the existing manual constructions (tests) keep compiling; DI always supplies it.
    CompetitionWorkflowHandler? competitionHandler = null,
    // PLAN-25 (G-04): DI supplies the evaluator with every gate and the Workflow options; manual constructions get the
    // same gates with the default options (no admin bypass).
    WorkflowApprovalEvaluator? evaluator = null) : IWorkflowInstanceActionService
{
    private readonly WorkflowApprovalEvaluator _evaluator = evaluator ?? WorkflowApprovalEvaluator.CreateDefault(context);

    /// <summary>
    /// PLAN-25 (G-14): entity types whose completed or rejected workflow has an effect here (<see cref="PublishWorkflowOutcomeAsync"/>).
    /// <c>WorkflowDefinitionsHaveHandlersTests</c> requires every published definition to be one of these or allow-listed.
    /// </summary>
    public static readonly IReadOnlySet<string> HandledEntityTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "AssetAssignment",
        "AssetLossReport",
        "RecruitmentRequisition",
        "OfferLetter",
        "SelectionCommitteeSession",
        "RecruitmentAppeal",
        "EstablishmentChangeRequest",
        CompetitionLifecycleService.EntityType,
        CompetitionResultsService.EntityType
    };

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

        if (WorkflowConcurrency.IsStale(instance, targetStepLevel))
            return Conflict("approve");

        var definition = await ResolvePinnedDefinitionAsync(instance, ct);
        if (definition is null)
            return new WorkflowActionResult(false, "Workflow definition not found for this instance version.", null);

        // Stage guard (status, no skipping, initiator, step role) then the entity gate (SoD, quorum, reviewer, scope).
        var blockReason = await _evaluator.GetBlockReasonAsync(
            instance, definition, actor, WorkflowActionKind.Approve, targetStepLevel, ct);
        if (blockReason is not null)
            return new WorkflowActionResult(false, blockReason, null);

        var snapshot = WorkflowDefinitionMapper.ToSnapshot(definition);
        var state = WorkflowDefinitionMapper.ToState(instance);
        var stepRole = snapshot.Steps.FirstOrDefault(s => s.Level == instance.CurrentStepLevel)?.RoleRequired;

        var result = engine.ApproveStep(state, snapshot, actor.UserId, actor.UserName, comments);
        if (!result.Success)
            return new WorkflowActionResult(false, result.Error, null);

        // PLAN-44: every refusal is above, so nothing a caller staged (the appeal's outcome) is saved by a refused action.
        try
        {
            await OutboxUnitOfWork.ExecuteAsync(context, async token =>
            {
                instance.CurrentStepLevel = result.NewStepLevel;
                instance.Status = result.NewStatus;

                if (result.HistoryEntry is not null)
                {
                    AppendHistory(instance, result.HistoryEntry);
                    await signatures.RecordStepActionAsync(
                        instance, definition, result.HistoryEntry, "approve", actor.UserId, actor.SignatureRole(stepRole), token);
                }

                if (result.NewStatus == WorkflowStatus.Completed)
                {
                    instance.CompletedAt = DateTime.UtcNow;
                    // PLAN-40 (class A): the asset outcome is staged before the save, so it commits with the instance.
                    await StageAssetOutcomeAsync(instance, approved: true, actor.UserId);
                    await PublishWorkflowOutcomeAsync(instance, approved: true, ct: token);
                }
            }, ct);
            return new WorkflowActionResult(true, null, instance);
        }
        catch (Exception ex) when (ex is WorkflowOutcomeRefusedException || WorkflowConcurrency.LostRace(ex))
        {
            return Undone("approve", ex);
        }
    }

    public async Task<WorkflowActionResult> RejectAsync(
        Guid instanceId,
        WorkflowActionActor actor,
        string? comments,
        CancellationToken ct = default,
        int? targetStepLevel = null)
    {
        var instance = await context.WorkflowInstances.FindAsync([instanceId], ct);
        if (instance is null)
            return new WorkflowActionResult(false, "Workflow instance not found.", null);

        if (WorkflowConcurrency.IsStale(instance, targetStepLevel))
            return Conflict("reject");

        var definition = await ResolvePinnedDefinitionAsync(instance, ct);
        if (definition is null)
            return new WorkflowActionResult(false, "Workflow definition not found for this instance version.", null);

        // PLAN-25 (G-04): a reject passes the same role check and gates (the committee quorum is approve-only).
        var blockReason = await _evaluator.GetBlockReasonAsync(instance, definition, actor, WorkflowActionKind.Reject, ct: ct);
        if (blockReason is not null)
            return new WorkflowActionResult(false, blockReason, null);

        var stepRole = WorkflowDefinitionMapper.ToSnapshot(definition).Steps
            .FirstOrDefault(s => s.Level == instance.CurrentStepLevel)?.RoleRequired;
        var state = WorkflowDefinitionMapper.ToState(instance);
        var result = engine.Reject(state, actor.UserId, actor.UserName, comments);
        if (!result.Success)
            return new WorkflowActionResult(false, result.Error, null);

        try
        {
            await OutboxUnitOfWork.ExecuteAsync(context, async token =>
            {
                instance.Status = result.NewStatus;

                if (result.HistoryEntry is not null)
                {
                    AppendHistory(instance, result.HistoryEntry);
                    await signatures.RecordStepActionAsync(
                        instance, definition, result.HistoryEntry, "reject", actor.UserId, actor.SignatureRole(stepRole), token);
                }

                instance.CompletedAt = DateTime.UtcNow;
                await StageAssetOutcomeAsync(instance, approved: false, actor.UserId, reason: comments);
                await PublishWorkflowOutcomeAsync(instance, approved: false, reason: comments, ct: token);
            }, ct);
            return new WorkflowActionResult(true, null, instance);
        }
        catch (Exception ex) when (ex is WorkflowOutcomeRefusedException || WorkflowConcurrency.LostRace(ex))
        {
            return Undone("reject", ex);
        }
    }

    /// <summary>
    /// PLAN-44 (G-WF-12): the unit of work above commits the instance, the signature, the entity's change and the outbox row together, and
    /// makes the last save after everything was staged (PLAN-40's order). Here the transaction has rolled back: a decision that lost the
    /// race on <c>xmin</c> or to a deadlock gets the conflict message and is never retried, because the step may have moved on; a refused outcome gets its
    /// reason. Any other failure of a handler propagates, and rolls back the same way.
    /// </summary>
    private WorkflowActionResult Undone(string action, Exception failure)
    {
        // Nothing of the decision may stay tracked: a later save in this request would write it again.
        context.ChangeTracker.Clear();
        return failure is WorkflowOutcomeRefusedException
            ? new WorkflowActionResult(false, failure.Message, null)
            : Conflict(action);
    }

    private static WorkflowActionResult Conflict(string action)
    {
        WorkflowIntegrityMetrics.RecordConflict(action);
        return new WorkflowActionResult(false, WorkflowConcurrency.ConflictMessage, null);
    }

    private static void AppendHistory(WorkflowInstance instance, WorkflowStepHistoryEntry entry)
    {
        var history = JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];
        history.Add(entry);
        instance.StepHistoryJson = JsonSerializer.Serialize(history);
    }

    /// <summary>
    /// PLAN-40 (class A, D-40-2/D-40-3): the custody outcomes (Admin.Logistics consumes them) are published through the
    /// bus outbox before the caller's single save, with a message id derived from the workflow instance, so a replay
    /// (MessagingRepairController) is deduplicated by the consumer's inbox. Other entity types publish nothing here.
    /// </summary>
    [StagesOutboxMessages]
    private async Task StageAssetOutcomeAsync(WorkflowInstance instance, bool approved, Guid? actor, string? reason = null)
    {
        if (WorkflowAssetOutcomes.IsAssetWorkflow(instance))
            await WorkflowAssetOutcomes.PublishOutcomeAsync(context, publish, instance, approved, reason, actor);
    }

    public Task ReapplyOutcomeAsync(WorkflowInstance instance, bool approved, string? reason, CancellationToken ct = default) =>
        PublishWorkflowOutcomeAsync(instance, approved, reason, ct);

    private async Task PublishWorkflowOutcomeAsync(
        WorkflowInstance instance,
        bool approved,
        string? reason = null,
        CancellationToken ct = default)
    {
        // The custody outcomes were staged with the instance's save (StageAssetOutcomeAsync).
        if (string.Equals(instance.EntityType, "AssetAssignment", StringComparison.OrdinalIgnoreCase)
            || string.Equals(instance.EntityType, "AssetLossReport", StringComparison.OrdinalIgnoreCase))
            return;

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

        if (competitionHandler is not null)
            await competitionHandler.HandleAsync(instance, approved, reason, ct);
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
