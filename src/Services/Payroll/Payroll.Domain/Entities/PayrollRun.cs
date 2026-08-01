using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

[Display(Name = "تشغيل الرواتب")]
public class PayrollRun : AuditableEntity
{
    [Required(ErrorMessage = "بداية الفترة مطلوبة")]
    [Display(Name = "بداية الفترة")]
    public DateTime PeriodStart { get; set; }

    [Required(ErrorMessage = "نهاية الفترة مطلوبة")]
    [Display(Name = "نهاية الفترة")]
    public DateTime PeriodEnd { get; set; }

    [Required(ErrorMessage = "تاريخ التشغيل مطلوب")]
    [Display(Name = "تاريخ التشغيل")]
    public DateTime RunDate { get; set; }

    [Display(Name = "الحالة")]
    public PayrollRunStatus Status { get; set; }

    [Display(Name = "نوع التشغيل")]
    public PayrollRunType RunType { get; set; } = PayrollRunType.Regular;

    [Display(Name = "التشغيل المرجعي")]
    public Guid? SourceRunId { get; set; }

    /// <summary>PAY-002: pinned legal configuration version used for calculation.</summary>
    [Display(Name = "نسخة الإعدادات القانونية")]
    public Guid? PayrollConfigurationId { get; set; }

    /// <summary>PAY-002: content hash captured at calculate time.</summary>
    [MaxLength(64)]
    [Display(Name = "بصمة الإعدادات")]
    public string? ConfigContentHash { get; set; }

    [Display(Name = "إجمالي صافي الرواتب")]
    public decimal TotalNetPay { get; set; }

    /// <summary>PAY-003: lines locked after successful validate.</summary>
    [Display(Name = "نتائج مُجمّدة")]
    public bool ResultsFrozen { get; set; }

    /// <summary>PAY-003: validate phase completed successfully.</summary>
    [Display(Name = "تم التحقق")]
    public bool ValidatedAtCalculate { get; set; }

    [ForeignKey(nameof(PayrollConfigurationId))]
    public PayrollConfiguration? PayrollConfiguration { get; set; }

    public ICollection<SalaryLine> SalaryLines { get; set; } = new List<SalaryLine>();
    public ICollection<PayrollAuditRecord> AuditRecords { get; set; } = new List<PayrollAuditRecord>();
    public ICollection<PaymentBatch> PaymentBatches { get; set; } = new List<PaymentBatch>();
    public ICollection<PayrollRunApproval> Approvals { get; set; } = new List<PayrollRunApproval>();

    public string? ValidateCanCalculate() =>
        Status is PayrollRunStatus.Draft or PayrollRunStatus.Calculated or PayrollRunStatus.Calculating
            ? ResultsFrozen
                ? "لا يمكن إعادة الحساب بعد تجميد النتائج؛ استخدم تصحيحاً أو عكساً."
                : null
            : "لا يمكن حساب البنود إلا في حالة مسودة أو محسوب أو قيد الحساب.";

    public string? ValidateCanSubmitForReview() =>
        Status == PayrollRunStatus.Calculated && SalaryLines.Count > 0 && ValidatedAtCalculate
            ? null
            : "يجب إكمال الحساب والتحقق من الإجماليات قبل الإرسال للمراجعة.";

    public string? ValidateCanApprove() =>
        Status == PayrollRunStatus.UnderReview
            ? null
            : "لا يمكن الاعتماد إلا للتشغيلات قيد المراجعة.";

    public string? ValidateCanModifyLines() =>
        ResultsFrozen
            ? "النتائج مُجمّدة بعد التحقق — لا يمكن تعديل البنود."
            : Status is PayrollRunStatus.Draft or PayrollRunStatus.Calculated or PayrollRunStatus.Calculating
                ? null
                : "لا يمكن تعديل بنود الرواتب بعد الإرسال للمراجعة.";

    public string? ValidateCanPost() =>
        Status != PayrollRunStatus.Approved
            ? "يجب اعتماد التشغيل قبل الترحيل."
            : !ValidatedAtCalculate
                ? "يجب نجاح مرحلة التحقق قبل الترحيل."
                : ResultsFrozen
                    ? null
                    : "يجب تجميد النتائج بعد التحقق قبل الترحيل.";

    public string? ValidateCanReverse() =>
        Status == PayrollRunStatus.Posted
            ? null
            : "لا يمكن عكس إلا تشغيلاً مرحّلاً.";

    public void BeginCalculating()
    {
        var err = ValidateCanCalculate();
        if (err != null) throw new InvalidOperationException(err);
        Status = PayrollRunStatus.Calculating;
    }

    public void MarkCalculated(decimal totalNetPay)
    {
        if (Status is not (PayrollRunStatus.Draft or PayrollRunStatus.Calculated or PayrollRunStatus.Calculating or PayrollRunStatus.Validating))
            throw new InvalidOperationException("لا يمكن إنهاء الحساب في الحالة الحالية.");
        Status = PayrollRunStatus.Calculated;
        TotalNetPay = totalNetPay;
    }

    public void MarkValidated(decimal totalNetPay)
    {
        Status = PayrollRunStatus.Calculated;
        TotalNetPay = totalNetPay;
        ValidatedAtCalculate = true;
        ResultsFrozen = true;
    }

    public void MarkCancelled()
    {
        if (Status is PayrollRunStatus.Posted or PayrollRunStatus.Reversed)
            throw new InvalidOperationException("لا يمكن إلغاء تشغيل مرحّل أو معكوس.");
        Status = PayrollRunStatus.Cancelled;
    }

    public void SubmitForReview()
    {
        var err = ValidateCanSubmitForReview();
        if (err != null) throw new InvalidOperationException(err);
        Status = PayrollRunStatus.UnderReview;
    }

    public void Approve(Guid approverId, string? role = null, string? notes = null)
    {
        var err = ValidateCanApprove();
        if (err != null) throw new InvalidOperationException(err);
        Status = PayrollRunStatus.Approved;
        Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = Id,
            ApproverId = approverId,
            ApproverRole = role,
            Decision = ApprovalDecision.Approved,
            Notes = notes,
            DecidedAt = DateTime.UtcNow
        });
    }

    public void Reject(Guid approverId, string? notes = null)
    {
        var err = ValidateCanApprove();
        if (err != null) throw new InvalidOperationException(err);
        Status = PayrollRunStatus.Calculated;
        Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = Id,
            ApproverId = approverId,
            Decision = ApprovalDecision.Rejected,
            Notes = notes,
            DecidedAt = DateTime.UtcNow
        });
    }

    public void Post(decimal totalNetPay)
    {
        var err = ValidateCanPost();
        if (err != null) throw new InvalidOperationException(err);
        Status = PayrollRunStatus.Posted;
        TotalNetPay = totalNetPay;
    }

    public void Reverse()
    {
        var err = ValidateCanReverse();
        if (err != null) throw new InvalidOperationException(err);
        Status = PayrollRunStatus.Reversed;
    }
}
