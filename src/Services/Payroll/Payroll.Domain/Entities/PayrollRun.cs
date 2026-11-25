using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;
using Payroll.Domain.Services;

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

    private PayrollRunStatus _status;

    /// <summary>
    /// PLAN-56 (G-PAY-06): the run's state. It is given once, when the run is constructed (a new draft, or a seeded run created in its
    /// state), and after that only the methods below change it, each through <see cref="PayrollRunTransitions"/>. No code assigns it.
    /// </summary>
    [Display(Name = "الحالة")]
    public PayrollRunStatus Status
    {
        get => _status;
        init => _status = value;
    }

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

    /// <summary>Every change of state passes here: the act must be legal from the present state (PLAN-56, G-PAY-06).</summary>
    private void Move(PayrollRunAct act) => _status = PayrollRunTransitions.EnsureTransition(act, _status);

    public void BeginCalculating()
    {
        var err = ValidateCanCalculate();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.BeginCalculating);
    }

    /// <summary>The calculator's own step: every partition is complete, and the totals are being checked.</summary>
    public void BeginValidating() => Move(PayrollRunAct.BeginValidating);

    /// <summary>
    /// The calculator's own step: a partition or the validation failed. The run stays «قيد الحساب», not frozen, until the calculation
    /// is resumed or restarted.
    /// </summary>
    public void MarkCalculationFailed() => Move(PayrollRunAct.MarkCalculationFailed);

    public void MarkCalculated(decimal totalNetPay)
    {
        Move(PayrollRunAct.MarkCalculated);
        TotalNetPay = totalNetPay;
    }

    /// <summary>
    /// The validation passed: the total is frozen with the lines. Only a run that is being calculated or was calculated can be
    /// validated; a run sent for review, approved, posted, reversed or cancelled is never brought back by a validation.
    /// </summary>
    public void MarkValidated(decimal totalNetPay)
    {
        Move(PayrollRunAct.MarkValidated);
        TotalNetPay = totalNetPay;
        ValidatedAtCalculate = true;
        ResultsFrozen = true;
    }

    public void MarkCancelled() => Move(PayrollRunAct.Cancel);

    public void SubmitForReview()
    {
        var err = ValidateCanSubmitForReview();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.SubmitForReview);
    }

    public void Approve(Guid approverId, string? role = null, string? notes = null)
    {
        var err = ValidateCanApprove();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.Approve);
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
        Move(PayrollRunAct.Reject);
        Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = Id,
            ApproverId = approverId,
            Decision = ApprovalDecision.Rejected,
            Notes = notes,
            DecidedAt = DateTime.UtcNow
        });
    }

    /// <summary>
    /// PLAN-56 (G-PAY-03): the posting keeps the total the validation froze. It writes no amount: what is posted is what was approved.
    /// </summary>
    public void Post()
    {
        var err = ValidateCanPost();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.Post);
    }

    public void Reverse()
    {
        var err = ValidateCanReverse();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.Reverse);
    }
}
