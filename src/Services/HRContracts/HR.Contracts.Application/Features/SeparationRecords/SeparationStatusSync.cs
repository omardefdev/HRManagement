using HR.Contracts.Application.Abstractions;
using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;
using HR.Contracts.Domain.Services;
using Microsoft.EntityFrameworkCore;

namespace HR.Contracts.Application.Features.SeparationRecords;

/// <summary>
/// PLAN-60 (§1, P-4): a separation's transition, with its linked <see cref="ServiceTransaction"/> moved along the transaction's own
/// allowed transitions and a history row for every transition, attributed to its actor. Nothing here saves: the caller saves once.
/// </summary>
public static class SeparationStatusSync
{
    /// <summary>The record's transition (<see cref="SeparationWorkflowRules.Transition"/>), then its transaction's.</summary>
    public static async Task TransitionAsync(
        IContractsRepository repository,
        SeparationRecord record,
        SeparationStatus to,
        Guid? actor,
        string? reason,
        CancellationToken ct,
        SeparationSubmission? submission = null)
    {
        var from = record.Status;
        SeparationWorkflowRules.Transition(record, to, actor, reason, submission);

        var transaction = await repository.ServiceTransactions
            .FirstOrDefaultAsync(t => t.SeparationRecordId == record.Id, ct);
        if (transaction is null)
            return;

        var before = transaction.StateHistory.Count;
        Follow(transaction, from, to, actor, reason);
        await repository.TrackNewHistoryAsync(transaction, before, ct);
    }

    /// <summary>
    /// Moves <paramref name="transaction"/> to the state that stands for <paramref name="to"/>, step by step through the transaction's own
    /// table, and writes a history row when its state does not change. No row is added to the transaction's table.
    /// </summary>
    public static void Follow(ServiceTransaction transaction, SeparationStatus from, SeparationStatus to, Guid? actor, string? reason)
    {
        var note = $"سجل الفصل: {SeparationWorkflowRules.DisplayName(from)} ← {SeparationWorkflowRules.DisplayName(to)}"
                   + (string.IsNullOrWhiteSpace(reason) ? string.Empty : $" — {reason.Trim()}");
        if (note.Length > 1000)
            note = note[..1000];

        var path = PathTo(transaction.State, to);
        if (path.Count == 0)
        {
            transaction.AppendHistory(actor, note);
            return;
        }

        foreach (var next in path)
            transaction.MoveTo(next, actor, note);
    }

    /// <summary>
    /// The transaction's states that lead to the one standing for <paramref name="to"/>, written out rather than searched: a search would
    /// cross <c>Approved</c> on the way from <c>PendingApproval</c> to <c>Cancelled</c>, which never happened. A withdrawal, a reopening and a
    /// cancellation after <c>PendingApproval</c> go through <c>InProcess</c> (P-4). An empty path means the state already stands for it,
    /// or the transaction (an older one) is somewhere this status cannot be shown: its history row says what happened.
    /// </summary>
    public static IReadOnlyList<ServiceTransactionState> PathTo(ServiceTransactionState current, SeparationStatus to)
    {
        IReadOnlyList<ServiceTransactionState> path = to switch
        {
            SeparationStatus.PendingApproval => current switch
            {
                ServiceTransactionState.Draft =>
                    [ServiceTransactionState.Submitted, ServiceTransactionState.UnderReview, ServiceTransactionState.InProcess, ServiceTransactionState.PendingApproval],
                ServiceTransactionState.Submitted =>
                    [ServiceTransactionState.UnderReview, ServiceTransactionState.InProcess, ServiceTransactionState.PendingApproval],
                ServiceTransactionState.UnderReview => [ServiceTransactionState.InProcess, ServiceTransactionState.PendingApproval],
                ServiceTransactionState.NeedsInfo =>
                    [ServiceTransactionState.UnderReview, ServiceTransactionState.InProcess, ServiceTransactionState.PendingApproval],
                ServiceTransactionState.InProcess => [ServiceTransactionState.PendingApproval],
                ServiceTransactionState.Rejected => [ServiceTransactionState.InProcess, ServiceTransactionState.PendingApproval],
                _ => []
            },
            SeparationStatus.Draft => current is ServiceTransactionState.PendingApproval or ServiceTransactionState.Rejected
                ? [ServiceTransactionState.InProcess]
                : [],
            SeparationStatus.Approved => current == ServiceTransactionState.PendingApproval ? [ServiceTransactionState.Approved] : [],
            SeparationStatus.Rejected => current == ServiceTransactionState.PendingApproval ? [ServiceTransactionState.Rejected] : [],
            SeparationStatus.Executing or SeparationStatus.AwaitingIntervention =>
                current == ServiceTransactionState.Approved ? [ServiceTransactionState.Executing] : [],
            SeparationStatus.Completed => current switch
            {
                ServiceTransactionState.Executing =>
                    [ServiceTransactionState.PostProcessing, ServiceTransactionState.Completed, ServiceTransactionState.Archived],
                ServiceTransactionState.PostProcessing => [ServiceTransactionState.Completed, ServiceTransactionState.Archived],
                ServiceTransactionState.Completed => [ServiceTransactionState.Archived],
                _ => []
            },
            SeparationStatus.Cancelled => current switch
            {
                ServiceTransactionState.PendingApproval => [ServiceTransactionState.InProcess, ServiceTransactionState.Cancelled],
                ServiceTransactionState.Cancelled or ServiceTransactionState.Completed or ServiceTransactionState.Archived => [],
                _ => ServiceTransaction.CanMove(current, ServiceTransactionState.Cancelled) ? [ServiceTransactionState.Cancelled] : []
            },
            _ => []
        };

        // Every step must be one the transaction's own table allows.
        var state = current;
        foreach (var next in path)
        {
            if (!ServiceTransaction.CanMove(state, next))
                return [];
            state = next;
        }

        return path;
    }
}
