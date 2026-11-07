using HRManagement.MessageContracts.Events;
using Microsoft.Extensions.Logging;

namespace HRManagement.ServiceDefaults.Workflow.CrossService;

/// <param name="Finished">The key found an instance that had already ended, and its outcome was applied.</param>
public sealed record WorkflowApprovalSubmitResult(bool Success, string? Error, Guid? InstanceId = null, bool Finished = false);

/// <param name="FinalOutcome">Set when the instance had ended before the withdrawal: Approved, Rejected or Cancelled.</param>
public sealed record WorkflowApprovalWithdrawResult(bool Success, string? Error, string? FinalOutcome = null);

/// <summary>What an owner tells <see cref="WorkflowApprovalSubmission"/> about the record it submits.</summary>
public sealed class WorkflowApprovalSubmitSteps
{
    /// <summary>The start request for submission number <c>n</c>; the key is <c>"{EntityType}:{Id}:{n}"</c>.</summary>
    public required Func<int, WorkflowApprovalStartRequest> Request { get; init; }

    /// <summary>
    /// Rule 3: sets the pending state, the instance and the number, and saves them in one save. Throws when the save fails (for instance on
    /// <c>xmin</c>).
    /// </summary>
    public required Func<Guid, int, CancellationToken, Task> LinkAndSaveAsync { get; init; }

    /// <summary>Saves the number alone: an instance of that number was withdrawn before the record ever pointed at it.</summary>
    public required Func<int, CancellationToken, Task> SpendNumberAsync { get; init; }

    /// <summary>Whether the record, read again from the database, already points at the instance.</summary>
    public required Func<Guid, CancellationToken, Task<bool>> IsLinkedToAsync { get; init; }

    /// <summary>The owner's <c>ApplyOutcomeAsync</c>, the one its consumer runs.</summary>
    public required Func<IWorkflowOutcomeDecided, CancellationToken, Task> ApplyOutcomeAsync { get; init; }
}

/// <summary>
/// PLAN-46 (section 1, rules 2 to 5): how an owner submits and withdraws, so every owner does it the same way.
/// <list type="bullet">
/// <item>The key is pinned per submission: a retry after a failure uses the same number and finds the same instance.</item>
/// <item>A failed start changes nothing: the record stays a draft and the person reads why.</item>
/// <item>A save that fails after a start withdraws the instance on a best-effort basis, unless the record already points at it.</item>
/// <item>A withdrawal that finds the instance ended applies its outcome and never returns the record to draft.</item>
/// </list>
/// The caller puts the record's tenant in <see cref="TenantIdFlow"/> (<see cref="UseTenant"/>).
/// </summary>
public static class WorkflowApprovalSubmission
{
    public const string LocalSaveFailedReason = "فشل الحفظ المحلي";
    public const string SaveFailedMessage = "تعذّر حفظ التقديم. أعد المحاولة.";
    public const string FinishedPendingMessage = "انتهى مسار الاعتماد قبل السحب، وستُطبّق نتيجته قريباً.";
    public const string ChangedMessage = "تغيّر السجل أثناء السحب. أعد تحميل الصفحة.";

