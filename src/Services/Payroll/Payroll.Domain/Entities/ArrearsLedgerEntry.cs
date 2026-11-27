using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>PAY-004: per-employee arrears from partial payroll disbursement.</summary>
[Display(Name = "قيد متأخرات")]
public class ArrearsLedgerEntry : AuditableEntity
{
    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    [Required]
    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    [Display(Name = "المبلغ الأصلي")]
    public decimal OriginalAmount { get; set; }

    [Display(Name = "المبلغ المدفوع")]
    public decimal PaidAmount { get; set; }

    [Display(Name = "المتأخرات")]
    public decimal ArrearsAmount { get; set; }

    [MaxLength(500)]
    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [MaxLength(32)]
    [Display(Name = "عصر العملة")]
    public string CurrencyEra { get; set; } = "SYP-new";

    [Display(Name = "الحالة")]
    public ArrearsLedgerStatus Status { get; set; } = ArrearsLedgerStatus.Pending;

    [Display(Name = "دفعة التسوية")]
    public Guid? SettledByBatchId { get; set; }

    /// <summary>
    /// Applies a catch-up payment. Maintains PaidAmount + ArrearsAmount == OriginalAmount.
    /// </summary>
    public bool ApplyPayment(decimal amount)
    {
        if (amount <= 0 || Status == ArrearsLedgerStatus.Voided)
            return false;

        if (amount > ArrearsAmount)
            return false;

        PaidAmount += amount;
        ArrearsAmount = OriginalAmount - PaidAmount;

        if (ArrearsAmount == 0)
            Status = ArrearsLedgerStatus.FullyPaid;
        else if (PaidAmount > 0)
            Status = ArrearsLedgerStatus.PartiallyPaid;
        else
            Status = ArrearsLedgerStatus.Pending;

        UpdatedAt = DateTime.UtcNow;
        return true;
    }

    /// <summary>
    /// PLAN-58 (P-5): the run was reversed while its funded batch had not left: nothing of these arrears was paid and nothing is owed under
    /// that run. The row is closed with the reversal as its reason.
    /// </summary>
    public bool VoidForReversal(string reason, DateTime at)
    {
        if (Status is ArrearsLedgerStatus.FullyPaid or ArrearsLedgerStatus.Voided)
            return false;
        Status = ArrearsLedgerStatus.Voided;
        var trimmed = reason.Trim();
        Reason = trimmed.Length <= 500 ? trimmed : trimmed[..500];
        UpdatedAt = at;
        return true;
    }

    /// <summary>
    /// Manual edit of amounts requires four-eyes: ApproverId must differ from ActorId.
    /// </summary>
    public bool TryManualUpdate(
        decimal originalAmount,
        decimal paidAmount,
        Guid actorId,
        Guid approverId,
        string? reason = null)
    {
        if (actorId == Guid.Empty || approverId == Guid.Empty || actorId == approverId || Status == ArrearsLedgerStatus.Voided)
            return false;

        if (originalAmount < 0 || paidAmount < 0 || paidAmount > originalAmount)
            return false;

        OriginalAmount = originalAmount;
        PaidAmount = paidAmount;
        ArrearsAmount = originalAmount - paidAmount;
        if (!string.IsNullOrWhiteSpace(reason))
            Reason = reason;

        Status = ArrearsAmount == 0
            ? ArrearsLedgerStatus.FullyPaid
            : PaidAmount > 0
                ? ArrearsLedgerStatus.PartiallyPaid
                : ArrearsLedgerStatus.Pending;

        UpdatedAt = DateTime.UtcNow;
        UpdatedBy = approverId;
        return true;
    }
}
