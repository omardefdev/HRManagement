using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

/// <summary>TRN-002: collective grievance pathway (not routed to individual HCM-006).</summary>
[Display(Name = "تظلم جماعي")]
public class CollectiveGrievance : AuditableEntity
{
    [Required]
    [MaxLength(200)]
    [Display(Name = "الموضوع")]
    public string Subject { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    [Display(Name = "مقدم من")]
    public string FiledBy { get; set; } = string.Empty;

    [Display(Name = "المهلة")]
    public DateTime Deadline { get; set; }

    [Display(Name = "المالك")]
    public Guid? OwnerId { get; set; }

    [MaxLength(32)]
    [Display(Name = "الحالة")]
    public string Status { get; set; } = "Open"; // Open/InReview/Resolved/Escalated

    [MaxLength(1000)]
    [Display(Name = "الأثر")]
    public string? Impact { get; set; }

    [MaxLength(2000)]
    [Display(Name = "الوصف")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Must remain false — collective grievances are never downgraded to individual path.</summary>
    public bool RoutedToIndividualPath { get; set; }
}
