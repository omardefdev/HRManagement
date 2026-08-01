using System.ComponentModel.DataAnnotations;
using HRManagement.SharedKernel.Entities;

namespace HR.Core.Domain.Entities;

public class EmploymentStatusHistory : BaseEntity
{
    [Display(Name = "الموظف")]
    [Required]
    public Guid EmployeeId { get; set; }

    [Display(Name = "الحالة السابقة")]
    [StringLength(50)]
    public string? OldStatus { get; set; }

    [Display(Name = "الحالة الجديدة")]
    [Required]
    [StringLength(50)]
    public string NewStatus { get; set; } = string.Empty;

    [Display(Name = "تم بواسطة")]
    public Guid? ChangedBy { get; set; }

    [Display(Name = "تاريخ التغيير")]
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    [Display(Name = "السبب")]
    public string? Reason { get; set; }

    [Display(Name = "سجل التوظيف المرتبط")]
    public Guid? EmploymentRecordId { get; set; }

    public virtual Employee? Employee { get; set; }
    public virtual EmploymentRecord? EmploymentRecord { get; set; }
}
