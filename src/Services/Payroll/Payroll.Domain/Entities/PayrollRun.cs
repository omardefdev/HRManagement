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

    private bool _resultsFrozen;
    private bool _validatedAtCalculate;

    /// <summary>
    /// PAY-003: lines locked after successful validate. PLAN-56: given at construction (a seeded run), and after that changed only by
    /// the validation (frozen) and by a rejection that reopens the run or a restart (unfrozen). Nothing freezes a run by hand.
    /// </summary>
    [Display(Name = "نتائج مُجمّدة")]
    public bool ResultsFrozen
    {
        get => _resultsFrozen;
        init => _resultsFrozen = value;
    }

    /// <summary>PAY-003: validate phase completed successfully. PLAN-56: set by the validation alone, like <see cref="ResultsFrozen"/>.</summary>
    [Display(Name = "تم التحقق")]
    public bool ValidatedAtCalculate
    {
        get => _validatedAtCalculate;
        init => _validatedAtCalculate = value;
    }

    /// <summary>
    /// PLAN-56 (G-PAY-04): the version of the run's results. 1 for the first calculation; a rejection that reopens the run adds one,
    /// and keeps what the rejected version was in a <see cref="PayrollRunRevision"/>.
    /// </summary>
    [Display(Name = "رقم النسخة")]
    public int Revision { get; private set; } = 1;

    /// <summary>PLAN-56 (G-PAY-06): who posted the run. The decider of its reversal is never this person.</summary>
    [Display(Name = "المُرحِّل")]
    public Guid? PostedBy { get; private set; }

    [Display(Name = "تاريخ الترحيل")]
    public DateTime? PostedAt { get; private set; }

    /// <summary>
    /// Who posted the run, as far as the run knows. A run posted before PLAN-56 has no <see cref="PostedBy"/>; the posting was the
    /// last write of such a run (nothing else writes a posted run), so its last writer stands for its poster. Null when neither
    /// is known (a seeded run): the rule «the decider is not the poster» then has no one to name.
    /// </summary>
    public Guid? KnownPoster => PostedBy ?? (_status == PayrollRunStatus.Posted ? UpdatedBy : null);

    /// <summary>PLAN-56 (D-PAY-18): who cancelled the run, when and why. The reason is always given.</summary>
    [Display(Name = "ألغاه")]
    public Guid? CancelledBy { get; private set; }

    [Display(Name = "تاريخ الإلغاء")]
    public DateTime? CancelledAt { get; private set; }

    [MaxLength(ReasonMaxLength)]
    [Display(Name = "سبب الإلغاء")]
    public string? CancellationReason { get; private set; }

    public const int ReasonMaxLength = 2000;

    public const string RejectionNoteRequired = "ملاحظة الرفض مطلوبة: اذكر ما يجب تصحيحه قبل إعادة الإرسال.";
    public const string CancellationReasonRequired = "سبب الإلغاء مطلوب.";
    public const string RestartNeedsUnfrozenResults = "لا يمكن إعادة بدء الحساب بعد تجميد النتائج؛ ألغِ التشغيل أو استخدم الرفض من المراجعة.";

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
        _validatedAtCalculate = true;
        _resultsFrozen = true;
    }

    /// <summary>
    /// PLAN-56 (G-PAY-04, D-PAY-18): the run is abandoned before its posting, by a named person, for a stated reason. Its period is
    /// free for a new run; the caller releases its inputs in the same transaction. A row of the run's decisions keeps the reason.
    /// </summary>
    public PayrollRunApproval Cancel(Guid cancelledBy, DateTime cancelledAt, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException(CancellationReasonRequired);
        Move(PayrollRunAct.Cancel);
        CancelledBy = cancelledBy;
        CancelledAt = cancelledAt;
        CancellationReason = Trimmed(reason);
        var decision = new PayrollRunApproval
        {
            PayrollRunId = Id,
            TenantId = TenantId,
            ApproverId = cancelledBy,
            Decision = ApprovalDecision.Cancelled,
            Notes = CancellationReason,
            DecidedAt = cancelledAt,
            Revision = Revision,
        };
        Approvals.Add(decision);
        return decision;
    }

    /// <summary>
    /// PLAN-56 (G-PAY-07): a calculation that failed or was paused is given up, and the run goes back to a draft for a calculation
    /// with a new snapshot. Only while the results are not frozen; the caller deletes the lines and releases the inputs with it.
    /// </summary>
    public void ResetForRestart()
    {
        if (ResultsFrozen)
            throw new InvalidOperationException(RestartNeedsUnfrozenResults);
        Move(PayrollRunAct.RestartCalculation);
        TotalNetPay = 0m;
        _validatedAtCalculate = false;
    }

    private static string Trimmed(string text)
    {
        var trimmed = text.Trim();
        return trimmed.Length <= ReasonMaxLength ? trimmed : trimmed[..ReasonMaxLength];
    }

    public void SubmitForReview()
    {
        var err = ValidateCanSubmitForReview();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.SubmitForReview);
    }

    public const string SameApproverTwice = "سبق أن اعتمدتَ هذا التشغيل: الاعتماد التالي لمعتمد آخر.";

    /// <summary>
    /// PLAN-58 (D-PAY-5): the approvals given to the present version of the run, one per approver. A rejection reopens the run with
    /// its version one more, so an approval of an earlier version does not count here.
    /// </summary>
    [NotMapped]
    public IReadOnlyList<PayrollRunApproval> ApprovalsOfThisVersion =>
        Approvals.Where(a => a.Decision == ApprovalDecision.Approved && a.Revision == Revision).ToList();

    /// <summary>PLAN-58: how many different approvers approved the present version («اعتماد 1 من 2» shows it).</summary>
    [NotMapped]
    public int ApprovalsGiven =>
        ApprovalsOfThisVersion.Where(a => a.ApproverId.HasValue).Select(a => a.ApproverId!.Value).Distinct().Count();

    /// <summary>
    /// PLAN-58 (D-PAY-5, G-PAY-09): one approval of the present version. The run stays «قيد المراجعة» until
    /// <paramref name="requiredApprovals"/> different approvers approved it (two above the threshold), and only the approval that
    /// completes the count moves it to «معتمد»: the first of two is not a transition. The same approver never counts twice.
    /// Returns true when the run moved to «معتمد».
    /// </summary>
    public bool Approve(Guid approverId, int requiredApprovals, string? role = null, string? notes = null, DateTime? decidedAt = null)
    {
        var err = ValidateCanApprove();
        if (err != null) throw new InvalidOperationException(err);
        if (ApprovalsOfThisVersion.Any(a => a.ApproverId == approverId))
            throw new InvalidOperationException(SameApproverTwice);
        Approvals.Add(new PayrollRunApproval
        {
            PayrollRunId = Id,
            TenantId = TenantId,
            ApproverId = approverId,
            ApproverRole = role,
            Decision = ApprovalDecision.Approved,
            Notes = notes,
            DecidedAt = decidedAt ?? DateTime.UtcNow,
            Revision = Revision,
        });
        // The run itself is written by every approval, so that two approvals at once cannot both count without one of them losing on
        // the run's row version (the second is refused, not lost).
        UpdatedAt = decidedAt ?? DateTime.UtcNow;
        if (ApprovalsGiven < Math.Max(1, requiredApprovals))
            return false;
        Move(PayrollRunAct.Approve);
        return true;
    }

    /// <summary>
    /// PLAN-56 (G-PAY-04): the reviewer sends the run back, and the run reopens: a draft again, its results neither frozen nor
    /// validated, its version one more. The note is mandatory: it says what to correct. The caller keeps the rejected version in a
    /// <see cref="PayrollRunRevision"/>, deletes its lines and releases its inputs in the same transaction. A rejection used to leave
    /// the run «محسوب» with frozen results, which could be neither recalculated nor resubmitted.
    /// </summary>
    public PayrollRunApproval ReopenAfterRejection(Guid rejectedBy, DateTime rejectedAt, string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            throw new InvalidOperationException(RejectionNoteRequired);
        Move(PayrollRunAct.Reject);
        _resultsFrozen = false;
        _validatedAtCalculate = false;
        TotalNetPay = 0m;
        Revision++;
        var decision = new PayrollRunApproval
        {
            PayrollRunId = Id,
            TenantId = TenantId,
            ApproverId = rejectedBy,
            Decision = ApprovalDecision.Rejected,
            Notes = Trimmed(notes),
            DecidedAt = rejectedAt,
            Revision = Revision - 1,
        };
        Approvals.Add(decision);
        return decision;
    }

    /// <summary>
    /// PLAN-56 (G-PAY-03): the posting keeps the total the validation froze. It writes no amount: what is posted is what was approved.
    /// </summary>
    public void Post(Guid postedBy, DateTime postedAt)
    {
        var err = ValidateCanPost();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.Post);
        PostedBy = postedBy;
        PostedAt = postedAt;
    }

    public void Reverse()
    {
        var err = ValidateCanReverse();
        if (err != null) throw new InvalidOperationException(err);
        Move(PayrollRunAct.Reverse);
    }
}
