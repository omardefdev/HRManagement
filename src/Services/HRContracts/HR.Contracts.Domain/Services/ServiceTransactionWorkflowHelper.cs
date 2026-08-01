using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

/// <summary>
/// Milestone transitions on service transactions (not a cross-workflow orchestrator).
/// </summary>
public static class ServiceTransactionWorkflowHelper
{
    public static void AdvanceToCompleted(ServiceTransaction transaction, Guid? byUserId, string? reason)
    {
        while (transaction.State != ServiceTransactionState.Completed)
        {
            var next = GetNextTowardCompleted(transaction.State);
            transaction.MoveTo(next, byUserId, reason);
        }
    }

    public static void AdvanceToRejected(ServiceTransaction transaction, Guid? byUserId, string? reason)
    {
        if (transaction.State is ServiceTransactionState.Rejected or ServiceTransactionState.Cancelled or ServiceTransactionState.Completed or ServiceTransactionState.Archived)
            return;

        if (transaction.State == ServiceTransactionState.PendingApproval)
        {
            transaction.MoveTo(ServiceTransactionState.Rejected, byUserId, reason);
            return;
        }

        if (transaction.State == ServiceTransactionState.InProcess)
            transaction.MoveTo(ServiceTransactionState.PendingApproval, byUserId, null);

        if (transaction.State == ServiceTransactionState.PendingApproval)
            transaction.MoveTo(ServiceTransactionState.Rejected, byUserId, reason);
    }

    public static void AdvanceToCancelled(ServiceTransaction transaction, Guid? byUserId, string? reason)
    {
        if (transaction.State is ServiceTransactionState.Cancelled or ServiceTransactionState.Completed or ServiceTransactionState.Archived)
            return;

        transaction.MoveTo(ServiceTransactionState.Cancelled, byUserId, reason);
    }

    private static ServiceTransactionState GetNextTowardCompleted(ServiceTransactionState current) =>
        current switch
        {
            ServiceTransactionState.Draft => ServiceTransactionState.Submitted,
            ServiceTransactionState.Submitted => ServiceTransactionState.UnderReview,
            ServiceTransactionState.UnderReview => ServiceTransactionState.InProcess,
            ServiceTransactionState.NeedsInfo => ServiceTransactionState.UnderReview,
            ServiceTransactionState.InProcess => ServiceTransactionState.PendingApproval,
            ServiceTransactionState.PendingApproval => ServiceTransactionState.Approved,
            ServiceTransactionState.Rejected => ServiceTransactionState.InProcess,
            ServiceTransactionState.Approved => ServiceTransactionState.Executing,
            ServiceTransactionState.Executing => ServiceTransactionState.PostProcessing,
            ServiceTransactionState.PostProcessing => ServiceTransactionState.Completed,
            _ => throw new InvalidOperationException($"Cannot advance transaction from {current} toward Completed.")
        };
}
