using System.ComponentModel.DataAnnotations;

namespace HR.Core.Domain.Enums;

/// <summary>COM-030 (PLAN-30 F3): delivery state of a <c>CandidateNotification</c> (stored as string).</summary>
public enum CandidateNotificationStatus
{
    [Display(Name = "بانتظار الإرسال")]
    Pending = 0,
    [Display(Name = "أُرسل")]
    Sent = 1,
    [Display(Name = "فشل")]
    Failed = 2,
    [Display(Name = "انتهت صلاحيته")]
    Expired = 3,
    [Display(Name = "أُوقف")]
    Suppressed = 4
}
