using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

public class ApprovalRequest : AuditableEntity
{
    [Display(Name = "نوع الكيان")]
    [Required]
    [StringLength(100)]
    public string EntityType { get; set; } = string.Empty;

    [Display(Name = "معرّف الكيان")]
    [Required]
    public Guid EntityId { get; set; }

    [Display(Name = "وصف الطلب")]
    [StringLength(500)]
    public string? Description { get; set; }

    [Display(Name = "مقدّم الطلب")]
    public Guid? RequestedBy { get; set; }

    [Display(Name = "اسم مقدّم الطلب")]
    [StringLength(200)]
    public string? RequestedByName { get; set; }

    [Display(Name = "تاريخ الطلب")]
    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    [Display(Name = "مستوى الموافقة الحالي")]
    public int CurrentLevel { get; set; } = 1;

    [Display(Name = "المراجع")]
    public Guid? ReviewedBy { get; set; }

    [Display(Name = "اسم المراجع")]
    [StringLength(200)]
    public string? ReviewedByName { get; set; }

    [Display(Name = "تاريخ المراجعة")]
    public DateTime? ReviewedAt { get; set; }

    [Display(Name = "ملاحظات المراجعة")]
    public string? ReviewComments { get; set; }

    [Display(Name = "البيانات المؤقتة (JSON)")]
    public string? PayloadJson { get; set; }

    public virtual ICollection<ApprovalStep> Steps { get; set; } = new List<ApprovalStep>();
}
