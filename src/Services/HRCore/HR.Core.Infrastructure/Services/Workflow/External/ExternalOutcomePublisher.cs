using System.Text.Json;
using HR.Core.Domain.Entities.Workflow;
using HR.Core.Infrastructure.Data;
using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults;
using HRManagement.ServiceDefaults.Messaging;
using HRManagement.ServiceDefaults.Workflow.CrossService;
using HRManagement.SharedKernel.Tenancy;
using HRManagement.SharedKernel.Workflow;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace HR.Core.Infrastructure.Services.Workflow.External;

/// <summary>
/// PLAN-46 (G-WF-01): tells the owner of a <see cref="ExternalOutcomeEvent.Generic"/> type how its approval ended. It only stages the message
/// in the bus outbox: the caller's unit of work saves it with the decision, so the outcome exists exactly when the decision does. The
/// message id comes from the instance, and the tenant from the instance rather than from whoever decided.
/// </summary>
public sealed class ExternalOutcomePublisher(HRCoreDbContext db, IPublishEndpoint publish)
{
    /// <summary>Stages the outcome of a finished instance. The status and the step history of <paramref name="instance"/> are already set.</summary>
    [StagesOutboxMessages]
    public async Task StageWorkflowOutcomeAsync(WorkflowInstance instance, CancellationToken ct = default)
    {
        var outcome = WorkflowApprovalOutcomes.FromEngineStatus(instance.Status.ToString())
            ?? throw new InvalidOperationException($"Instance {instance.Id} has not finished.");
        var definition = await LoadDefinitionAsync(db, instance, ct);
        var last = LastEntry(instance);

        await publish.Publish<IWorkflowOutcomeDecided>(new
        {
            TenantId = (Guid?)instance.TenantId,
            instance.EntityType,
            instance.EntityId,
            InstanceId = instance.Id,
            WorkflowCode = definition?.Code ?? string.Empty,
            Outcome = outcome,
            DecidedByUserId = last?.ActorId,
            DecidedAt = instance.CompletedAt ?? DateTime.UtcNow,
            Reason = outcome == WorkflowApprovalOutcomes.Approved ? null : last?.Comments,
            Decisions = Decisions(instance, definition),
            CorrelationId = instance.IdempotencyKey,
            SchemaVersion = 1
        }, p =>
        {
            p.MessageId = DeterministicMessageId.For<IWorkflowOutcomeDecided>(instance.Id);
            p.Headers.Set(TenantIdMiddleware.HeaderName, instance.TenantId.ToString("D"));
        }, ct);
    }

    /// <summary>What the owner reads when the event did not reach it: the same words as the event.</summary>
    public static WorkflowApprovalStatusDto ToStatus(WorkflowInstance instance, WorkflowDefinition? definition)
    {
        var final = WorkflowApprovalOutcomes.FromEngineStatus(instance.Status.ToString());
        var current = final is null ? Steps(definition).FirstOrDefault(s => s.Level == instance.CurrentStepLevel) : null;

        return new WorkflowApprovalStatusDto(
            instance.Id,
            instance.EntityType,
            instance.EntityId,
            definition?.Code ?? string.Empty,
            instance.DefinitionVersion,
            final ?? WorkflowApprovalOutcomes.InProgress,
            current?.Level,
            current?.Name,
            current?.RoleRequired,
            Decisions(instance, definition),
            instance.StartedAt,
            instance.CompletedAt,
            $"/ui/hr-core/WorkflowApprovals/Details/{instance.Id:D}",
            final is WorkflowApprovalOutcomes.Rejected or WorkflowApprovalOutcomes.Cancelled ? LastEntry(instance)?.Comments : null,
            instance.IdempotencyKey);
    }

    /// <summary>The definition the instance pinned, published or not.</summary>
    public static Task<WorkflowDefinition?> LoadDefinitionAsync(HRCoreDbContext db, WorkflowInstance instance, CancellationToken ct) =>
        db.WorkflowDefinitions
            .IgnoreTenantFilterForSystem(SystemTenantBypassPurpose.PolicyResolution)
            .AsNoTracking()
            .FirstOrDefaultAsync(d =>
                d.Id == instance.DefinitionId
                && d.Version == instance.DefinitionVersion
                && (d.TenantId == instance.TenantId || d.TenantId == TenantDefaults.DefaultTenantId), ct);

    /// <summary>Every decision taken, in order. A step skipped by its condition is nobody's decision.</summary>
    public static IReadOnlyList<WorkflowStepDecisionDto> Decisions(WorkflowInstance instance, WorkflowDefinition? definition)
    {
        var roles = Steps(definition).ToDictionary(s => s.Level, s => s.RoleRequired ?? string.Empty);
        return History(instance)
            .Where(h => h.Status is WorkflowStepStatus.Approved or WorkflowStepStatus.Rejected or WorkflowStepStatus.Withdrawn)
            .Select(h => new WorkflowStepDecisionDto(h.Level, roles.GetValueOrDefault(h.Level, string.Empty), h.ActorId, h.Status.ToString(), h.ActionAt))
            .ToList();
    }

    private static WorkflowStepHistoryEntry? LastEntry(WorkflowInstance instance) => History(instance).LastOrDefault();

    private static List<WorkflowStepHistoryEntry> History(WorkflowInstance instance) =>
        string.IsNullOrWhiteSpace(instance.StepHistoryJson)
            ? []
            : JsonSerializer.Deserialize<List<WorkflowStepHistoryEntry>>(instance.StepHistoryJson) ?? [];

    private static IReadOnlyList<WorkflowStepSnapshot> Steps(WorkflowDefinition? definition) =>
        definition is null ? [] : WorkflowDefinitionMapper.ToSnapshot(definition).Steps;
}
