using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class WorkforcePlan : AuditableEntity
{
    [Required]
    [Display(Name = "العنوان")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "السنة المالية")]
    public int FiscalYear { get; set; }

    [Display(Name = "الوحدة التنظيمية")]
    public Guid? OrgUnitId { get; set; }

    [Display(Name = "ملخص")]
    public string? Summary { get; set; }

    [Display(Name = "الحالة")]
    [StringLength(32)]
    public string Status { get; set; } = "draft";

    [Display(Name = "تاريخ الاعتماد")]
    public DateTime? ApprovedAt { get; set; }

    public Guid? ApprovedBy { get; set; }

    [StringLength(100)]
    public string? LegalSourceId { get; set; }

    public virtual OrgUnit? OrgUnit { get; set; }

    public virtual ICollection<WorkforcePlanLine> Lines { get; set; } = new List<WorkforcePlanLine>();
}
