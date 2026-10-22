using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>
/// HCM-030 (B1): persisted form of the internal <c>RecruitmentApplicationStatusChanged</c> event. Append-only and
/// free of personal data; written in the same save as the change by <c>HRCoreDbContext</c>. It feeds the candidate
/// timeline and, from session 2, the candidate notifications (<see cref="NotificationsPlannedAt"/>).
/// </summary>
public class RecruitmentApplicationEvent : BaseEntity
{
    [Display(Name = "طلب التوظيف")]
    public Guid RecruitmentApplicationId { get; set; }

    /// <summary>One of <see cref="RecruitmentApplicationEventTypes"/>.</summary>
    [Required, StringLength(40)]
    [Display(Name = "نوع الحدث")]
    public string EventType { get; set; } = string.Empty;

    /// <summary>
    /// Previous status of the entity that triggered the event (enum as int): <c>RecruitmentApplicationStatus</c> for
    /// Received/StatusChanged, <c>InterviewSlotStatus</c> for Interview*, <c>OfferStatus</c> for Offer*,
    /// <c>RecruitmentAppealStatus</c> for Appeal*. Null when the entity was added.
    /// </summary>
    [Display(Name = "من حالة")]
    public int? FromStatus { get; set; }

    [Display(Name = "إلى حالة")]
    public int? ToStatus { get; set; }

    /// <summary>The interview slot, offer letter or appeal behind the event (null for application events).</summary>
    [Display(Name = "الكيان المرتبط")]
    public Guid? RelatedEntityId { get; set; }

    [Display(Name = "وقت الحدث")]
    public DateTime OccurredAt { get; set; }

    /// <summary>PLAN-30 S2: set by the notification dispatcher once the candidate messages are planned.</summary>
    [Display(Name = "تاريخ تخطيط الإشعارات")]
    public DateTime? NotificationsPlannedAt { get; set; }
}

/// <summary>HCM-030 (B1): values of <see cref="RecruitmentApplicationEvent.EventType"/> (session 2 contract).</summary>
public static class RecruitmentApplicationEventTypes
{
    public const string Received = "Received";
    public const string StatusChanged = "StatusChanged";
    public const string InterviewScheduled = "InterviewScheduled";
    public const string InterviewRescheduled = "InterviewRescheduled";
    public const string InterviewCancelled = "InterviewCancelled";
    public const string OfferIssued = "OfferIssued";
    public const string OfferAccepted = "OfferAccepted";
    public const string OfferDeclined = "OfferDeclined";
    public const string OfferExpired = "OfferExpired";
    public const string OfferRevoked = "OfferRevoked";
    public const string AppealFiled = "AppealFiled";
    public const string AppealDecided = "AppealDecided";

    public static readonly IReadOnlyList<string> All =
    [
        Received, StatusChanged, InterviewScheduled, InterviewRescheduled, InterviewCancelled,
        OfferIssued, OfferAccepted, OfferDeclined, OfferExpired, OfferRevoked, AppealFiled, AppealDecided
    ];
}
