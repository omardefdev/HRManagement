using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-53 (D-IAM-2, D-PAY-6): a change of an employee's base salary or grade, as a transaction. A payroll processor requests it and a
/// different payroll approver decides it; it reaches the pay profile only through <c>IPayProfileChangeApplier</c>, on or after
/// <see cref="EffectiveFrom"/>. An automatic source (a contract, a promotion, a transfer) is recorded in the same entity as
/// <see cref="PayProfileChangeState.Applied"/> at once. The contract is in <c>docs/PAYROLL_SALARY_CHANGE.md</c>: later plans add to it.
/// </summary>
[Display(Name = "طلب تغيير الراتب")]
public class PayProfileChangeRequest : BaseEntity
{
    public const int ReasonMaxLength = 500;
    public const int LegalReferenceMaxLength = 200;
    public const int SourceReferenceMaxLength = 100;
    public const int NameMaxLength = 200;
    public const int NoteMaxLength = 500;

    /// <summary>The name recorded for a change no person made (an automatic source, the activation job).</summary>
    public const string SystemActorName = "النظام";

    [Required]
    [Display(Name = "الموظف")]
    public Guid EmployeeId { get; set; }

    /// <summary>The unit of the active pay profile when the request was made: the key of the branch scope.</summary>
    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "نوع التغيير")]
    public PayProfileChangeKind ChangeKind { get; set; }

    [Display(Name = "المصدر")]
    public PayProfileChangeSource Source { get; set; }

    /// <summary>The contract's id or the decision's number, for an automatic source; a caller's own reference otherwise.</summary>
    [MaxLength(SourceReferenceMaxLength)]
    [Display(Name = "مرجع المصدر")]
    public string? SourceReference { get; set; }

    [Display(Name = "ملف الأجر الحالي")]
    public Guid? CurrentProfileId { get; set; }

    [AuditRedact]
    [Display(Name = "الراتب الحالي")]
    public decimal? CurrentBaseSalary { get; set; }

    [Display(Name = "الدرجة الحالية")]
    public Guid? CurrentPayGradeId { get; set; }

    [AuditRedact]
    [Display(Name = "الراتب المقترح")]
    public decimal? ProposedBaseSalary { get; set; }

    [Display(Name = "الدرجة المقترحة")]
    public Guid? ProposedPayGradeId { get; set; }

    /// <summary>D-53-1: a calendar date, the first day of a month for a manual or API request.</summary>
    [Display(Name = "يسري من")]
    public DateOnly EffectiveFrom { get; set; }

    [Required, MaxLength(ReasonMaxLength)]
    [Display(Name = "السبب")]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(LegalReferenceMaxLength)]
    [Display(Name = "المرجع القانوني")]
    public string? LegalReference { get; set; }

    [Display(Name = "الحالة")]
    public PayProfileChangeState State { get; set; } = PayProfileChangeState.PendingApproval;

    [Display(Name = "الطالب")]
    public Guid RequestedBy { get; set; }

    [MaxLength(NameMaxLength)]
    [Display(Name = "اسم الطالب")]
    public string? RequestedByName { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; }

    /// <summary>The approver or the rejecter. Empty for an automatic source.</summary>
    [Display(Name = "صاحب القرار")]
    public Guid? DecidedBy { get; set; }

    [MaxLength(NameMaxLength)]
    [Display(Name = "اسم صاحب القرار")]
    public string? DecidedByName { get; set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime? DecidedAt { get; set; }

    [MaxLength(NoteMaxLength)]
    [Display(Name = "ملاحظة القرار")]
    public string? DecisionNote { get; set; }

    /// <summary>Who cancelled an open request: its requester, or an approver. Kept apart from the decision, so a scheduled request keeps its approver.</summary>
    [Display(Name = "الملغي")]
    public Guid? CancelledBy { get; set; }

    [MaxLength(NameMaxLength)]
    [Display(Name = "اسم الملغي")]
    public string? CancelledByName { get; set; }

    [Display(Name = "تاريخ الإلغاء")]
    public DateTime? CancelledAt { get; set; }

    [MaxLength(NoteMaxLength)]
    [Display(Name = "سبب الإلغاء")]
    public string? CancelReason { get; set; }

    [Display(Name = "تاريخ التطبيق")]
    public DateTime? AppliedAt { get; set; }

    [Display(Name = "ملف الأجر الناتج")]
    public Guid? AppliedProfileId { get; set; }

    /// <summary>Why the last activation attempt of a scheduled request was refused (a frozen month, the grade's bounds). Arabic, for the approver.</summary>
    [MaxLength(NoteMaxLength)]
    [Display(Name = "سبب تعذّر التطبيق")]
    public string? LastApplyError { get; set; }

    /// <summary>The automatic change of the same employee that replaced this open request.</summary>
    [Display(Name = "الطلب الذي أبطله")]
    public Guid? SupersededByRequestId { get; set; }

    public bool IsOpen => State is PayProfileChangeState.PendingApproval or PayProfileChangeState.Scheduled;

    /// <summary>The automatic sources: recorded as applied at once, with no decider.</summary>
    public static bool IsAutomatic(PayProfileChangeSource source) =>
        source is not (PayProfileChangeSource.Manual or PayProfileChangeSource.Api);
}
