using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

/// <summary>HCM-004: versioned assessment criteria per requisition.</summary>
public class RecruitmentAssessmentCriteria : AuditableEntity
{
    [Display(Name = "طلب الشاغر")]
    public Guid RequisitionId { get; set; }

    public virtual RecruitmentRequisition? Requisition { get; set; }

    [Display(Name = "رقم الإصدار")]
    public int VersionNumber { get; set; } = 1;

    [Required, StringLength(200)]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "معايير JSON")]
    public string CriteriaJson { get; set; } = "[]";

    [Display(Name = "ساري من")]
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow;

    /// <summary>HCM-026: exactly one active version per requisition.</summary>
    [Display(Name = "نشط")]
    public bool IsActive { get; set; }

    /// <summary>HCM-026: set on the first submitted score; locked versions are never edited.</summary>
    [Display(Name = "تاريخ القفل")]
    public DateTime? LockedAt { get; set; }

    [StringLength(500)]
    [Display(Name = "سبب التغيير")]
    public string? ChangeReason { get; set; }

    [Display(Name = "اعتمد بواسطة")]
    public Guid? ApprovedBy { get; set; }
}