    public static async Task<WorkflowApprovalSubmitResult> SubmitAsync(
        IWorkflowApprovalClient client, int submissionNo, WorkflowApprovalSubmitSteps steps, ILogger logger, CancellationToken ct)
    {
        // At most two numbers: the second only when the first was spent by an instance withdrawn before this record pointed at it.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var number = submissionNo + 1;
            var request = steps.Request(number);
            var started = await client.StartAsync(request, ct);
            if (!started.Success || started.InstanceId is not Guid instanceId)
                return new WorkflowApprovalSubmitResult(false, started.Error ?? WorkflowApprovalErrorCodes.MessageFor(started.ErrorCode));

            WorkflowApprovalStatusDto? finished = null;
            if (!started.AlreadyActive)
            {
                finished = await client.GetStatusAsync(instanceId, ct);
                if (finished is null)
                    return new WorkflowApprovalSubmitResult(false, WorkflowApprovalErrorCodes.EngineUnavailableMessage);

                if (finished.Status == WorkflowApprovalOutcomes.Cancelled)
                {
                    await steps.SpendNumberAsync(number, ct);
                    submissionNo = number;
                    continue;
                }

                if (!WorkflowApprovalOutcomes.IsFinal(finished.Status))
                    finished = null;
            }

            try
            {
                await steps.LinkAndSaveAsync(instanceId, number, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (await steps.IsLinkedToAsync(instanceId, ct))
                {
                    // Another submission of the same record saved the same instance first: nothing to undo.
                    logger.LogInformation("Record {EntityId} was linked to instance {InstanceId} by a concurrent submission.", request.EntityId, instanceId);
                    return new WorkflowApprovalSubmitResult(true, null, instanceId);
                }

                logger.LogError(ex, "Saving the submission of {EntityId} failed after instance {InstanceId} started.", request.EntityId, instanceId);
                if (finished is null)
                {
                    var cancelled = await client.CancelAsync(instanceId, LocalSaveFailedReason, ct);
                    if (!cancelled.Success)
                    {
                        // The same key finds the same instance on the next submission.
                        logger.LogWarning("Withdrawing instance {InstanceId} after the failed save did not succeed: {ErrorCode}.",
                            instanceId, cancelled.ErrorCode ?? cancelled.FinalOutcome);
                    }
                }

                return new WorkflowApprovalSubmitResult(false, SaveFailedMessage);
            }

            if (finished is not null)
            {
                try
                {
                    await steps.ApplyOutcomeAsync(WorkflowOutcomeDecision.From(finished, TenantIdFlow.Get()), ct);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
                {
                    // The consumer or the sweep applied the same outcome at the same moment; the record is linked either way.
                    return new WorkflowApprovalSubmitResult(false, ChangedMessage, instanceId);
                }

                return new WorkflowApprovalSubmitResult(true, null, instanceId, Finished: true);
            }

            return new WorkflowApprovalSubmitResult(true, null, instanceId);
        }

        return new WorkflowApprovalSubmitResult(false, WorkflowApprovalErrorCodes.ConflictMessage);
    }

    /// <summary>
    /// Rule 5: withdraws the instance first. When it had ended, its outcome is applied, never a return to draft; when the engine cannot
    /// say how it ended, nothing changes here and the event or the sweep applies it.
    /// </summary>
    public static async Task<WorkflowApprovalWithdrawResult> WithdrawAsync(
        IWorkflowApprovalClient client,
        string entityType,
        Guid entityId,
        Guid instanceId,
        Guid? actor,
        string reason,
        Func<IWorkflowOutcomeDecided, CancellationToken, Task> applyOutcomeAsync,
        CancellationToken ct)
    {
        var cancelled = await client.CancelAsync(instanceId, reason, ct);
        if (cancelled.Success)
        {
            var now = DateTime.UtcNow;
            try
            {
                await applyOutcomeAsync(new WorkflowOutcomeDecision(
                    TenantIdFlow.Get(), entityType, entityId, instanceId, string.Empty, WorkflowApprovalOutcomes.Cancelled, actor, now, reason,
                    [new WorkflowStepDecisionDto(0, string.Empty, actor, WorkflowApprovalOutcomes.ActionWithdrawn, now)], null), ct);
            }
            catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
            {
                // The record changed under this request (xmin). The instance is withdrawn; its Cancelled event or the sweep applies it.
                return new WorkflowApprovalWithdrawResult(false, ChangedMessage);
            }

            return new WorkflowApprovalWithdrawResult(true, null);
        }

        if (!cancelled.AlreadyTerminal)
            return new WorkflowApprovalWithdrawResult(false, cancelled.Error ?? WorkflowApprovalErrorCodes.MessageFor(cancelled.ErrorCode));

        var status = await client.GetStatusAsync(instanceId, ct);
        if (status is null || !WorkflowApprovalOutcomes.IsFinal(status.Status))
            return new WorkflowApprovalWithdrawResult(false, FinishedPendingMessage, cancelled.FinalOutcome);

        try
        {
            await applyOutcomeAsync(WorkflowOutcomeDecision.From(status, TenantIdFlow.Get()), ct);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException)
        {
            // The consumer or the sweep applied the same outcome at the same moment.
            return new WorkflowApprovalWithdrawResult(false, ChangedMessage, status.Status);
        }

        return new WorkflowApprovalWithdrawResult(false, null, status.Status);
    }

    /// <summary>Puts <paramref name="tenantId"/> in the flow until disposed, then restores what was there.</summary>
    public static IDisposable UseTenant(Guid tenantId)
    {
        var previous = TenantIdFlow.Get();
        TenantIdFlow.Set(tenantId);
        return new Restore(previous);
    }

    private sealed class Restore(Guid? previous) : IDisposable
    {
        public void Dispose() => TenantIdFlow.Set(previous);
    }
}
