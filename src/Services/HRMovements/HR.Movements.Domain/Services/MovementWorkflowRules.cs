using HR.Movements.Domain.Entities;
using HR.Movements.Domain.Enums;
using HRManagement.SharedKernel.Enums;

namespace HR.Movements.Domain.Services;

public static class MovementWorkflowRules
{
    public static bool IsEffectiveDateReached(DateOnly effectiveDate, DateOnly? todayUtc = null)
    {
        var today = todayUtc ?? DateOnly.FromDateTime(DateTime.UtcNow);
        return effectiveDate <= today;
    }

    public static bool CanCancelTransfer(TransferRequest t) =>
        !t.IsCancelled && t.Status is not ApprovalStatus.Executed;

    public static bool CanCancelAfterExecutePreEffective(TransferRequest t) =>
        !t.IsCancelled
        && t.Status == ApprovalStatus.Executed
        && t.EffectiveDate > DateOnly.FromDateTime(DateTime.UtcNow);

    public static void SoftCancelTransfer(TransferRequest t, string? reason)
    {
        t.IsCancelled = true;
        t.CancelledAt = DateTime.UtcNow;
        t.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء قبل النفاذ" : reason.Trim();
    }

    public static void SoftCancelSecondment(SecondmentRequest s, string? reason)
    {
        s.IsCancelled = true;
        s.CancelledAt = DateTime.UtcNow;
        s.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء قبل النفاذ" : reason.Trim();
    }

    public static void SoftCancelLoan(LoanAssignmentRequest l, string? reason)
    {
        l.IsCancelled = true;
        l.CancelledAt = DateTime.UtcNow;
        l.CancellationReason = string.IsNullOrWhiteSpace(reason) ? "إلغاء قبل النفاذ" : reason.Trim();
    }

    public static bool RequiredChecklistComplete(IEnumerable<MovementChecklist> items)
    {
        var required = items.Where(i => i.IsRequired).ToList();
        if (required.Count == 0) return false;
        return required.All(i => i.IsCompleted && !string.IsNullOrWhiteSpace(i.EvidenceRef));
    }

    public static bool PreExecuteChecklistComplete(IEnumerable<MovementChecklist> items)
    {
        // Party steps (Financial/Org/Directory/Assets/Executed/Ended) complete during/after saga.
        var gate = items
            .Where(i => i.IsRequired
                        && i.Step is MovementChecklistStep.RequestRecorded
                            or MovementChecklistStep.Approved)
            .ToList();
        if (gate.Count == 0) return false;
        return gate.All(i => i.IsCompleted && !string.IsNullOrWhiteSpace(i.EvidenceRef));
    }

    /// <summary>Light conflict gate: overlapping active secondment/loan or pending/executed transfer same employee.</summary>
    public static bool HasConcurrentMovementConflict(
        Guid employeeId,
        Guid excludeRequestId,
        IEnumerable<TransferRequest> transfers,
        IEnumerable<SecondmentRequest> secondments,
        IEnumerable<LoanAssignmentRequest> loans)
    {
        var activeTransfer = transfers.Any(t =>
            t.Id != excludeRequestId
            && t.EmployeeId == employeeId
            && !t.IsCancelled
            && t.Status is ApprovalStatus.Approved or ApprovalStatus.Executed);
        if (activeTransfer) return true;

        var activeSecondment = secondments.Any(s =>
            s.Id != excludeRequestId
            && s.EmployeeId == employeeId
            && !s.IsCancelled
            && s.Status is SecondmentStatus.Approved or SecondmentStatus.Active);
        if (activeSecondment) return true;

        return loans.Any(l =>
            l.Id != excludeRequestId
            && l.EmployeeId == employeeId
            && !l.IsCancelled
            && l.Status is LoanAssignmentStatus.Approved or LoanAssignmentStatus.Active);
    }

    public static void MarkStep(
        MovementChecklist item,
        Guid? by,
        string evidence,
        string? notes = null)
    {
        if (string.IsNullOrWhiteSpace(evidence))
            throw new InvalidOperationException("EvidenceRef مطلوب.");
        item.IsCompleted = true;
        item.CompletedAt = DateTime.UtcNow;
        item.CompletedBy = by;
        item.EvidenceRef = evidence.Trim();
        if (!string.IsNullOrWhiteSpace(notes))
            item.Notes = notes.Trim();
    }
}
