using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using Payroll.Domain.Enums;

namespace Payroll.Domain.Entities;

/// <summary>
/// PLAN-58 (§3, §7, G-PAY-09): «four eyes» as two acts by two people from their own sessions: one person asks, with a reason (and an amount
/// where the act has one), another person who holds the kind's policy decides. Both actors come from their tokens, never from a form or a
/// body. The act itself (a hold's release, a critical alert's resolution, an arrears adjustment, a bank file's reissue, a batch's settlement
/// or rejection) happens inside the decision, in the decision's transaction. One pending request per kind and target; a request not
/// decided before <see cref="ExpiresAt"/> expires with no effect.
/// </summary>
[Display(Name = "طلب أربع عيون")]
public class PayrollDualControlRequest : BaseEntity
{
    public const int ReasonMaxLength = 2000;
    public const int PayloadMaxLength = 4000;

    public const string AlreadyDecided = "قُرّر هذا الطلب من قبل أو انتهت مهلته؛ لا يُقرَّر مرة ثانية.";

    [Display(Name = "نوع الطلب")]
    public PayrollDualControlKind Kind { get; set; }

    /// <summary>The record the act changes: the hold, the alert, the arrears row, the bank file, or the batch.</summary>
    [Display(Name = "السجل المعني")]
    public Guid TargetId { get; set; }

    /// <summary>What was asked: the reason, and the amounts and references the kind needs (<c>PayrollDualControlPayload</c>). No account number.</summary>
    [MaxLength(PayloadMaxLength)]
    public string PayloadJson { get; set; } = "{}";

    [Display(Name = "الطالب")]
    public Guid RequestedBy { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; }

    [Display(Name = "تنتهي مهلته")]
    public DateTime ExpiresAt { get; set; }

    [Display(Name = "الحالة")]
    public PayrollDualControlStatus Status { get; private set; } = PayrollDualControlStatus.Pending;

    [Display(Name = "المقرِّر")]
    public Guid? DecidedBy { get; private set; }

    [Display(Name = "تاريخ القرار")]
    public DateTime? DecidedAt { get; private set; }

    [MaxLength(ReasonMaxLength)]
    [Display(Name = "ملاحظات القرار")]
    public string? DecisionNotes { get; private set; }

    public bool IsExpired(DateTime now) => Status == PayrollDualControlStatus.Pending && now >= ExpiresAt;

    /// <summary>The decision of another person. The caller has checked that he is not the requester and holds the kind's policy.</summary>
    public void Decide(bool approve, Guid decidedBy, DateTime decidedAt, string? notes)
    {
        if (Status != PayrollDualControlStatus.Pending)
            throw new InvalidOperationException(AlreadyDecided);
        Status = approve ? PayrollDualControlStatus.Approved : PayrollDualControlStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = decidedAt;
        DecisionNotes = Trimmed(notes);
    }

    /// <summary>The request was not decided in time: it ends with no effect.</summary>
    public void Expire(DateTime at)
    {
        if (Status != PayrollDualControlStatus.Pending)
            return;
        Status = PayrollDualControlStatus.Expired;
        DecidedAt = at;
    }

    private static string? Trimmed(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var trimmed = text.Trim();
        return trimmed.Length <= ReasonMaxLength ? trimmed : trimmed[..ReasonMaxLength];
    }
}
