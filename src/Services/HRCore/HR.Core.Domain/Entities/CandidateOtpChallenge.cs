using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Notifications;

namespace HR.Core.Domain.Entities;

/// <summary>
/// HCM-030 (PLAN-30 H1, D-4): one-time code sent to a public candidate to open <c>/careers/my</c>. The plain code is never
/// stored: <see cref="CodeHmac"/> is HMAC-SHA256 over the challenge id and the code with <c>Recruitment:Portal:OtpHmacKey</c>
/// (a secret kept outside the database). At most one challenge of an application is active (unconsumed and unexpired): a
/// new request expires the previous one. Deleted with the citizen row (cascade) and by candidate anonymization.
/// </summary>
public class CandidateOtpChallenge : BaseEntity
{
    [Display(Name = "طلب البوابة")]
    public Guid CitizenApplicationId { get; set; }

    /// <summary>Lowercase hex HMAC-SHA256 of <c>{Id:D}:{code}</c>.</summary>
    [Required, StringLength(64)]
    [Display(Name = "بصمة الرمز")]
    [AuditRedact]
    public string CodeHmac { get; set; } = string.Empty;

    /// <summary>Email or Sms (stored as string): where the code was sent.</summary>
    [Display(Name = "القناة")]
    public NotificationChannel Channel { get; set; }

    [Display(Name = "تنتهي صلاحيته")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>Wrong codes entered against this challenge.</summary>
    [Display(Name = "المحاولات")]
    public int Attempts { get; set; }

    [Display(Name = "وقت الاستخدام")]
    public DateTime? ConsumedAt { get; set; }

    /// <summary>Keyed hash (HMAC with the OTP key, label <c>ip:</c>) of the requesting client address; never the address.</summary>
    [StringLength(64)]
    [Display(Name = "بصمة عنوان الطلب")]
    [AuditRedact]
    public string? RequestIp { get; set; }

    public bool IsActive(DateTime utcNow) => ConsumedAt is null && ExpiresAt > utcNow;
}
