using HRManagement.MessageContracts.Events;
using HRManagement.ServiceDefaults.Workflow.CrossService;

namespace HRManagement.TestSupport.Workflow;

/// <summary>
/// PLAN-46: a stand-in for HR.Core's engine, for the tests of an owner (PLAN-60 uses it too). It records every call, keeps one instance per
/// key as the engine does, answers what a test tells it to, and builds the outcome events a test delivers by hand.
/// </summary>
public sealed class FakeWorkflowApprovalClient : IWorkflowApprovalClient
{
    private readonly Dictionary<string, Guid> _instancesByKey = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, WorkflowApprovalStatusDto> _statuses = [];

    public List<WorkflowApprovalStartRequest> Starts { get; } = [];

    public List<(Guid InstanceId, string Reason)> Cancels { get; } = [];

    public List<Guid> StatusReads { get; } = [];

    /// <summary>Set to answer the next starts with this failure.</summary>
    public WorkflowApprovalStartResult? StartFailure { get; set; }

    /// <summary>Set to answer the next withdrawals with this result.</summary>
    public WorkflowApprovalCancelResult? CancelResult { get; set; }

    /// <summary>The instance a start with this key found already finished.</summary>
    public string? FinishedKey { get; set; }

    public Task<WorkflowApprovalStartResult> StartAsync(WorkflowApprovalStartRequest request, CancellationToken ct)
    {
        Starts.Add(request);
        if (StartFailure is { } failure)
            return Task.FromResult(failure);

        if (!_instancesByKey.TryGetValue(request.IdempotencyKey, out var instanceId))
        {
            instanceId = Guid.NewGuid();
            _instancesByKey[request.IdempotencyKey] = instanceId;
        }

        var running = !string.Equals(FinishedKey, request.IdempotencyKey, StringComparison.Ordinal);
        return Task.FromResult(new WorkflowApprovalStartResult(true, instanceId, running, null, null));
    }

    public Task<WorkflowApprovalCancelResult> CancelAsync(Guid instanceId, string reason, CancellationToken ct)
    {
        Cancels.Add((instanceId, reason));
        return Task.FromResult(CancelResult ?? new WorkflowApprovalCancelResult(true, false, null, null, null));
    }

    public Task<WorkflowApprovalStatusDto?> GetStatusAsync(Guid instanceId, CancellationToken ct)
    {
        StatusReads.Add(instanceId);
        return Task.FromResult(_statuses.GetValueOrDefault(instanceId));
    }

    /// <summary>What <see cref="GetStatusAsync"/> answers for the instance from now on.</summary>
    public void SetStatus(
        Guid instanceId,
        string status,
        string entityType = "Test",
        Guid? entityId = null,
        string? reason = null,
        IReadOnlyList<WorkflowStepDecisionDto>? decisions = null) =>
        _statuses[instanceId] = new WorkflowApprovalStatusDto(
            instanceId,
            entityType,
            entityId ?? Guid.NewGuid(),
            "TEST",
            1,
            status,
            null,
            null,
            null,
            decisions ?? [],
            DateTime.UtcNow.AddMinutes(-30),
            WorkflowApprovalOutcomes.IsFinal(status) ? DateTime.UtcNow : null,
            $"/ui/hr-core/WorkflowApprovals/Details/{instanceId:D}",
            reason);

    /// <summary>An outcome event, as HR.Core publishes it.</summary>
    public static IWorkflowOutcomeDecided Outcome(
        string entityType,
        Guid entityId,
        Guid instanceId,
        string outcome,
        Guid? tenantId = null,
        Guid? decidedBy = null,
        string? reason = null,
        IReadOnlyList<WorkflowStepDecisionDto>? decisions = null,
        int schemaVersion = 1) =>
        new WorkflowOutcomeDecision(
            tenantId,
            entityType,
            entityId,
            instanceId,
            "TEST",
            outcome,
            decidedBy,
            DateTime.UtcNow,
            reason,
            decisions ?? [],
            null)
        { SchemaVersion = schemaVersion };
}
