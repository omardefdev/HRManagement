using HR.Contracts.Domain.Entities;
using HR.Contracts.Domain.Enums;

namespace HR.Contracts.Domain.Services;

public static class HireWorkflowRules
{
    public static bool HasOpenAppointmentTransaction(IEnumerable<ServiceTransaction> transactions) =>
        transactions.Any(t =>
            t.TransactionType == ServiceTransactionType.Appointment
            && t.State is not ServiceTransactionState.Archived
            and not ServiceTransactionState.Cancelled);

    public static bool CanAdvanceToPendingApproval(
        ServiceTransaction transaction,
        bool hasContract,
        bool hasCommencement)
    {
        if (transaction.TransactionType != ServiceTransactionType.Appointment)
            return true;

        return hasContract && hasCommencement;
    }

    public static bool CanAdvanceToCompleted(IEnumerable<HireChecklist> checklist)
    {
        var items = checklist.ToList();
        if (items.Count == 0)
            return false;

        return items.All(c => c.IsCompleted);
    }

    public static bool CanPublishHireCompleted(
        ServiceTransaction transaction,
        IEnumerable<HireChecklist> checklist)
    {
        if (transaction.TransactionType != ServiceTransactionType.Appointment)
            return false;

        if (transaction.State is not ServiceTransactionState.PostProcessing
            and not ServiceTransactionState.Completed
            and not ServiceTransactionState.Executing)
            return false;

        // INT-003: HireArchived is acknowledged by Archive party inside the saga — not required before start.
        var requiredBeforeSaga = checklist
            .Where(c => c.Step != HireStep.HireArchived)
            .ToList();
        if (requiredBeforeSaga.Count == 0)
            return false;
        return requiredBeforeSaga.All(c => c.IsCompleted);
    }

    public static void MarkStepCompleted(
        HireChecklist item,
        string? notes = null,
        string? evidenceRef = null,
        Guid? completedBy = null,
        string? completedByName = null)
    {
        var (_, _, requiresEvidence) = HireChecklistDefaults.GovernanceFor(item.Step);
        var evidence = string.IsNullOrWhiteSpace(evidenceRef) ? notes : evidenceRef;
        if (requiresEvidence && string.IsNullOrWhiteSpace(evidence) && string.IsNullOrWhiteSpace(item.EvidenceRef))
            throw new InvalidOperationException($"الدليل (EvidenceRef) مطلوب لخطوة {item.Step}.");

        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        if (!string.IsNullOrWhiteSpace(notes))
            item.Notes = notes;
        if (!string.IsNullOrWhiteSpace(evidence))
            item.EvidenceRef = evidence.Trim();
        if (completedBy.HasValue)
            item.CompletedBy = completedBy;
        if (!string.IsNullOrWhiteSpace(completedByName))
            item.CompletedByName = completedByName.Trim();
    }

    /// <summary>FUN-001-D: reject starting saga when appointment already cancelled/rejected.</summary>
    public static bool IsRejectedOrCancelled(ServiceTransaction transaction) =>
        transaction.State is ServiceTransactionState.Cancelled
            or ServiceTransactionState.Archived
            or ServiceTransactionState.Rejected;
}
