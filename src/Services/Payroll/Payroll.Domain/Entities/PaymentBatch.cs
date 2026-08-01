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

    public PayrollRun PayrollRun { get; set; } = null!;
    public FundingDecision? FundingDecision { get; set; }
}
