using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Dev.Analytics.Domain.Entities;

[Display(Name = "حدث تحليلي")]
public class AnalyticsEvent : BaseEntity
{
    [Display(Name = "اسم الحدث")]
    [Required(ErrorMessage = "اسم الحدث مطلوب")]
    public string EventName { get; set; } = string.Empty;

    [Display(Name = "البيانات")]
    public string? Payload { get; set; }

    [Display(Name = "وقت الحدوث")]
    public DateTime OccurredAt { get; set; }

    /// <summary>FUN-006: org grain for branch RLS / projections.</summary>
    public Guid? OrgUnitId { get; set; }

    [MaxLength(128)]
    public string? IdempotencyKey { get; set; }
}
