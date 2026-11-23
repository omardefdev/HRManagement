using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-53 (D-53-4): a change of a salary scale or a grade (create, update or delete) as a request. Whoever holds
/// <c>Payroll.MasterData</c> submits it with a reason and a legal reference; a different holder of <c>Payroll.Approve</c> decides it, and
/// the change is applied in the approval's own save. <see cref="ProposedJson"/> and <see cref="CurrentJson"/> hold the allowlisted
/// fields of the record, never a bound entity.
/// </summary>
[Display(Name = "طلب تغيير بيانات مرجعية")]
public class PayrollReferenceChangeRequest : BaseEntity
{
    public const int LabelMaxLength = 260;

    [Display(Name = "نوع السجل")]
    public PayrollReferenceEntityType EntityType { get; set; }

    /// <summary>The record changed or deleted; empty for a creation until it is applied.</summary>
    [Display(Name = "السجل")]
    public Guid? EntityId { get; set; }

    /// <summary>The record's code and name when the request was made, so a list reads without the JSON.</summary>
    [MaxLength(LabelMaxLength)]
    [Display(Name = "السجل")]
    public string? EntityLabel { get; set; }

    [Display(Name = "العملية")]
    public PayrollReferenceOperation Operation { get; set; }

    [Display(Name = "القيم المقترحة")]
    public string? ProposedJson { get; set; }

    [Display(Name = "القيم الحالية")]
    public string? CurrentJson { get; set; }

    [Required, MaxLength(PayProfileChangeRequest.ReasonMaxLength)]
    [Display(Name = "السبب")]
    public string Reason { get; set; } = string.Empty;

    [Required, MaxLength(PayProfileChangeRequest.LegalReferenceMaxLength)]
    [Display(Name = "المرجع القانوني")]
    public string LegalReference { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public PayrollReferenceChangeState State { get; set; } = PayrollReferenceChangeState.PendingApproval;

    [Display(Name = "الطالب")]
    public Guid RequestedBy { get; set; }

    [MaxLength(PayProfileChangeRequest.NameMaxLength)]
    [Display(Name = "اسم الطالب")]
    public string? RequestedByName { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; }

    [Display(Name = "صاحب القرار")]
    public Guid? DecidedBy { get; set; }

    [MaxLength(PayProfileChangeRequest.NameMaxLength)]
    [Display(Name = "اسم صاحب القرار")]
    public string? DecidedByName { get; set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime? DecidedAt { get; set; }

    [MaxLength(PayProfileChangeRequest.NoteMaxLength)]
    [Display(Name = "ملاحظة القرار")]
    public string? DecisionNote { get; set; }

    [Display(Name = "الملغي")]
    public Guid? CancelledBy { get; set; }

    [MaxLength(PayProfileChangeRequest.NameMaxLength)]
    [Display(Name = "اسم الملغي")]
    public string? CancelledByName { get; set; }

    [Display(Name = "تاريخ الإلغاء")]
    public DateTime? CancelledAt { get; set; }

    [MaxLength(PayProfileChangeRequest.NoteMaxLength)]
    [Display(Name = "سبب الإلغاء")]
    public string? CancelReason { get; set; }

    [Display(Name = "تاريخ التطبيق")]
    public DateTime? AppliedAt { get; set; }

    public bool IsOpen => State == PayrollReferenceChangeState.PendingApproval;
}
