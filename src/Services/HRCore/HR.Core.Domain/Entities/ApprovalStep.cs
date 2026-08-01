using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;
using HRManagement.SharedKernel.Enums;

namespace HR.Core.Domain.Entities;

public class ApprovalStep : BaseEntity
{
    [Display(Name = "طلب الموافقة")]
    [Required]
    public Guid ApprovalRequestId { get; set; }

    [Display(Name = "المستوى")]
    [Required]
    public int Level { get; set; }

    [Display(Name = "الدور المطلوب")]
    [Required]
    [StringLength(100)]
    public string RoleRequired { get; set; } = string.Empty;

    [Display(Name = "الحالة")]
    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    [Display(Name = "المراجع")]
    public Guid? ReviewedBy { get; set; }

    [Display(Name = "اسم المراجع")]
    [StringLength(200)]
    public string? ReviewedByName { get; set; }

    [Display(Name = "تاريخ المراجعة")]
    public DateTime? ReviewedAt { get; set; }

    [Display(Name = "ملاحظات")]
    public string? Comments { get; set; }

    public virtual ApprovalRequest? ApprovalRequest { get; set; }
}
