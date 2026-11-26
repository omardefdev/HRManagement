using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "دفعة الصرف")]
public class PaymentBatch : BaseEntity
{
    [Required(ErrorMessage = "تشغيل الرواتب مطلوب")]
    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    [Display(Name = "مرجع التحويل البنكي")]
    public string? BankTransferReference { get; set; }

    [Display(Name = "المبلغ الإجمالي")]
    public decimal TotalAmount { get; set; }

    [Display(Name = "تاريخ المعالجة")]
    public DateTime? ProcessedAt { get; set; }

    [Display(Name = "تاريخ الإرسال")]
    public DateTime? SubmittedAt { get; set; }

    [Display(Name = "تاريخ الإشعار")]
    public DateTime? AckedAt { get; set; }

    [Display(Name = "تاريخ التسوية")]
    public DateTime? SettledAt { get; set; }

    /// <summary>Reference returned by the channel on settlement; the audit trail for "money moved".</summary>
    [MaxLength(128)]
    [Display(Name = "مرجع التسوية")]
    public string? SettlementReference { get; set; }

    [MaxLength(500)]
    [Display(Name = "سبب الرفض")]
    public string? RejectionReason { get; set; }

    [Display(Name = "حالة الدفعة")]
    public PaymentBatchStatus Status { get; set; } = PaymentBatchStatus.Pending;

    [Display(Name = "نوع الدفعة")]
    public PaymentBatchKind BatchKind { get; set; } = PaymentBatchKind.Disbursement;

    public Guid? ReversesBatchId { get; set; }

    /// <summary>PAY-004: funding decision that authorized this batch.</summary>
    public Guid? FundingDecisionId { get; set; }

    /// <summary>PAY-004: true when FundingRatio &lt; 1 (partial treasury funding).</summary>
    public bool IsPartial { get; set; }

    /// <summary>PAY-004: amount actually disbursed (may be less than TotalAmount).</summary>
    public decimal DisbursedAmount { get; set; }

    /// <summary>
    /// PLAN-58 (§1 rule 2): the part of the run's net that the funding decision left as arrears (zero without a decision). The batch's
    /// orders that are not cancelled plus this amount are the run's payable net at every transition of the batch
    /// (<c>PaymentBatchLedger</c>). A snapshot taken by the decision: a later adjustment of an arrears row does not move it.
    /// </summary>
    [Display(Name = "المتأخرات")]
    public decimal ArrearsAmount { get; set; }

    /// <summary>PLAN-58 (P-5): where the batch came from. <see cref="PaymentBatchSource.Legacy"/> for a batch older than this plan.</summary>
    [Display(Name = "مصدر الدفعة")]
    public PaymentBatchSource Source { get; set; } = PaymentBatchSource.Legacy;

    /// <summary>
    /// PLAN-58 (P-5): the one named way a seed makes a batch at construction (the demo and acceptance seeds). Product code makes a batch
    /// only through <c>PaymentBatchIssuer</c>; <c>Pay058SinglePaymentPathArchitectureTests</c> allows this call in the seeder alone.
    /// </summary>
    public static PaymentBatch Seeded(
        Guid id,
        Guid tenantId,
        Guid payrollRunId,
        string bankTransferReference,
        decimal totalAmount,
        decimal disbursedAmount,
        DateTime? processedAt,
        PaymentBatchStatus status,
        DateTime createdAt) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            PayrollRunId = payrollRunId,
            BankTransferReference = bankTransferReference,
            TotalAmount = totalAmount,
            DisbursedAmount = disbursedAmount,
            ProcessedAt = processedAt,
            Status = status,
            BatchKind = PaymentBatchKind.Disbursement,
            Source = PaymentBatchSource.Seeded,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };

    public PayrollRun PayrollRun { get; set; } = null!;
    public FundingDecision? FundingDecision { get; set; }
}
