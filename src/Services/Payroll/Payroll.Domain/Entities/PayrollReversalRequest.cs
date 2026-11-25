using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-56 (G-PAY-06, D-PAY-16): a reversal of a posted run is a request with a reason that another person decides. The decider is
/// neither the requester nor the person who posted the run. One request can wait on a run at a time; a decision is final.
/// </summary>
[Display(Name = "طلب عكس تشغيل الرواتب")]
public class PayrollReversalRequest : AuditableEntity
{
    public const int ReasonMaxLength = 2000;

    [Display(Name = "تشغيل الرواتب")]
    public Guid PayrollRunId { get; set; }

    [Display(Name = "طالب العكس")]
    public Guid RequestedBy { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; }

    [Required(ErrorMessage = "سبب العكس مطلوب")]
    [MaxLength(ReasonMaxLength)]
    [Display(Name = "سبب العكس")]
    public string Reason { get; set; } = "";

    [Display(Name = "الحالة")]
    public PayrollReversalRequestStatus Status { get; private set; } = PayrollReversalRequestStatus.Pending;

    [Display(Name = "قرّر فيه")]
    public Guid? DecidedBy { get; private set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime? DecidedAt { get; private set; }

    [MaxLength(ReasonMaxLength)]
    [Display(Name = "ملاحظة القرار")]
    public string? DecisionNotes { get; private set; }

    public PayrollRun PayrollRun { get; set; } = null!;

    public const string AlreadyDecided = "سبق القرار في طلب العكس هذا.";

    /// <summary>The decision, once. A request that was decided is never decided again.</summary>
    public void Decide(bool approve, Guid decidedBy, DateTime decidedAt, string? notes)
    {
        if (Status != PayrollReversalRequestStatus.Pending)
            throw new InvalidOperationException(AlreadyDecided);
        Status = approve ? PayrollReversalRequestStatus.Approved : PayrollReversalRequestStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
        DecisionNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }
}
