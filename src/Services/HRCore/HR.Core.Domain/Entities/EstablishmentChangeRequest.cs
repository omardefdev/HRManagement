using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Legal;

namespace HR.Core.Domain.Entities;

public enum EstablishmentChangeType
{
    [Display(Name = "إنشاء دليل وظيفة")]
    CreateJobCatalog,
    [Display(Name = "استيراد جماعي لدليل الوظائف")]
    BulkCreateJobCatalog,
    [Display(Name = "تعديل دليل وظيفة")]
    UpdateJobCatalog,
    [Display(Name = "تعطيل دليل وظيفة")]
    DeactivateJobCatalog,
    [Display(Name = "إنشاء منصب ممول")]
    CreateFundedPosition,
    [Display(Name = "تجميد منصب")]
    FreezeFundedPosition,
    [Display(Name = "إلغاء تجميد منصب")]
    UnfreezeFundedPosition
}

public enum EstablishmentChangeStatus
{
    [Display(Name = "مسودة")]
    Draft,
    [Display(Name = "بانتظار الاعتماد")]
    PendingApproval,
    [Display(Name = "معتمد")]
    Approved,
    [Display(Name = "مرفوض")]
    Rejected,
    [Display(Name = "مطبّق")]
    Applied,
    [Display(Name = "ملغى")]
    Cancelled
}

/// <summary>EST-002: governed change request for establishment catalog and funded positions.</summary>
public class EstablishmentChangeRequest : AuditableEntity
{
    [Required]
    [StringLength(50)]
    [Display(Name = "رقم الطلب")]
    public string RequestNumber { get; set; } = string.Empty;

    [Display(Name = "نوع التغيير")]
    public EstablishmentChangeType ChangeType { get; set; }

    [Display(Name = "الحالة")]
    public EstablishmentChangeStatus Status { get; set; } = EstablishmentChangeStatus.Draft;

    [Required]
    public string PayloadJson { get; set; } = "{}";

    [Display(Name = "الكيان المستهدف")]
    public Guid? TargetEntityId { get; set; }

    [StringLength(100)]
    [Display(Name = "مرجع قانوني")]
    public string? LegalSourceId { get; set; }

    public virtual LegislationSource? LegalSource { get; set; }

    [StringLength(100)]
    [Display(Name = "مرجع القرار")]
    public string? DecisionReference { get; set; }

    [StringLength(500)]
    [Display(Name = "مبررات")]
    public string? JustificationNotes { get; set; }

    [Display(Name = "معرف سير العمل")]
    public Guid? WorkflowInstanceId { get; set; }

    [Display(Name = "تاريخ التطبيق")]
    public DateTime? AppliedAt { get; set; }

    [Display(Name = "طبّق بواسطة")]
    public Guid? AppliedBy { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "الكيان الناتج")]
    public Guid? ResultEntityId { get; set; }

    [Display(Name = "تاريخ النفاذ")]
    public DateTime? EffectiveFrom { get; set; }
}
