using System.ComponentModel.DataAnnotations;
using HR.Core.Domain.Enums;
using HRManagement.SharedKernel;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Notifications;

namespace HR.Core.Domain.Entities;

/// <summary>
/// COM-030 (PLAN-30 F3, G-18): one planned candidate message on one channel (durable queue + history). The real address
/// is never stored here: it is read from the citizen row at send time; <see cref="RecipientMasked"/> is for display only.
/// <see cref="Subject"/>/<see cref="Body"/> are rendered when the message is planned (Arabic templates, no PII beyond the
/// reference number and dates). Unique per tenant on <see cref="IdempotencyKey"/> (<c>{eventId}:{channel}</c>).
/// </summary>
public class CandidateNotification : BaseEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    /// <summary>The <see cref="RecruitmentApplicationEvent"/> that planned the message (null for messages without one).</summary>
    [Display(Name = "الحدث")]
    public Guid? EventId { get; set; }

    /// <summary>One of <see cref="RecruitmentApplicationEventTypes"/>.</summary>
    [Required, StringLength(40)]
    [Display(Name = "نوع الحدث")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>Email or Sms (stored as string).</summary>
    [Display(Name = "القناة")]
    public NotificationChannel Channel { get; set; }

    /// <summary>One of <c>CandidateNotificationTemplates</c> codes (e.g. <c>APP_RECEIVED</c>).</summary>
    [Required, StringLength(40)]
    [Display(Name = "القالب")]
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>Masked address (<c>PiiMask.Email</c>/<c>PiiMask.Phone</c>), never the real one.</summary>
    [Required, StringLength(160)]
    [Display(Name = "المستلم")]
    [AuditRedact]
    public string RecipientMasked { get; set; } = string.Empty;

    [StringLength(200)]
    [Display(Name = "الموضوع")]
    [AuditRedact]
    public string? Subject { get; set; }

    [Required, StringLength(2000)]
    [Display(Name = "النص")]
    [AuditRedact]
    public string Body { get; set; } = string.Empty;

    [Required, StringLength(200)]
    [Display(Name = "مفتاح عدم التكرار")]
    public string IdempotencyKey { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public CandidateNotificationStatus Status { get; set; } = CandidateNotificationStatus.Pending;

    [Display(Name = "المحاولات")]
    public int Attempts { get; set; }

    [Display(Name = "المحاولة التالية")]
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Sanitized reason (<c>HTTP 503</c>, <c>SMTP 550</c>, <c>Timeout</c>, exception type) — never an address.</summary>
    [StringLength(500)]
    [Display(Name = "آخر خطأ")]
    public string? LastError { get; set; }

    [Display(Name = "وقت الإرسال")]
    public DateTime? SentAt { get; set; }

    [Display(Name = "تنتهي صلاحيته")]
    public DateTime? ExpiresAt { get; set; }

    [StringLength(200)]
    [Display(Name = "معرّف المزوّد")]
    public string? ProviderMessageId { get; set; }

    /// <summary>Key of the message of <paramref name="eventId"/> on <paramref name="channel"/>: <c>{eventId:D}:{channel}</c>.</summary>
    public static string IdempotencyKeyFor(Guid eventId, NotificationChannel channel) => $"{eventId:D}:{channel}";
}
