using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace Compliance.Domain.Entities;

/// <summary>TRN-002: documented worker/union engagement before a rollout wave.</summary>
[Display(Name = "إشراك نقابي")]
public class UnionEngagement : AuditableEntity
{
    [Required]
    [MaxLength(64)]
    [Display(Name = "معرّف الموجة")]
    public string WaveId { get; set; } = string.Empty;

    [Display(Name = "تاريخ الإشراك")]
    public DateTime EngagementDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    [Display(Name = "المشاركون")]
    public string Participants { get; set; } = string.Empty;

    [MaxLength(1000)]
    [Display(Name = "المواضيع")]
    public string Topics { get; set; } = string.Empty;

    [MaxLength(2000)]
    [Display(Name = "التحفظات")]
    public string? Objections { get; set; }

    [MaxLength(32)]
    [Display(Name = "حالة القرار")]
    public string ResolutionStatus { get; set; } = "Pending"; // Pending/Agreed/Conditional/Escalated/Exempted

    [MaxLength(1000)]
    [Display(Name = "أثر على النطاق")]
    public string? ScopeImpact { get; set; }

    [MaxLength(200)]
    [Display(Name = "مرجع الإعفاء")]
    public string? ExemptionAuthorityRef { get; set; }
}
